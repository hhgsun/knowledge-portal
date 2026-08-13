# MCP Quality Gates

The CI test stage runs the complete Docker-free backend suite and then publishes a separate
`mcp-quality-gates.trx` result for the release-critical MCP categories.

| Gate | Purpose |
|---|---|
| `McpConformance` | JSON-RPC request/notification behavior and invalid parameter handling |
| `McpSchema` | Tool discovery, input schema and output schema structural contract |
| `GoldenRetrieval` | Deterministic technical queries: expected source recall@5, forbidden-source exclusion, evidence/governance/security metadata |
| `DataIsolation` | API-key `only_own_content` isolation across independent keys |
| `McpSecurity` | Injection/secret corpus detection plus benign-text false-positive checks |
| `McpConcurrency` | Concurrent read-only calls complete with correlated IDs and trace IDs |

Run locally:

```powershell
cd backend/Tests
dotnet test --filter "Gate=McpConformance|Gate=McpSchema|Gate=GoldenRetrieval|Gate=DataIsolation|Gate=McpSecurity|Gate=McpConcurrency"
```

## Fidelity boundary

The automated suite uses EF Core InMemory, `FakeVectorSearchService`, `FakeEmbeddingGenerator`,
and `FakeChatClient`. It gates application behavior but does **not** claim fidelity for PostgreSQL
Turkish snowball stemming, GIN ranking, pgvector HNSW recall, query plans, or production Ollama
latency. Those require a deployed PostgreSQL/Ollama smoke environment and the existing search
diagnostics/benchmark tooling. A release must not interpret passing deterministic retrieval tests
as proof of production vector-ranking quality.

## Extending the golden set

Each golden case must define:

- a realistic technical question;
- an expected source that must appear in the top five;
- an unrelated source that must not appear;
- evidence, governance, and security metadata requirements.

Use unique marker terms so the deterministic InMemory test measures orchestration regressions,
not PostgreSQL linguistic quality. Real anonymous production queries should only be promoted to
the corpus after removing personal data and credentials.
