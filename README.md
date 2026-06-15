# Mikael Hammar RAG Assistant

A RAG-based assistant that turns a consultant's recorded client conversations into
LinkedIn post drafts **in his own voice** — grounded in his own material, not generic
AI text. Built for case *Mikael Hammar* (Inlämning 2).

See [PLAN.md](./PLAN.md) for the full spec (architecture, data model, endpoints).

## Stack

- **.NET 10** Web API (backend + business logic)
- **Postgres 17 + pgvector** (storage + vector retrieval)
- **Local embeddings** — `multilingual-e5-small` (Python sidecar)
- **Local Whisper** — `faster-whisper` (transcription, called from n8n)
- **Anthropic Claude Sonnet** — generation (the only paid component)
- **n8n** — automation flow (audio → transcript → ingest)

## Setup

```bash
# 1. Copy env template and fill in your key(s)
cp .env.example .env

# 2. Start Postgres + n8n
docker compose up -d

# 3. Run the backend
dotnet run --project src/Api
```

n8n runs at http://localhost:5678 — see PLAN.md §7 for the flow.

## Documentation

- Security analysis (OWASP LLM Top 10) — `docs/sakerhetsanalys.pdf` *(Swedish)*
- Critical reflection — `docs/reflektion.pdf` *(Swedish)*

## Note

Embeddings and transcription run locally, so Mikael's raw material never leaves the
machine. API keys are kept in `.env` (gitignored).
