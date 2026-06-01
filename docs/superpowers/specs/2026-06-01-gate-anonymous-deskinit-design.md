# Gate Anonymous `deskinit` to Uninitialized-Only — Design Spec

**Status:** Approved 2026-06-01

**Campaign:** Code-review CRITICAL remediation (report `docs/code-review-2026-05-18.md` #4).
First of three remaining CRITICAL fixes (#4 deskinit, #6 fire-and-forget, #1 forced first-login change).

## Goal

`Controllers/ConfigController.DeskInitAsync` (`/api/config/deskinit`, line 158) is `[AllowAnonymous]` and accepts a full `DouyinCookie` (cookie string + arbitrary `SavePath`/`FavSavePath`/`UpSavePath`). Any anonymous client can therefore inject a Douyin cookie and arbitrary storage paths **at any time**, including long after setup — a data-exfiltration / takeover surface.

Close the window **without** breaking first-run setup: allow anonymous `deskinit` only while the system is uninitialized (no cookie configured yet); reject once initialized.

## Why a gate, not `[Authorize]`

`deskinit` is reached **before login** during a fresh install:

- `Login.vue` `onMounted` → `AppisInit()` → if not initialized → `router.push('/init')`
- `/init` (`pages/desk/index.vue`) is under the public `FrontView` layout (sibling of `/login`) → calls `DeskInitAsync`

A brand-new install has no session, so `[Authorize]` would 401 the first-run flow. The genuine vulnerability is *"anonymous injection **after** setup"*, which an uninitialized-gate closes precisely while preserving first-run UX. Subsequent cookie management already goes through the authorized `AddOrUpdateAsync` (`/api/config/update`, line 231, under class-level `[Authorize]`), so the gate does not break multi-cookie scenarios.

`checktag` (line 350) and `getmp3` (line 431) are intentionally left anonymous: `checktag` is read by the pre-login login page for the version banner and exposes only docker tag strings (no state change, no secrets); `getmp3` already validates `Path.GetFileName(name) != name`.

## Current state (verbatim — what changes)

`Controllers/ConfigController.cs:158-180`, `DeskInitAsync` body begins:

```csharp
public async Task<IActionResult> DeskInitAsync([FromBody] DouyinCookie dyUserCookies)
{
    // 1. 基础赋值
    dyUserCookies.Id = IdGener.GetLong().ToString();
    ...
```

## Change

Prepend an uninitialized-gate as step 0, reusing the existing `dyCookieService.IsInit()` (`Task<bool>`, true ⇒ a cookie already exists — same source of truth as the `IsInit` endpoint and the `/init` redirect):

```csharp
// 0. 未初始化门控：deskinit 仅用于首次安装配置首个 Cookie。
//    系统一旦完成初始化，匿名调用即被拒绝；后续 Cookie 管理须经鉴权的 update 端点。
if (await dyCookieService.IsInit())
    return ApiResult.Fail("系统已初始化，禁止匿名配置；请登录后在设置中管理 Cookie");
```

`[AllowAnonymous]` is retained.

## File-level scope

- **Modify:** `Controllers/ConfigController.cs` — prepend the gate to `DeskInitAsync`. No other endpoints touched.

## Testing

Controller attribute/orchestration; no pure-logic seam. The gate predicate is the one-liner `IsInit()` (already covered by its own endpoint usage). Verification is build-green + the manual reasoning above (first-run uninitialized → allowed; initialized → rejected). No golden-master fact applies; recorded as such in `tests/README.md`.

## Out of scope

`checktag`/`getmp3` (left anonymous by review decision), and any frontend change — the `/init` flow is unchanged for fresh installs.
