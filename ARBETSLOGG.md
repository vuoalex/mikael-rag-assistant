# ARBETSLOGG

Löpande logg över bygget. Råmaterial till den kritiska reflektionen — skriv kort men
ofta. Fånga: designval och varför, var Cursor/AI hjälpte och var den behövde rättas,
problem du stötte på och hur du löste dem, och eventuella diskussioner med andra som
valt samma case (vem, om vad).

> Det här dokumentet lämnas inte in — det matar reflektionen. Var ärlig och konkret.

---

## Mall för en post

**Vad jag gjorde:**
**Beslut / varför:**
**AI:n (Cursor):** hjälpte med … / fick rättas på …
**Problem & lösning:**
**Avvikelse från Inlämning 1?** (om ja → lägg även i PLAN.md §7b)

---

## Poster

### Setup
**Vad jag gjorde:** Satte upp repo, docker-compose (Postgres 17 + pgvector, n8n),
Cursor-regel som läser PLAN.md.
**Beslut / varför:** pgvector i samma DB för att hålla retrieval som SQL och slippa en
separat vektortjänst.
**AI:n (Cursor):**
**Problem & lösning:**
**Avvikelse från Inlämning 1?** Embeddings + Whisper körs lokalt i stället för OpenAI
(kostnad + integritet). Noterat i PLAN.md §7b.
