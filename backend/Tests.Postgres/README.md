# PostgreSQL/pgvector fidelity tests

This opt-in suite validates behavior that EF Core InMemory cannot reproduce:

- database migrations create the `vector` extension and HNSW index;
- pgvector cosine ranking, metadata filtering, and HNSW query plans;
- PostgreSQL Turkish stemming, content-type filtering, and GIN query plans.

Set `RAG_FIDELITY_CONNECTION_STRING` to a PostgreSQL database with pgvector available, then run:

```powershell
$env:RAG_FIDELITY_CONNECTION_STRING = "Host=localhost;Database=knowledge_portal_fidelity;Username=postgres;Password=..."
dotnet test backend/Tests.Postgres/KnowledgePortal.Api.PostgresTests.csproj --configuration Release
```

The database user must be allowed to create schemas and run `CREATE EXTENSION IF NOT EXISTS vector`. Each test run creates a uniquely named `kp_fidelity_*` schema, runs the production migrations there, and drops only that schema when complete. Use a dedicated non-production database. Without the environment variable the tests are reported as skipped, so the normal InMemory suite remains fast and Docker-free.

Do not paste connection strings into tickets or chat transcripts. Store the CI value as a secret variable and rotate credentials immediately if a password is exposed.
