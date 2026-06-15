# ARBETSLOGG

Löpande anteckningar under bygget. Råmaterial till reflektionen.
Tre frågor att ha i bakhuvudet när du skriver:
- Vad funkade?
- Vad funkade inte / vad skulle du gjort annorlunda?
- När var AI rätt verktyg, och när var det inte det?

Skriv kort, skriv ofta. Behöver inte vara snyggt.

---

Stod mellan EF Core eller Dapper och jag valde Dapper. Vektorsökningen är ändå rå SQL.

Embeddings och Whisper körs lokalt istället för OpenAI API. Kostnad + integritet: Mikaels råmaterial lämnar aldrig maskinen. Lite mer setup, men värt det.

Scaffold av API:t (Program.cs, request-modeller, tomma IngestService/GenerateService, db/schema.sql). Endpoints returnerar 501 tills logiken byggs. Valde att läsa DATABASE_URL från .env med en liten egen parser i Program.cs istället för att dra in DotNetEnv.

