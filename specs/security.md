# Security Architecture

> **⚠️ Bu dosya `AGENTS.md`'ye tabidir.** Çelişki durumunda `AGENTS.md` geçerlidir.
> RBAC Permission Matrix, Endpoint Auth Matrix → `AGENTS.md`

## Authentication Mechanisms

### JWT Bearer Tokens (Interactive Sessions)

| Property | Value |
|----------|-------|
| Algorithm | HMAC-SHA256 |
| Expiration | 1440 minutes (24 hours) |
| Issuer / Audience | `KnowledgePortal` |
| Signing key | Configured via `Jwt:Secret` in appsettings.json |
| Storage | `localStorage` (key: `"token"`) |
| Validation | Issuer, audience, lifetime, signing key all validated |

**Claims issued**:

| Claim | Source |
|-------|--------|
| `sub` | User ID |
| `email` | User email |
| `name` | User name |
| `role` (custom + ClaimTypes.Role) | User role |
| `id` (custom) | User ID |

### API Key Tokens (Programmatic Access)

| Property | Value |
|----------|-------|
| Format | `kp_` + 32 random hex characters |
| Storage | BCrypt hash in `api_keys.key_hash` column |
| Expiration | 1–365 days (default 90), checked at runtime |
| Lookup | Prefix-indexed: first 8 chars after `kp_` stored in `key_prefix` column |
| Last-used tracking | `last_used_at` updated on each successful verification |

**Verification flow** (ApiKeyMiddleware):
1. Check `X-API-Key` header with `kp_*` prefix
2. Extract 8-char prefix after `kp_`
3. Query database by indexed `key_prefix` column (O(1) lookup)
4. Skip expired keys
5. BCrypt-verify raw key against matched hash
6. On match: set `HttpContext.User` with claims + `source: "api-key"` discriminator

### Azure AD (Microsoft Entra ID)

| Property | Value |
|----------|-------|
| Library (Frontend) | `@azure/msal-browser` + `@azure/msal-react` |
| Flow | MSAL popup/silent → Azure access token → backend validation |
| Validation | Backend calls Microsoft Graph `/me` with the access token |
| User linking | Matches by `AzureObjectId` first, then by email |
| Auto-create | If no match, creates new viewer user from Azure profile |
| Profile sync | Display name synced from Azure on each login |
| Config | `AzureAd:Enabled`, `AzureAd:TenantId`, `AzureAd:ClientId` in appsettings.json |
| Frontend config | `VITE_AZURE_CLIENT_ID`, `VITE_AZURE_TENANT_ID` in `.env` |
| Silent login | Login page auto-attempts silent auth if user has active Azure session |

**Flow (MSAL v5 redirect-bridge pattern)**:
1. Frontend calls `msalInstance.acquireTokenPopup({ scopes: ["User.Read"] })`
2. Popup opens → Azure AD login → redirects to `/auth-popup-callback.html#code=...`
3. Popup page calls `broadcastResponseToMainFrame()` (from `@azure/msal-browser/redirect-bridge`)
4. Auth response broadcast to parent window via `BroadcastChannel`
5. Parent exchanges auth code for access token (PKCE)
6. Frontend POSTs `{ accessToken }` to `/api/auth/azure-login`
7. Backend calls Microsoft Graph `GET /v1.0/me` with the token
8. Backend finds/creates local user by AzureObjectId or email
9. Backend returns local JWT + user object

**Azure Portal — App Registration (Deployment)**:

| Setting | Value |
|---------|-------|
| Display name | `knowledge-portal` |
| Application (client) ID | `da617abd-249a-4130-8514-8e90b010cca0` |
| Directory (tenant) ID | `83ad3b45-d663-443b-a431-2a825740c73d` |
| Supported account types | Single tenant (My organization only) |
| Platform | Single-page application (SPA) |
| Redirect URIs (dev) | `http://localhost:5173/auth-popup-callback.html`, `http://localhost:5173` |
| Redirect URIs (prod) | `https://knowledge.finagotech.com.tr/auth-popup-callback.html`, `https://knowledge.finagotech.com.tr` |
| API permissions | `Microsoft Graph → User.Read` (delegated) |
| Client credentials | Not required (SPA uses PKCE, no client secret needed) |

**Logout behavior**: `msalInstance.clearCache()` is called on logout to prevent auto-silent login on next visit.

## Authorization Model

### Role-Based Access Control (RBAC)

Three static roles with a hardcoded permission matrix in `RbacService`:

> **Full permission matrix**: See `AGENTS.md` → "RBAC Permission Matrix"

### Enforcement Points

| Mechanism | Where | Effect |
|-----------|-------|--------|
| `[Authorize]` attribute | Controller/action level | Requires authenticated user (401 if not) |
| `[RequirePermission("...")]` attribute | Controller/action level | Principal-aware permission check incl. API-key cap (403 if denied) |
| Inline RBAC checks | Inside controller actions | Principal-aware ownership checks (`RbacService.CanEditArticle(User, …)` etc.) |
| `[RequireSessionAuth]` attribute | Controller/action level | `source == "api-key"` returns 403 for session-only endpoints |

### Assistant Route Policy

`POST /api/assistant` is authenticated and exposes a fixed read-only route registry. The router may classify intent, but it cannot authorize a route or invoke a tool. `AssistantPolicyService` re-checks every selected route against the authenticated principal; analytics requires both an interactive session and `analytics:view`, exactly like `GET /api/analytics`. The search and RAG routes reuse `SearchExecutionService`, including its article visibility and API-key ownership filters.

User text is serialized as untrusted data for the optional structured classifier. The classifier has no tool access and its schema cannot return a rewritten query; retrieval always receives the original normalized user input. It has a separate bulkhead, queue timeout, circuit breaker and fingerprint-only exact decision cache. General chat is canned so it cannot invent company facts, and company answers use the existing grounded RAG validation path. No mutation or free-form SQL operation is registered; changing configuration cannot create one. The request has bounded message, classifier, total-time and tool-call budgets. `/api/capabilities` lets the authenticated UI honor the runtime backend kill switch in addition to the compile-time flag.

Assistant audit stores only a SHA-256 query fingerprint plus route/tool/timing identifiers; raw prompts and generated answers are not persisted in `assistant_interactions`. Feedback updates require ownership of the interaction. When an interaction produced a RAG query, the vote is also attached to the existing owned `search_queries` evaluation record. `Assistant:Enabled=false` is the backend kill switch; `VITE_ASSISTANT_ENABLED=false` removes frontend navigation and routing at build time.

Conversation history is an explicit separate data class: only interactive sessions can create/read/delete it, every query is owner-filtered, context is bounded to recent user messages, retention is configurable, and user/conversation deletion cascades content. Semantic answer cache never crosses a user/role/auth/API-key scope and becomes unusable on any published-corpus, review/approval/authority, prompt/retrieval/model/chunking change. Only fully grounded non-partial answers qualify. SSE never exposes pre-validation model output. Shadow routing runs asynchronously and persists only a query fingerprint.

### API Key Capability Model ("editor minus delete")

API-key principals (`source=api-key`) are capped independently of their owner's role:

- **Effective role**: owner role capped at `editor` — an admin-owned key acts as editor (no `users:manage`, `articles:edit_any/delete_any`, `api_keys:manage_any`). Editor/viewer-owned keys keep their owner's role.
- **Delete denial**: the admin-only `articles:delete_any` permission is always denied for keys.
- **Session-only DELETEs**: destructive DELETE endpoints (articles, attachments, comments, tags, lookups, featured-links) carry `[RequireSessionAuth]` as a second enforcement layer.
- **Allowed**: all reads per effective role, article create/edit/publish/archive, tag create/rename + auto-create, own-vote removal.

### Session-Only Endpoints

These endpoints explicitly reject API key authentication:

| Endpoint | Reason |
|----------|--------|
| `GET/POST/PUT/DELETE /api/admin/users` | User management is sensitive admin operation |
| `GET /api/analytics` | Analytics data should not be programmatically scraped |
| `POST /api/assistant` when routed to analytics | Route policy preserves the same session-only analytics boundary |
| `POST /api/assistant/route-preview` | Admin-only live classifier quality probe; never executes the selected tool |
| `GET/POST/DELETE /api/keys` | Prevents API key from creating/managing other API keys |
| `GET/POST/PUT/DELETE /api/admin/keys` | Admin all-user key management; same reason as above |
| `DELETE /api/articles/{id}`, `DELETE .../attachments/{id}`, `DELETE .../comments/{id}`, `DELETE /api/tags`, `DELETE /api/lookups`, `DELETE /api/featured-links` | Destructive deletes are session-only (API key capability model) |

## Password Security

| Property | Value |
|----------|-------|
| Hashing algorithm | BCrypt |
| Cost factor | 12 |
| Library | BCrypt.Net-Next 4.2.0 |
| Registration constraints | 8–128 characters |
| Admin-created user constraints | 8–128 characters |

## Rate Limiting

| Policy | Limit | Window | Endpoints |
|--------|-------|--------|-----------|
| `auth` | 10 requests | 1 minute | Login, Register |
| `search` | 30 requests | 1 minute | Search and Assistant |
| `mcp` | 60 requests | 1 minute | MCP endpoint (`/mcp`) |

Implemented via ASP.NET Core built-in `AddRateLimiter` with **partitioned** fixed-window limiters:
partition key = API key id > user id > client IP (real IPs via ForwardedHeaders behind the reverse proxy).
Each client gets its own window. Returns `429 Too Many Requests` when exceeded.

## Transport & Headers

- TLS terminates at the company reverse proxy; the app and nginx stay HTTP internally.
- `UseForwardedHeaders` (first middleware) honors `X-Forwarded-For`/`X-Forwarded-Proto` from proxies listed in `ForwardedHeaders:KnownProxies`/`KnownNetworks` config.
- HSTS (365 days) is emitted in non-Development environments on https requests.
- API responses carry `X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY`, `Referrer-Policy: no-referrer`.
- The SPA (nginx) adds the same headers plus a CSP (`default-src 'self'`; `style-src 'unsafe-inline'` for Milkdown/Crepe; MSAL endpoints allowed in `connect-src`/`frame-src`). Geist and Geist Mono are bundled with the SPA and served from the same origin; the frontend does not depend on Google Fonts or require external style/font CSP sources.

## Known Security Gaps

The following are documented security concerns in the current baseline:

The tracked deployment connection/JWT values are an explicit controlled-deployment owner decision recorded in `AGENTS.md`; they are not listed as an open security defect unless that decision is reopened.

### Critical

All critical issues have been resolved:
- ~~API key verification O(n)~~ → **Fixed**: prefix-indexed lookup (8-char prefix column)
- ~~No rate limiting~~ → **Fixed**: auth + search + mcp rate limited

### Medium

| # | Issue | Impact | Location |
|---|-------|--------|----------|
| 6 | **localStorage for JWT** — tokens stored in localStorage are accessible to any JS on the page | XSS attacks can steal tokens (accepted trade-off for SPA) | `AuthContext.tsx` |
| 8 | **No CSRF token** — SPA uses Authorization bearer tokens rather than authentication cookies | Mitigated by the bearer model and configured origin whitelist | `Program.cs` |

External reranking is disabled by default. Enabling `Reranking:External` sends only bounded candidate passages after retrieval filtering to an explicitly configured HTTPS or loopback endpoint. The integration never sends authorization tokens, falls back locally on timeout/error/invalid output, and must be covered by the organization's provider privacy agreement. Parent/neighbor expansion reuses only stored chunks from the same authorized article, source/attachment, embedding model and derived parent section; it runs after the published/filter safety recheck.

### Resolved

| # | Issue | Resolution |
|---|-------|-----------|
| 1 | API key O(n) BCrypt | Fixed: prefix-indexed lookup |
| 2 | No rate limiting | Fixed: auth + search + mcp rate limited |
| 7 | LIKE wildcard injection | Fixed: `%` and `_` escaped in all LIKE queries |
| 9 | View tracking no dedup | Fixed: 15-minute deduplication window per user/article |

### Low

| # | Issue | Impact | Location |
|---|-------|--------|----------|
| 10 | **Generic catch blocks** — some controllers swallow exceptions silently | Error masking, difficult debugging | Various controllers |
| 11 | **Default admin credentials** — `admin@finagotech.com.tr` / `1q2w3E*/` seeded automatically | Insecure if deployed without changing credentials | `DbInitializer.cs` |

## Security Controls Present

| Control | Status | Notes |
|---------|--------|-------|
| Password hashing | ✓ BCrypt cost 12 | Industry standard |
| JWT token validation | ✓ Full validation | Issuer, audience, lifetime, signing key |
| RBAC | ✓ Static permission matrix | Covers all sensitive operations |
| API key expiration | ✓ Checked at runtime | 1–365 day TTL |
| Session-only endpoint protection | ✓ Source claim check | Admin endpoints block API keys |
| Input validation | Partial | Length checks on passwords; no comprehensive input sanitization |
| CORS | ✓ Origin whitelist | `localhost:5173`, `localhost:3000` |
| External link safety | ✓ In renderer | `target="_blank"` + `rel="noopener noreferrer"` |
| Self-demotion prevention | ✓ | Admin cannot remove own admin role |
| Self-deletion prevention | ✓ | Admin cannot delete own account |

## Security Controls Absent

| Control | Status | Notes |
|---------|--------|-------|
| HTTPS enforcement | Proxy-owned | TLS terminates at the trusted reverse proxy; non-development HTTPS responses emit HSTS |
| Content Security Policy | ✓ nginx SPA | CSP is emitted by nginx; API responses use nosniff/DENY/referrer headers |
| Audit logging | Partial | Structured MCP audit and authenticated REST usage events are persisted; this is not a general-purpose SIEM |
| Account lockout | ✗ | No failed-login tracking or lockout |
| Token revocation | ✗ | No JWT blacklist or refresh token rotation |
| Input sanitization | Partial | Canonical Markdown is accepted as text; read views render through `react-markdown` rather than injecting raw HTML |
| Test coverage | ✓ backend | Authentication, RBAC/API-key caps, session-only routes, MCP security and input guards are covered by xUnit |
