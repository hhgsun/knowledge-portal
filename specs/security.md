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
| `GET/POST/PUT/DELETE /api/admin/keys` | Admin all-user key management; same reason as above |

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
| `search` | 30 requests | 1 minute | Search |

Implemented via ASP.NET Core built-in `AddRateLimiter` with `FixedWindowLimiter`.
Returns `429 Too Many Requests` when exceeded.

## Known Security Gaps

The following are documented security concerns in the current baseline:

### Critical

All critical issues have been resolved:
- ~~API key verification O(n)~~ → **Fixed**: prefix-indexed lookup (8-char prefix column)
- ~~No rate limiting~~ → **Fixed**: auth + search rate limited

### Medium

| # | Issue | Impact | Location |
|---|-------|--------|----------|
| 5 | **JWT secret in appsettings.json** — signing key stored in plain text in config file | Secret exposure if config file is leaked | `appsettings.json` |
| 6 | **localStorage for JWT** — tokens stored in localStorage are accessible to any JS on the page | XSS attacks can steal tokens (accepted trade-off for SPA) | `AuthContext.tsx` |
| 8 | **No CSRF protection** — SPA uses Bearer tokens (not cookies), but CORS is permissive | Mitigated by Bearer auth model | `Program.cs` |

### Resolved

| # | Issue | Resolution |
|---|-------|-----------|
| 1 | API key O(n) BCrypt | Fixed: prefix-indexed lookup |
| 2 | No rate limiting | Fixed: auth + search rate limited |
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
| HTTPS enforcement | ✗ | No HSTS or redirect-to-HTTPS |
| Content Security Policy | ✗ | No CSP headers |
| Audit logging | ✗ | No structured security event logging |
| Account lockout | ✗ | No failed-login tracking or lockout |
| Token revocation | ✗ | No JWT blacklist or refresh token rotation |
| Input sanitization | Partial | TipTap JSON accepted without schema validation |
| Test coverage | ✗ | No security tests |
