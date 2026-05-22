# Security Architecture

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
| Permissions | JSON array in `permissions` column; default `["articles:read", "search"]` |
| Last-used tracking | `last_used_at` updated on each successful verification |

**Verification flow** (ApiKeyMiddleware):
1. Check `Authorization: Bearer kp_*` pattern
2. Load ALL API keys from database
3. Skip expired keys
4. BCrypt-verify raw key against each hash (O(n) scan)
5. On match: set `HttpContext.User` with claims + `source: "api-key"` discriminator

## Authorization Model

### Role-Based Access Control (RBAC)

Three static roles with a hardcoded permission matrix in `RbacService`:

| Permission | admin | editor | viewer |
|-----------|:-----:|:------:|:------:|
| `articles:create` | ✓ | ✓ | |
| `articles:edit_own` | ✓ | ✓ | |
| `articles:edit_any` | ✓ | | |
| `articles:delete_own` | ✓ | ✓ | |
| `articles:delete_any` | ✓ | | |
| `articles:publish` | ✓ | ✓ | |
| `articles:archive` | ✓ | ✓ | |
| `tags:manage` | ✓ | ✓ | |
| `users:manage` | ✓ | | |
| `analytics:view` | ✓ | ✓ | |
| `api_keys:manage` | ✓ | | |

### Enforcement Points

| Mechanism | Where | Effect |
|-----------|-------|--------|
| `[Authorize]` attribute | Controller/action level | Requires authenticated user (401 if not) |
| `[RequirePermission("...")]` attribute | Controller/action level | Checks role→permission matrix (403 if denied) |
| Inline RBAC checks | Inside controller actions | Ownership checks (e.g., article owner or `edit_any`) |
| API key source rejection | Controller actions | `User.GetSource() == "api-key"` returns 403 for session-only endpoints |

### Session-Only Endpoints

These endpoints explicitly reject API key authentication:

| Endpoint | Reason |
|----------|--------|
| `GET/POST/PUT/DELETE /api/admin/users` | User management is sensitive admin operation |
| `GET /api/analytics` | Analytics data should not be programmatically scraped |
| `GET/POST/DELETE /api/keys` | Prevents API key from creating/managing other API keys |

## Password Security

| Property | Value |
|----------|-------|
| Hashing algorithm | BCrypt |
| Cost factor | 12 |
| Library | BCrypt.Net-Next 4.2.0 |
| Registration constraints | 8–128 characters |
| Admin-created user constraints | 6–100 characters |

## Known Security Gaps

The following are documented security concerns in the current baseline:

### Critical

| # | Issue | Impact | Location |
|---|-------|--------|----------|
| 1 | **API key verification is O(n)** — middleware iterates all keys with BCrypt compare | DoS vector: each request triggers N BCrypt operations | `ApiKeyMiddleware.cs` |
| 2 | **No rate limiting** — login, register, search, and API key creation have no request throttling | Brute-force attacks, credential stuffing, resource exhaustion | All controllers |
| 3 | **Password length inconsistency** — registration requires 8 chars, admin user creation requires 6 | Policy confusion, weaker admin-created passwords | `AuthController`, `AdminUsersController` |

### Medium

| # | Issue | Impact | Location |
|---|-------|--------|----------|
| 4 | **SQLite no WAL mode** — no write-ahead logging or busy timeout configured | Concurrent write failures under load | `AppDbContext` / connection string |
| 5 | **JWT secret in appsettings.json** — signing key stored in plain text in config file | Secret exposure if config file is leaked | `appsettings.json` |
| 6 | **localStorage for JWT** — tokens stored in localStorage are accessible to any JS on the page | XSS attacks can steal tokens | `AuthContext.tsx` |
| 7 | **LIKE injection** — search queries use SQL LIKE without escaping `%` and `_` wildcards | Unexpected search behavior, minor data leakage | `ArticlesController`, `AdminUsersController`, `SearchController` |
| 8 | **No CSRF protection** — SPA uses Bearer tokens (not cookies), but CORS is permissive | Mitigated by Bearer auth model, but CORS allows multiple origins | `Program.cs` |

### Low

| # | Issue | Impact | Location |
|---|-------|--------|----------|
| 9 | **View tracking on every GET** — no deduplication or debouncing | Inflated analytics; disk usage growth | `ArticlesController` |
| 10 | **Generic catch blocks** — some controllers swallow exceptions silently | Error masking, difficult debugging | Various controllers |
| 11 | **Default admin credentials** — `admin@knowledge.local` / `admin123` seeded automatically | Insecure if deployed without changing credentials | `DbInitializer.cs` |

## Security Controls Present

| Control | Status | Notes |
|---------|--------|-------|
| Password hashing | ✓ BCrypt cost 12 | Industry standard |
| JWT token validation | ✓ Full validation | Issuer, audience, lifetime, signing key |
| RBAC | ✓ Static permission matrix | Covers all sensitive operations |
| API key expiration | ✓ Checked at runtime | 1–365 day TTL |
| API key scope limiting | ✓ Permission array | Default: read-only |
| Session-only endpoint protection | ✓ Source claim check | Admin endpoints block API keys |
| Input validation | Partial | Length checks on passwords; no comprehensive input sanitization |
| CORS | ✓ Origin whitelist | `localhost:5173`, `localhost:3000` |
| External link safety | ✓ In renderer | `target="_blank"` + `rel="noopener noreferrer"` |
| Self-demotion prevention | ✓ | Admin cannot remove own admin role |
| Self-deletion prevention | ✓ | Admin cannot delete own account |

## Security Controls Absent

| Control | Status | Notes |
|---------|--------|-------|
| Rate limiting | ✗ | No request throttling on any endpoint |
| HTTPS enforcement | ✗ | No HSTS or redirect-to-HTTPS |
| Content Security Policy | ✗ | No CSP headers |
| Audit logging | ✗ | No structured security event logging |
| Account lockout | ✗ | No failed-login tracking or lockout |
| Token revocation | ✗ | No JWT blacklist or refresh token rotation |
| Input sanitization | Partial | TipTap JSON accepted without schema validation |
| Test coverage | ✗ | No security tests |
