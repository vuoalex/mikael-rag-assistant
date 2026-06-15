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
    embedding    vector(1536) NOT NULL,
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
