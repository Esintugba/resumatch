# Authentication and sessions

Every sign-in opens a session for that device. The browser keeps a short-lived access token in memory and a refresh token in an HttpOnly cookie that scripts cannot read.

## Frontend flow

1. **Sign in.** `POST /api/auth/login` or `/register` with `credentials: 'include'`. The body returns `accessToken`, `expiresAt` and `user`; the refresh token arrives only as a cookie. Keep the access token in a module variable, never in `localStorage`, `sessionStorage` or IndexedDB.
2. **Call the API** with `Authorization: Bearer <accessToken>`. Ordinary requests do not need cookies.
3. **On a 401**, refresh once and retry the original request once. If the refresh fails, treat the user as signed out. Never loop.
4. **On page load**, call refresh. A 401 means there is no session and the sign-in screen is shown.
5. **Sign out** with `POST /api/auth/logout`, then clear the in-memory token and tell other tabs. If the request does not reach the API, sign out locally anyway and keep a `resumatch.signOutPending` flag in `localStorage` (never a token). While the flag is set, no refresh runs; the next page load, or the browser coming back online, sends the logout again and clears the flag when it succeeds. A new sign-in clears it too.

Refresh and logout need `credentials: 'include'` and the `X-ResuMatch-CSRF: 1` header:

```ts
async function refresh(): Promise<AuthResponse | null> {
  // One refresh at a time across all tabs of this origin.
  return navigator.locks.request('resumatch-refresh', async () => {
    for (let attempt = 0; attempt < 2; attempt++) {
      const response = await fetch(`${API}/api/auth/refresh`, {
        method: 'POST',
        credentials: 'include',
        headers: { 'X-ResuMatch-CSRF': '1' },
      });
      if (response.ok) return response.json();
      // Another tab just rotated the cookie; the browser now holds the new one.
      if (response.status === 409 && attempt === 0) continue;
      return null;
    }
    return null;
  });
}
```

- **Single refresh per tab:** concurrent 401s in one tab share one pending refresh promise.
- **Web Locks across tabs:** with the lock, a tab that waited usually finds the cookie already rotated and gets a fresh token at once. `refresh_token_superseded` (409) covers browsers or moments where two refreshes still overlap: retry once.
- **`BroadcastChannel('resumatch-auth')`:** announce sign-out and sign-in, so other tabs drop their in-memory token.
- **Do not retry** a 403 `csrf_rejected` or a 429. Show the error.

## Endpoints

| Endpoint | Auth | Effect |
|---|---|---|
| `POST /api/auth/register`, `POST /api/auth/login` | None | Open a session, return the access token and set the refresh cookie |
| `POST /api/auth/refresh` | Cookie + CSRF checks | Rotate the cookie and return a new access token. 401 clears the cookie; 409 `refresh_token_superseded` leaves it untouched |
| `POST /api/auth/logout` | Cookie + CSRF checks | Revoke the cookie's session and clear the cookie. Always 204 |
| `GET /api/auth/sessions` | Bearer | List active sessions with a coarse device label such as `Chrome on Windows`; the current one has `isCurrent: true` |
| `DELETE /api/auth/sessions/{id}` | Bearer | Revoke one of the caller's sessions. Its access tokens stop working at once. Another user's session returns 404 |
| `POST /api/auth/logout-all` | Bearer | Revoke every session and change the security stamp |

Refresh and logout need no access token, so they work after the access token has expired.

## Access tokens

JWTs that last 15 minutes (`Jwt:AccessTokenExpirationMinutes`). Besides the user identifier they carry:

| Claim | Meaning |
|---|---|
| `session` | The session the token belongs to |
| `stamp` | The user's security stamp when the token was issued |

Each request checks, in one database read, that the session exists, belongs to the user, is not revoked, has passed neither its idle nor its absolute expiry, and that the stamp still matches. Ordinary requests never write session state.

## Refresh cookie

| Attribute | Value | Why |
|---|---|---|
| Name | `__Host-resumatch_refresh` | The `__Host-` prefix makes the browser reject the cookie unless it is `Secure`, has `Path=/` and no `Domain`. A sibling subdomain therefore cannot set or shadow it. |
| `HttpOnly` | Yes | Scripts, including injected ones, cannot read the token. |
| `Secure` | Yes | HTTPS only. Browsers treat `http://localhost` as secure, so local development works unchanged. |
| `SameSite` | `Strict` | `app.alanadi.com` (or `alanadi.com`) and `api.alanadi.com` are the same site, so the cookie is sent on the app's requests and never on a request started by another site. |
| `Domain` | None | Host-only: only `api.alanadi.com` receives it, not `app.alanadi.com` or other subdomains. |
| `Path` | `/` | Required by `__Host-`. Only refresh and logout read the cookie; every other endpoint ignores it. |
| `Expires` | The session's idle expiry | Moves on each refresh and never passes the absolute expiry. |

`__Secure-` with `Path=/api/auth` was rejected: it would send the cookie to fewer endpoints, but a sibling subdomain could plant a cookie with the same name for `Domain=alanadi.com` and fix a victim into the attacker's session.

`onrender.com` is on the Public Suffix List, so a frontend on another `onrender.com` host would be a different site and `SameSite=Strict` would block the cookie. The cookie flow therefore needs the custom domain; until then the live API sets the cookie but no browser client uses it.

Responses that set or clear the cookie carry `Cache-Control: no-store`. The name can be changed with `RefreshTokenCookie:Name`, which must keep the `__Host-` prefix.

## CSRF protection

Refresh and logout are the only endpoints that act on the cookie. Each request to them must pass all of these:

1. **`SameSite=Strict`:** the browser does not attach the cookie to cross-site requests.
2. **`X-ResuMatch-CSRF: 1`:** a custom header, which a page on another origin cannot send without a CORS preflight that this API refuses.
3. **Origin:** `Origin` must exactly match an entry in `HttpSecurity:AllowedOrigins`. Without `Origin`, the origin of `Referer` must match; in production only an HTTPS `Referer` counts. With neither, the request is rejected.

A failed check returns 403 `csrf_rejected` before the request body is read, and leaves the cookie and session untouched. Rejected requests still count towards the rate limit.

Login and register set the cookie but do not read it. They accept only JSON bodies, which a cross-site form cannot send, and a cross-origin script would need a preflight.

## CORS

- `HttpSecurity:AllowCredentials=true` works only with explicit origins. A wildcard or an empty list fails at startup.
- In production every credentialed origin must use HTTPS.
- Allowed request headers: `Authorization`, `Content-Type`, `If-Match`, `X-ResuMatch-CSRF`.

## Rate limits

Per client IP, in `AuthenticationRateLimits`:

| Policy | Default |
|---|---|
| `auth-login` | 5 per minute |
| `auth-register` | 3 per 10 minutes |
| `auth-refresh` | 30 per minute (open tabs refresh about every 15 minutes, and users behind one NAT share an address) |
| `auth-logout` | 10 per minute |

The `policy` tag of `resumatch.security.rate_limited` takes only these names or `unknown`.

## Sessions and rotation

| Setting (`AuthSessions`) | Default |
|---|---|
| `RefreshTokenIdleLifetime` | 14 days |
| `RefreshTokenAbsoluteLifetime` | 30 days |
| `MaximumActiveSessionsPerUser` | 10; the oldest session is revoked beyond it |
| `RotationGracePeriod` | 10 seconds |
| `PurgeRetention`, `PurgeInterval`, `PurgeBatchSize` | 7 days, 1 day, 500 |

The settings are validated at startup. Refresh tokens are 32 random bytes in base64url, and only their SHA-256 hash is stored. No IP address is stored; the device label is derived from `User-Agent` and the raw header is not kept.

**Rotation:** each refresh consumes the token and issues a new one in the same session. The session's last-used time and idle expiry move only at this point.

**Reuse:** a consumed token used again within the grace period gets 409 `refresh_token_superseded` and the session stays intact. After the grace period, reuse revokes the whole session and returns 401.

**Logout** revokes the session even when it receives an already rotated token of that session.

## Concurrency and the Neon pooler

Rotation, revocation and sign-in each run in one database transaction and take row locks with `SELECT ... FOR UPDATE`. Row locks end with their transaction, so they work behind Neon's pooled connection, which runs PgBouncer in transaction mode. No advisory lock, temporary table or session setting is used.

To avoid deadlocks, locks are always taken in the same order: refresh token, then session. Revocation locks only the session. Sign-in locks the user row, so concurrent sign-ins cannot exceed the session limit.

## Cleanup

A background job deletes sessions, and through cascade their refresh tokens, once they have been revoked or expired for longer than `PurgeRetention`. Consumed tokens stay while their session lives, so reuse can be detected.

## Local development

Run the API with the `http` profile (`http://localhost:5252`) and let Vite proxy `/api` and `/health`:

```ts
// web/vite.config.ts
server: {
  port: 5173,
  strictPort: true,
  proxy: {
    '/api': { target: 'http://localhost:5252', changeOrigin: true },
    '/health': { target: 'http://localhost:5252', changeOrigin: true },
  },
}
```

`/health` is proxied for the cold start check, which calls `/health/live`; it carries no cookie or token. `strictPort` keeps Vite on 5173, the only origin the API allows.

The browser then talks only to `http://localhost:5173`, and the cookie is stored for that host. `appsettings.Development.json` already allows the origin `http://localhost:5173` with credentials; the browser's `Origin` stays `http://localhost:5173` through the proxy. Chrome, Edge and Firefox accept `Secure` and `__Host-` cookies on `http://localhost`.

**WebKit needs HTTPS.** Playwright's WebKit (27.2, Windows) stores `Secure` cookies set over `http://localhost` but never sends them back: of `plain`, `SameSite=Strict`, `Secure`, `Secure; SameSite=Strict` and `__Host-` cookies, only the two without `Secure` returned. Over HTTPS it sends all five. The refresh cookie must be `Secure` because of its `__Host-` prefix, so over plain HTTP a WebKit user is signed out on every reload. `npm run dev` stays on HTTP for Chrome, Edge and Firefox; `vite --mode e2e` (and `vite preview --mode e2e`) serves HTTPS with a throwaway self-signed certificate (`@vitejs/plugin-basic-ssl`), which `scripts/run-e2e-fullstack.sh` uses with `HttpSecurity__AllowedOrigins__0=https://localhost:4173`. Real Safari on iPhone and macOS has not been checked; it is on the launch checklist in the handoff.

## When the domain is ready

No code changes. Set on Render:

| Variable | Value |
|---|---|
| `HttpSecurity__AllowedOrigins__0` | `https://app.alanadi.com` or `https://alanadi.com`, exactly as the browser sends it |
| `HttpSecurity__AllowCredentials` | `true` |
| `AllowedHosts` | `api.alanadi.com` |
| `PRODUCTION_URL` (CI) | `https://api.alanadi.com` |

The frontend sets `VITE_API_BASE_URL=https://api.alanadi.com`. The cookie needs no change: it is host-only on whichever host serves the API. Afterwards, verify in a browser that login sets the cookie, a reload refreshes silently, logout clears it, and a request from another origin gets 403.

## Planned

- Password change (PR 12) and account deletion (PR 13) change the security stamp and revoke every session.
- Sessions and their device labels are part of the data export and account deletion (PR 13).
