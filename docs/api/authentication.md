# Authentication and sessions

Every sign-in opens a session for that device. Access tokens are JWTs bound to the session; refresh tokens rotate within it.

## Access tokens

`POST /api/auth/register` and `POST /api/auth/login` return a bearer access token. Besides the user identifier it carries:

| Claim | Meaning |
|---|---|
| `session` | The session the token belongs to |
| `stamp` | The user's security stamp when the token was issued |

Each request checks, in one database read, that the session exists, belongs to the user, is not revoked and has not passed its absolute lifetime, and that the stamp still matches. Ordinary requests never write session state.

## Sessions

| Endpoint | Effect |
|---|---|
| `GET /api/auth/sessions` | Lists active sessions with a coarse device label such as `Chrome on Windows`. The current one has `isCurrent: true`. |
| `DELETE /api/auth/sessions/{id}` | Revokes one of the caller's sessions. Its access tokens stop working at once. Another user's session returns 404. |
| `POST /api/auth/logout-all` | Revokes every session and changes the security stamp, so no earlier access token stays valid. |

No IP address is stored. The device label is derived from the `User-Agent` header and the raw header is not kept.

## Refresh tokens

Refresh tokens are 32 random bytes in base64url. Only their SHA-256 hash is stored. They are not issued over HTTP yet: they will be delivered only in an HttpOnly cookie, never in a response body.

| Setting (`AuthSessions`) | Default |
|---|---|
| `RefreshTokenIdleLifetime` | 14 days |
| `RefreshTokenAbsoluteLifetime` | 30 days |
| `MaximumActiveSessionsPerUser` | 10; the oldest session is revoked beyond it |
| `RotationGracePeriod` | 10 seconds |
| `PurgeRetention`, `PurgeInterval`, `PurgeBatchSize` | 7 days, 1 day, 500 |

The settings are validated at startup.

**Rotation:** each use consumes the token and issues a new one in the same session. The session's last-used time and idle expiry move only at this point, and the idle expiry never passes the absolute one.

**Reuse:** a consumed token used again within the grace period gets a soft rejection and the session stays intact. This covers two tabs refreshing at once; the browser shares the new cookie, so the slower tab can retry. After the grace period, reuse revokes the whole session.

## Concurrency and the Neon pooler

Rotation, revocation and sign-in each run in one database transaction and take row locks with `SELECT ... FOR UPDATE`. Row locks end with their transaction, so they work behind Neon's pooled connection, which runs PgBouncer in transaction mode. No advisory lock, temporary table or session setting is used.

To avoid deadlocks, locks are always taken in the same order: refresh token, then session. Revocation locks only the session. Sign-in locks the user row, so concurrent sign-ins cannot exceed the session limit.

## Cleanup

A background job deletes sessions, and through cascade their refresh tokens, once they have been revoked or expired for longer than `PurgeRetention`. Consumed tokens stay while their session lives, so reuse can be detected.

## Planned

- Password change (PR 12) and account deletion (PR 13) change the security stamp and revoke every session.
- Sessions and their device labels are part of the data export and account deletion (PR 13).
