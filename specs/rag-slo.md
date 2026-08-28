# RAG Service-Level Objectives

These objectives apply to authenticated `POST /api/assistant`, Assistant streaming, and MCP
`ask_knowledge` requests on the supported single-backend deployment. Planned maintenance and caller cancellations are excluded. Prometheus rules live in
`ops/prometheus/rag-alerts.yml`; the importable Grafana dashboard is
`ops/grafana/rag-overview.json`.

| Signal | Objective | Window | Source |
|---|---:|---:|---|
| RAG availability (`success`, `refused`, or declared `partial`) | >= 99.5% | rolling 30 days | `kp_rag_requests_total` |
| End-to-end latency | p95 <= 30s; p99 <= 120s | rolling 30 days | `kp_rag_duration_ms_milliseconds` |
| Citation coverage | >= 98% average | each release dataset and rolling 7 days | evaluation runs + `kp_rag_citation_coverage` |
| Claim support/grounding | >= 95% average | each release dataset | evaluation run `groundingCoverage` |
| Refusal accuracy | >= 90% | each release dataset | evaluation run `refusalAccuracy` |
| Published-content indexing freshness | 99% indexed within 5 minutes | rolling 7 days | `kp_pending_embeddings` + indexing diagnostics |

The monthly availability error budget is 0.5%. Exhausting 50% of the budget before the middle of
the window freezes non-remediation RAG releases; exhausting 100% freezes releases until the
failure source is corrected and the live golden-dataset gate passes. A release is not promotable
when the PostgreSQL fidelity suite or the post-deploy live-model gate is skipped or fails.

Structured refusals are successful service outcomes, not availability failures. Responses rejected
by the grounding validator are tracked as refusals for availability and must still satisfy the
separate refusal-accuracy and grounding gates.
The same dashboard and alert group also cover Assistant request latency, answer/stream volume, feedback, semantic-answer-cache outcomes and audit persistence failures. Search availability is evaluated separately because it is a document-retrieval product and does not share the Assistant answer surface.
