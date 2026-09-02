# MCP Quality Gates

The CI test stage runs the complete Docker-free backend suite and then publishes a separate
`mcp-quality-gates.trx` result for the release-critical MCP categories.

| Gate | Purpose |
|---|---|
| `McpConformance` | JSON-RPC behavior, modern/legacy negotiation, routing-header validation, invalid parameters, and a real MCP 2.0 C# SDK connect/list/call flow |
| `McpSchema` | Tool discovery, bounded input schema, output schema and uniform structured error/result contract |
| `GoldenRetrieval` | Deterministic technical queries: expected source recall@5, forbidden-source exclusion, evidence/governance/security metadata |
| `PublishedCorpus` | MCP API keys see the complete published corpus across creator keys; ownership never narrows MCP knowledge |
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

After deployment, `backend/scripts/run-mcp-live-quality-gate.ps1` sends a modern
`2026-07-28` `ask_knowledge` request through `/mcp` and requires a non-error grounded answer,
evidence, RAG trace identity and HTTP trace identity from the live Ollama-backed runtime.
`run-assistant-live-routing-gate.ps1` separately verifies that the REST Assistant still terminates
only in the grounded-answer path.

## Extending the golden set

Each golden case must define:

- a realistic technical question;
- an expected source that must appear in the top five;
- an unrelated source that must not appear;
- evidence, governance, and security metadata requirements.

Use unique marker terms so the deterministic InMemory test measures orchestration regressions,
not PostgreSQL linguistic quality. Real anonymous production queries should only be promoted to
the corpus after removing personal data and credentials.
