using Api.Models;
using Api.Services;
using Dapper;
using Npgsql;
using Pgvector.Dapper;

var builder = WebApplication.CreateBuilder(args);

// Load .env (DATABASE_URL etc.) — secrets live there, never in appsettings.
LoadDotEnv(builder.Environment.ContentRootPath);

builder.Services.AddOpenApi();

// Register a single shared NpgsqlDataSource built from DATABASE_URL.
// UseVector() maps pgvector's `vector` type; the Dapper type handler lets us
// pass/read Pgvector.Vector values in Dapper queries.
var databaseUrl = Environment.GetEnvironmentVariable("DATABASE_URL")
    ?? throw new InvalidOperationException("DATABASE_URL is not set. Copy .env.example to .env.");

SqlMapper.AddTypeHandler(new VectorTypeHandler());

var dataSourceBuilder = new NpgsqlDataSourceBuilder(databaseUrl);
dataSourceBuilder.UseVector();
builder.Services.AddSingleton(dataSourceBuilder.Build());

builder.Services.AddHttpClient("anthropic", client =>
{
    client.Timeout = TimeSpan.FromSeconds(120);
});

builder.Services.AddScoped<IngestService>();
builder.Services.AddScoped<GenerateService>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

// POST /ingest — chunk → embed → store.
app.MapPost("/ingest", async (IngestRequest request, IngestService service) =>
{
    if (string.IsNullOrWhiteSpace(request.Text))
    {
        return Results.BadRequest(new { error = "Text must not be empty." });
    }

    var result = await service.IngestAsync(request);
    return Results.Ok(new { documentId = result.DocumentId, chunkCount = result.ChunkCount });
});

// POST /generate — embed query → retrieve → Claude → draft.
app.MapPost("/generate", async (GenerateRequest request, GenerateService service) =>
{
    if (string.IsNullOrWhiteSpace(request.Input))
    {
        return Results.BadRequest(new { error = "Input must not be empty." });
    }

    var result = await service.GenerateAsync(request);
    return Results.Ok(new
    {
        draft = result.Draft,
        usedChunks = result.UsedChunks,
        flagged = result.Flagged,
    });
});

// GET /health — liveness check: DB reachable + required keys present.
app.MapGet("/health", async (NpgsqlDataSource dataSource) =>
{
    var keysPresent = Environment.GetEnvironmentVariable("OPENAI_API_KEY") is not null
        && Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY") is not null;

    bool dbReachable;
    try
    {
        await using var connection = await dataSource.OpenConnectionAsync();
        dbReachable = await connection.ExecuteScalarAsync<int>("SELECT 1") == 1;
    }
    catch
    {
        dbReachable = false;
    }

    var healthy = keysPresent && dbReachable;
    var payload = new { status = healthy ? "ok" : "degraded", dbReachable, keysPresent };

    return healthy
        ? Results.Ok(payload)
        : Results.Json(payload, statusCode: StatusCodes.Status503ServiceUnavailable);
});

app.Run();

// Minimal .env loader: walk up from the content root and set any KEY=VALUE pairs
// not already present in the environment. Avoids an extra package dependency.
static void LoadDotEnv(string startPath)
{
    var dir = new DirectoryInfo(startPath);
    while (dir is not null)
    {
        var envPath = Path.Combine(dir.FullName, ".env");
        if (File.Exists(envPath))
        {
            foreach (var rawLine in File.ReadAllLines(envPath))
            {
                var line = rawLine.Trim();
                if (line.Length == 0 || line.StartsWith('#'))
                {
                    continue;
                }

                var separator = line.IndexOf('=');
                if (separator <= 0)
                {
                    continue;
                }

                var key = line[..separator].Trim();
                var value = line[(separator + 1)..].Trim();

                if (Environment.GetEnvironmentVariable(key) is null)
                {
                    Environment.SetEnvironmentVariable(key, value);
                }
            }

            return;
        }

        dir = dir.Parent;
    }
}
