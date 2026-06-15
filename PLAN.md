# PLAN.md — Mikael Hammar RAG Assistant

> Project spec and build context. Read this first.
> Technical content (code, comments, commits, README) is in English.
> The two assessed documents — security analysis and critical reflection — are written in **Swedish**.

---

## 1. The case (short)

Mikael Hammar is a consultant who wants to keep an active LinkedIn presence but
has no time to write. He records his thoughts and client conversations and wants
a system that turns those into LinkedIn post drafts **that sound like him** — not
generic AI text.

The decision document (Inlämning 1) committed to a RAG architecture over Mikael's
own material, using Claude Sonnet for generation, plus an automation that
transcribes new recordings and ingests them automatically.

**MVP definition (the thing that must work):**
Feed in a client conversation → get out a LinkedIn draft in Mikael's voice that is
demonstrably better than plain ChatGPT without context.

---

## 2. What Inlämning 1 promised (the contract)

The solution should consist of four parts:

1. A .NET backend with the business logic
2. A database holding Mikael's material
3. An AI connection (Claude Sonnet) for generation
4. An automation that transcribes new recordings and ingests them

Any deviation from this is fine **if it is motivated** in the reflection.

---

## 3. Architecture

```
                    ┌─────────────────────────────┐
  Audio file        │  n8n flow (automation)      │
  (recording)  ──►  │  Manual trigger             │
                    │   → Whisper LOCAL (transcribe)│
                    │   → HTTP POST /ingest        │
                    └──────────────┬──────────────┘
                                   │ transcript text
                                   ▼
        ┌──────────────────────────────────────────────┐
        │  .NET 10 Web API backend                       │
        │                                                │
        │  POST /ingest     chunk → embed → store        │
        │  POST /generate   retrieve → Claude → draft     │
        │  GET  /health                                   │
        └───────┬─────────────────────────┬──────────────┘
                │                          │
                ▼                          ▼
   ┌────────────────────────┐   ┌──────────────────────────┐
   │  Postgres 17 + pgvector │   │  Models                   │
   │  documents              │   │  - embeddings: LOCAL e5   │
   │  chunks (vector col)    │   │  - Whisper: LOCAL         │
   │  generations (log)      │   │  - Anthropic Claude (paid)│
   └────────────────────────┘   └──────────────────────────┘

   Only paid component: Claude Sonnet. Embeddings + transcription run locally.
```

### Components
- **Backend:** .NET 10 Web API (minimal API is fine)
- **Database:** Postgres 17 with the `pgvector` extension, in Docker
- **Embeddings:** local `intfloat/multilingual-e5-small` (384 dims) — runs on your
  machine, good at Swedish, no API cost
- **Generation:** Anthropic Claude Sonnet (`claude-sonnet-4-6`) — the only paid part
- **Transcription:** Whisper running locally (e.g. `faster-whisper`, `small`/`base`
  model), called from n8n. No API cost; audio never leaves the machine.
- **Automation:** n8n (Docker), reusing the Flöde-A pattern from V3D1

### Why pgvector
Keeps retrieval as a SQL query inside the one database we already run — no separate
vector service (Qdrant/Pinecone). Matches the decision doc (data in our own DB, low
lock-in) and is one fewer moving part for a 3-day build.

---

## 4. Data model

```sql
CREATE EXTENSION IF NOT EXISTS vector;

CREATE TABLE documents (
    id          uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    source      text NOT NULL,          -- 'transcript', 'manual', etc.
    title       text,
    raw_text    text NOT NULL,
    created_at  timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE chunks (
    id           uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    document_id  uuid NOT NULL REFERENCES documents(id) ON DELETE CASCADE,
    chunk_index  int  NOT NULL,
    content      text NOT NULL,
    embedding    vector(384) NOT NULL,
    token_count  int
);

-- approximate nearest-neighbour index (cosine)
CREATE INDEX ON chunks USING hnsw (embedding vector_cosine_ops);

-- log every generation for the reflection + traceability
CREATE TABLE generations (
    id              uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    input_query     text NOT NULL,
    retrieved_ids   uuid[] NOT NULL,
    draft_text      text NOT NULL,
    flagged         boolean NOT NULL DEFAULT false,  -- prompt-injection flag
    created_at      timestamptz NOT NULL DEFAULT now()
);
```

---

## 5. API endpoints

### POST /ingest
Accepts transcript text + metadata. Chunks it, embeds each chunk, stores document
and chunks. This is the endpoint n8n calls after Whisper.
```
Request:  { "source": "transcript", "title": "...", "text": "..." }
Response: { "documentId": "...", "chunkCount": 7 }
```

### POST /generate
The core endpoint. Takes a client conversation / prompt, embeds it, retrieves the
top-k nearest chunks via pgvector, builds a prompt with that context, calls Claude,
returns a LinkedIn draft. Logs to `generations`.
```
Request:  { "input": "...client conversation or topic..." }
Response: { "draft": "...", "usedChunks": [ ... ], "flagged": false }
```

### GET /health
Liveness check (DB reachable, keys present).

---

## 6. RAG pipeline details

- **Chunking:** by paragraph, target ~400–600 tokens, small overlap (~50). Keep it
  simple; transcripts are conversational so paragraph splits work well.
- **Embedding:** local `multilingual-e5-small`, 384 dims. Note the e5 quirk: prefix
  passages with `passage: ` and queries with `query: ` before embedding — the model
  was trained that way and skipping it hurts retrieval quality.
- **Retrieval:** cosine distance (`<=>`), top-k = 5. Tune if drafts feel thin.
- **Generation prompt:** system prompt instructs Claude to write a LinkedIn post in
  Mikael's voice, grounded **only** in the retrieved context, no invented facts.
  The retrieved chunks are wrapped in tags and explicitly marked as source material,
  not instructions (see security below).

---

## 7. n8n automation flow

MVP (demo-friendly): **Manual trigger → read audio file → local Whisper → HTTP POST /ingest**

Reuse the Flöde-A pattern from V3D1. Mappbevakning (watch a folder / Dropbox) is a
nice-to-have for after the deadline — manual trigger is enough to demo the loop.

---

## 7b. Deviations from Inlämning 1 (must be motivated in the reflection)

Running list. Anything here that differs from the decision document needs a written
justification in the Swedish reflection — the teacher allows changes *if motivated*.

- **Transcription + embeddings moved local.** The decision doc referenced OpenAI for
  transcription. Now: local Whisper (`faster-whisper`) and local embeddings
  (`multilingual-e5-small`). *Motivation:* lower/zero cost, and Mikael's raw material
  never leaves the machine (privacy). Trade-off: slightly more setup, model weights
  downloaded once.
- _(add further deviations here as they come up)_

---

## 8. Security — OWASP LLM Top 10 mapping (for the analysis)

The recorded transcripts are **untrusted input** — someone could embed instructions
in a recording. This is the same threat modeled in the V3D1 exercise, reused here.

- **LLM01 Prompt Injection** — transcript content is wrapped in tags and marked as
  material-to-use, never instructions to follow. A detection step (regex from V3D1)
  flags suspicious text and routes it for manual review instead of generating.
- **LLM02 Sensitive Information Disclosure** — API keys live in `.env` / n8n
  credentials, never in code or the public repo. `.gitignore` covers `.env`. Running
  embeddings and Whisper locally also means Mikael's raw material never leaves the
  machine — a real privacy gain to call out, not just key hygiene.
- **LLM06 Excessive Agency / human-in-the-loop** — nothing is published
  automatically. The system produces a *draft*; Mikael approves before anything goes
  to LinkedIn. The architecture, not a warning box, enforces this.
- (Add 1–2 more from the V4 material once it lands — e.g. output handling,
  unbounded consumption / cost.)

---

## 9. Build phases (milestones, not a timetable)

Ordered by dependency — each phase builds on the previous. No calendar attached;
done is done.

**Phase 1 — Foundation.** Docker up (Postgres 17 + pgvector), .NET 10 project, data
model, ingest pipeline (chunk → embed → store). *Done when:* test data is in and rows
are visible in the DB.

**Phase 2 — The brain.** Retrieval + /generate endpoint (query → nearest chunks →
Claude → draft). *Done when:* a client conversation in produces a Mikael-voice draft
out. This proves the whole point of the case.

**Phase 3 — The frame.** n8n transcription flow (local Whisper → /ingest), README,
and a draft of both assessed documents (security analysis + reflection). *Done when:*
the loop runs end to end and the docs exist in draft.

**Phase 4 — Polish.** Finish and refine the assessed documents, tighten the demo,
fill in the OWASP points from the V4 material.

---

## 10. Working notes / non-negotiables

- Keep a running `ARBETSLOGG.md` from commit one: design choices, where Cursor
  helped vs. where it had to be corrected, problems hit. This is the raw material for
  the reflection and cannot be reconstructed later.
- Secrets in `.env` only. Add `.env` to `.gitignore` before the first commit.
- The core that must not slip: RAG producing a Mikael-voice draft that beats plain
  ChatGPT. Everything else is negotiable under time pressure.

---

## 11. Commit convention (conventional commits)

Format: `type: description` — imperative mood ("add", not "added"), subject under
~50 chars, details in the body if needed.

Common types:
- `feat:` — new functionality
- `fix:` — bug fix
- `docs:` — documentation
- `refactor:` — restructure without behaviour change
- `chore:` — deps, config, tooling
- `test:` — tests

Examples for this project:
- `chore: set up postgres 17 + pgvector via docker compose`
- `feat: add ingest endpoint with chunking and embeddings`
- `feat: add /generate with pgvector retrieval and claude`
- `fix: prevent transcript text from being read as instructions`
- `docs: add OWASP LLM mapping to security analysis`

Keep commits small and thematic — the history doubles as a visible build log to cite
in the reflection ("incremental, here is where I had to backtrack").
