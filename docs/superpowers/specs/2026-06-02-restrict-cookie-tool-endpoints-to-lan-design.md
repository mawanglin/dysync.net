# Restrict Anonymous Cookie-Tool Endpoints to LAN/Loopback — Design Spec

**Status:** Approved 2026-06-02

**Campaign:** Post-merge hardening. The `master` merge (`94c3d8b`) brought in the upstream "重置 cookie 工具" which added two `[AllowAnonymous]` endpoints on `ConfigController`:

- `GET /api/config/Cookies` (`GetAllCookies`) — anonymously lists **every** cookie's `id`/`name`/`status`.
- `POST /api/config/FastResetCookie` (`FastResetCookie`) — anonymously resets/overwrites the cookie for any `id` (caller supplies a valid Douyin cookie).

Combined, an anonymous client can enumerate cookie ids and overwrite cookies — the same class of anonymous-injection surface review #4 closed for `deskinit`.

## Constraint: the client is a fixed binary that sends no auth

`dy.cookie.exe` (committed at repo root) is a **Go + chromedp** binary. `strings` analysis shows it connects to a configurable LAN server (example `192.168.1.100:10101`) and calls `/api/config/cookies` + the reset path **with no `Authorization`/token header**. We cannot recompile it. Therefore any hardening that requires a token or login would break the legitimate tool.

## Decision

Keep `[AllowAnonymous]` (the tool authenticates nothing) but **gate both endpoints to private-network / loopback client IPs**. This matches the tool's real deployment (desktop-on-LAN → server-on-LAN/NAS) and closes the actual threat: the app listens directly on `http://*:10101` (no built-in proxy, per the 2026-05-18 report), so a public client reaching a port-forwarded `:10101` has a public `RemoteIpAddress` and is rejected, while LAN/loopback callers pass.

**Limitations (recorded):** If the server is fronted by a reverse proxy without forwarded-headers configured, `RemoteIpAddress` is the proxy's (likely private) IP and the gate would pass-through — operators behind a proxy must restrict these paths at the proxy. A LAN-resident attacker is still in scope (unchanged from upstream). Token/auth on the tool path remains a stronger follow-up *if* the tool is replaced.

## Pure-logic seam (testable)

Add `utils/NetworkGuard.IsPrivateOrLoopback(IPAddress ip) → bool` — a pure predicate, the characterization seam:

- `null` → `false`.
- `IPAddress.IsLoopback(ip)` → `true` (127.0.0.0/8, ::1).
- IPv4-mapped IPv6 → unwrap to IPv4 first.
- IPv4: `10.0.0.0/8`, `172.16.0.0/12`, `192.168.0.0/16`, `169.254.0.0/16` (link-local) → `true`.
- IPv6: `fc00::/7` (ULA), `fe80::/10` (link-local) → `true`.
- else → `false`.

## Changes

- **Add:** `utils/NetworkGuard.cs` — the pure predicate.
- **Modify:** `Controllers/ConfigController.cs` — a private `IsLocalToolRequest()` helper reading `HttpContext.Connection.RemoteIpAddress` via `NetworkGuard`; prepend a `403` guard to `FastResetCookie` and `GetAllCookies`.
- **Add:** `tests/dy.net.Tests/NetworkGuardTests.cs` — golden-master over the predicate (loopback v4/v6, each private range incl. boundaries 172.15/172.16/172.31/172.32, link-local, IPv4-mapped, public 8.8.8.8 / 1.1.1.1, ULA, null).
- **Modify:** `tests/README.md` — record the pinned coverage.

## Testing

`IsPrivateOrLoopback` is pure → fully golden-master covered. The controller guard (reading `RemoteIpAddress` + returning 403) is thin orchestration over the predicate; verified by build + the predicate tests. Verification: build 0 errors + suite stays green.
