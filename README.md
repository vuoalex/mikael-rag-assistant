# Mikael Hammar RAG Assistant

A RAG-based assistant that turns a consultant's recorded client conversations into LinkedIn post drafts in his own voice - that bases its output on previous material, not generic AI text.

## Stack

- **.NET 10** Web API
- **Postgres 17 + pgvector** (storage + vector retrieval)
- **n8n** — automation flow (audio -> transcript -> /ingest)
- **OpenAI `text-embedding-3-small`** — embeddings, called from .NET
- **OpenAI Whisper** — transcription, called from n8n
- **Anthropic Claude Sonnet** — generation

## How it works

1. An n8n flow transcribes an audio file (OpenAI Whisper) and posts the text to the backend.
2. The backend chunks the text, creates embeddings (OpenAI) and stores them in Postgres with pgvector.
3. For a new client conversation, the backend retrieves the most relevant chunks and Claude Sonnet writes a draft grounded in them.

<img width="5504" height="1560" alt="image" src="https://github.com/user-attachments/assets/cf9e81d6-4088-46cf-a6bd-b9691796fd83" />


## Setup

Requires your own API keys (OpenAI + Anthropic).

```bash
# 1. Copy env template and fill in your keys
cp .env.example .env

# 2. Start Postgres + n8n
docker compose up -d

# 3. Run the database schema
Get-Content db/schema.sql | docker compose exec -T db psql -U mikael -d mikael_rag

# 4. Run the backend
dotnet run --project src/Api
```

The backend runs at http://localhost:5218, n8n at http://localhost:5678.

## Documentation

- **Security analysis & critical reflection** (Swedish) — written for a school assignment

## Note

This is a local MVP. Transcription (Whisper), embeddings, and generation all call external APIs, so client material does leave the machine at those points; only the database is local. API keys are kept in `.env` (gitignored).
