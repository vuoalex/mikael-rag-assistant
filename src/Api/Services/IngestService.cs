using System.ClientModel;
using System.Text;
using System.Text.RegularExpressions;
using Api.Models;
using Dapper;
using Npgsql;
using OpenAI.Embeddings;
using Pgvector;

namespace Api.Services;

/// <summary>
/// Ingest pipeline: chunk transcript text by paragraph, embed each chunk with
/// OpenAI text-embedding-3-small (1536 dims), and store the document and chunks
/// in Postgres/pgvector via Dapper.
/// </summary>
public class IngestService
{
    private const string EmbeddingModel = "text-embedding-3-small";

    // Character targets for a chunk. Paragraphs are packed up to the max; a small
    // overlap (the last sentence of the previous chunk) is carried into the next
    // one so context isn't lost at the boundary. See PLAN.md §6.
    private const int TargetMaxChars = 600;
    private const int OverlapMaxChars = 200;

    private static readonly Regex ParagraphSplitter = new(@"\n\s*\n", RegexOptions.Compiled);
    private static readonly Regex SentenceSplitter = new(@"(?<=[.!?])\s+", RegexOptions.Compiled);

    private const string InsertDocumentSql = """
        INSERT INTO documents (source, title, raw_text)
        VALUES (@Source, @Title, @RawText)
        RETURNING id;
        """;

    private const string InsertChunkSql = """
        INSERT INTO chunks (document_id, chunk_index, content, embedding, token_count)
        VALUES (@DocumentId, @ChunkIndex, @Content, @Embedding, @TokenCount);
        """;

    private readonly NpgsqlDataSource _dataSource;
    private readonly EmbeddingClient _embeddingClient;

    public IngestService(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;

        var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY")
            ?? throw new InvalidOperationException(
                "OPENAI_API_KEY is not set. Copy .env.example to .env and fill it in.");

        _embeddingClient = new EmbeddingClient(EmbeddingModel, apiKey);
    }

    public async Task<IngestResult> IngestAsync(IngestRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Text))
        {
            throw new ArgumentException("Text must not be empty.", nameof(request));
        }

        var chunks = ChunkText(request.Text);

        // Batch all chunks into a single embeddings request — one round-trip,
        // and the response preserves input order.
        ClientResult<OpenAIEmbeddingCollection> response =
            await _embeddingClient.GenerateEmbeddingsAsync(chunks, cancellationToken: cancellationToken);
        OpenAIEmbeddingCollection embeddings = response.Value;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var documentId = await connection.ExecuteScalarAsync<Guid>(new CommandDefinition(
            InsertDocumentSql,
            new { request.Source, request.Title, RawText = request.Text },
            transaction,
            cancellationToken: cancellationToken));

        for (var i = 0; i < chunks.Count; i++)
        {
            var content = chunks[i];
            var embedding = new Vector(embeddings[i].ToFloats());

            await connection.ExecuteAsync(new CommandDefinition(
                InsertChunkSql,
                new
                {
                    DocumentId = documentId,
                    ChunkIndex = i,
                    Content = content,
                    Embedding = embedding,
                    TokenCount = EstimateTokens(content),
                },
                transaction,
                cancellationToken: cancellationToken));
        }

        await transaction.CommitAsync(cancellationToken);

        return new IngestResult(documentId, chunks.Count);
    }

    /// <summary>
    /// Split text into chunks by paragraph, packing paragraphs up to
    /// <see cref="TargetMaxChars"/> and carrying the previous chunk's last
    /// sentence into the next as overlap. Oversized paragraphs fall back to
    /// sentence-level packing.
    /// </summary>
    internal static List<string> ChunkText(string text)
    {
        var normalized = text.Replace("\r\n", "\n").Replace("\r", "\n");
        var paragraphs = ParagraphSplitter.Split(normalized)
            .Select(p => p.Trim())
            .Where(p => p.Length > 0)
            .ToList();

        var chunks = new List<string>();
        var current = new StringBuilder();

        foreach (var paragraph in paragraphs)
        {
            // A single paragraph bigger than the target can't be packed whole;
            // flush what we have and split it by sentence instead.
            if (paragraph.Length > TargetMaxChars)
            {
                FlushInto(chunks, current);
                PackSentences(paragraph, chunks);
                continue;
            }

            var separator = current.Length > 0 ? "\n\n" : string.Empty;
            if (current.Length + separator.Length + paragraph.Length <= TargetMaxChars)
            {
                current.Append(separator).Append(paragraph);
            }
            else
            {
                var flushed = current.ToString();
                chunks.Add(flushed);
                current.Clear();

                var overlap = LastSentence(flushed);
                if (overlap.Length > 0)
                {
                    current.Append(overlap).Append("\n\n");
                }
                current.Append(paragraph);
            }
        }

        FlushInto(chunks, current);
        return chunks;
    }

    private static void PackSentences(string paragraph, List<string> chunks)
    {
        var sentences = SentenceSplitter.Split(paragraph)
            .Select(s => s.Trim())
            .Where(s => s.Length > 0);

        var current = new StringBuilder();
        foreach (var sentence in sentences)
        {
            var separator = current.Length > 0 ? " " : string.Empty;

            // Always keep at least one sentence per chunk, even if a single
            // sentence exceeds the target — otherwise we'd loop forever.
            if (current.Length == 0 || current.Length + separator.Length + sentence.Length <= TargetMaxChars)
            {
                current.Append(separator).Append(sentence);
            }
            else
            {
                var flushed = current.ToString();
                chunks.Add(flushed);
                current.Clear();

                var overlap = LastSentence(flushed);
                if (overlap.Length > 0)
                {
                    current.Append(overlap).Append(' ');
                }
                current.Append(sentence);
            }
        }

        FlushInto(chunks, current);
    }

    private static void FlushInto(List<string> chunks, StringBuilder buffer)
    {
        if (buffer.Length > 0)
        {
            chunks.Add(buffer.ToString());
            buffer.Clear();
        }
    }

    private static string LastSentence(string text)
    {
        var sentences = SentenceSplitter.Split(text)
            .Select(s => s.Trim())
            .Where(s => s.Length > 0)
            .ToList();

        var last = sentences.Count > 0 ? sentences[^1] : text.Trim();

        // Guard against a runaway overlap when "sentences" are very long
        // (e.g. transcripts with little punctuation).
        if (last.Length > OverlapMaxChars)
        {
            last = last[^OverlapMaxChars..];
        }

        return last;
    }

    // Rough heuristic — we don't run a real tokenizer. Good enough for the
    // token_count column, which is informational only.
    private static int EstimateTokens(string content) => Math.Max(1, content.Length / 4);
}
