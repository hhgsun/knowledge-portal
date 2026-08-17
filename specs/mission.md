# Mission

> **⚠️ Bu dosya `AGENTS.md`'ye tabidir.** Çelişki durumunda `AGENTS.md` geçerlidir.

**Knowledge Portal** is an internal knowledge-base platform that enables teams to author, version, search, and review documentation through a rich-text editor and a RESTful API.

## Target Audience

| Segment | Description |
|---------|-------------|
| **Primary** | Engineering and operations teams who need a single, searchable source of truth for reference docs, how-to guides, runbooks, ADRs, and onboarding material. |
| **Secondary** | Automated systems (CI/CD, bots, scripts) that create or query articles programmatically via API keys. |

## Success Metrics

1. **Knowledge coverage** — ≥ 80 % of "top 20 search queries" return at least one published article (measured via the analytics `failedSearches` / `topSearches` ratio).
2. **Content freshness** — ≤ 10 % of published articles are stale (> 90 days since last review), tracked by the `staleArticles` counter on the analytics dashboard.

## Core Value Propositions

- **Author once, find everywhere** — full-text and tag-based search surface content across content types (reference, how-to, ADR, runbook, FAQ, policy, onboarding).
- **Version everything** — every edit creates an immutable version with author attribution and optional change summary; any two versions can be compared.
- **Publication and review lifecycle** — articles move between `draft`, `published`, and `archived`; approval is an independent trust signal on published content, with configurable review intervals (default 90 days).
- **Dual access model** — interactive users authenticate via JWT; automated consumers authenticate via scoped, expiring API keys (`kp_` prefix).
- **Role-based access** — three roles (admin, editor, viewer) with a static permission matrix controlling create, edit, delete, publish, and administrative operations.
