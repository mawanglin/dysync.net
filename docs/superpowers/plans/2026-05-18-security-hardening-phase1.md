# Security Hardening — Phase 0+1 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the repository diff-reviewable again (line-ending normalization + hygiene), then apply the surgical, low-blast-radius backend security/correctness fixes from the 2026-05-18 review that do not require a subsystem rewrite.

**Architecture:** Two phases. Phase 0 = repo normalization (no build needed, correct by inspection). Phase 1 = targeted backend source edits (SQL parameterization, real transaction await, authorization coverage, TLS validation). All Phase 1 edits are surgical and reversible; correctness is verified by **the user building/smoke-testing in their .NET environment** (no .NET SDK exists in the planning environment, and the project has no test harness).

**Tech Stack:** .NET 6 / ASP.NET Core, SqlSugar ORM, git, Docker.

**Out of scope — deferred to their own future plans (each a subsystem-sized effort):**
- `#1` password hashing migration (MD5 → PBKDF2/BCrypt + default-credential removal) → `plan: auth-credential-redesign`
- `#2` JWT signing-key + issuer/audience redesign → `plan: jwt-key-redesign` (do together with `#1`)
- `#7`/`#8` frontend token/password storage model → `plan: frontend-session-model`
- `#9` .NET 6 → .NET 8 migration + dependency bumps → `plan: dotnet8-migration`
- WARNING-tier perf/refactor items (full-table loads, god-class split, async cleanup) → `plan: backend-perf-refactor`

**Verification convention:** Steps marked **USER ACTION** must be run by the user in their .NET dev environment (`dotnet build` / run / curl). The agent cannot compile here. Do not mark a task complete until its USER ACTION passes.

---

## File Structure

| File | Phase | Responsibility / change |
|---|---|---|
| `.gitattributes` (create) | 0 | Enforce LF normalization, end CRLF churn |
| `.gitignore` (modify) | 0 | Exclude env/user/timestamp/idea files |
| `.dockerignore` (create) | 0 | Stop shipping `.git`/`node_modules`/secrets into image |
| tracked junk (remove) | 0 | `dy.net.csproj.user`, `*.timestamp-*.mjs`, `app/.env*`, `* copy.vue` |
| `repository/AdminUserRepository.cs:110-118` | 1 | Parameterize `ResetPwd` SQL (kills injection) |
| `repository/BaseRepository.cs:107-115` | 1 | `await` the transaction, return real success |
| `extension/ServiceExtension.cs:331` | 1 | Remove TLS cert-validation bypass |
| `extension/ServiceExtension.cs:202-236` | 1 | Add global `FallbackPolicy` (auth-by-default) |
| `Controllers/VideoController.cs:10-12` | 1 | Class-level `[Authorize]` (public endpoints keep existing `[AllowAnonymous]`) |
| `Controllers/LogsController.cs` | 1 | Class-level `[Authorize]` + path-traversal guard in `GetLog` |
| `Controllers/ConfigController.cs:431-433` | 1 | Filename containment in anonymous `GetMp3` |

---

# PHASE 0 — Repository Normalization & Hygiene

### Task 0.1: Add `.gitattributes` and renormalize line endings

**Files:**
- Create: `.gitattributes`

- [ ] **Step 1: Create `.gitattributes`**

Create `.gitattributes` with exactly:

```gitattributes
* text=auto eol=lf
*.sh text eol=lf
*.cmd text eol=crlf
*.bat text eol=crlf
*.png binary
*.jpg binary
*.jpeg binary
*.gif binary
*.ico binary
*.svg text
*.mp3 binary
*.woff binary
*.woff2 binary
*.ttf binary
*.eot binary
```

- [ ] **Step 2: Stage the attributes file only**

```bash
git add .gitattributes
```

- [ ] **Step 3: Renormalize the whole tree in a dedicated commit**

```bash
git add --renormalize .
git status --short | head
```
Expected: many files staged (this is the intentional one-time EOL flip).

- [ ] **Step 4: Commit**

```bash
git commit -m "chore: add .gitattributes and renormalize line endings to LF

One-time tree-wide CRLF->LF normalization. Future diffs are now reviewable.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

- [ ] **Step 5: Verify the churn is gone**

```bash
git status --short
```
Expected: clean (no more 228 phantom-modified files).

---

### Task 0.2: Stop tracking generated/dev/secret files

**Files:**
- Modify: `.gitignore`
- Remove from index: `dy.net.csproj.user`, `app/vite.config.ts.timestamp-*.mjs`, `app/.env`, `app/.env.development`, `app/.env.github`
- Delete: `app/src/components/layout/FrontView copy.vue`, `app/src/pages/workplace/statics copy.vue`

- [ ] **Step 1: Append ignore rules to root `.gitignore`**

Append these lines to the existing `.gitignore` (do not remove existing entries):

```gitignore

# dev/IDE
*.user
.vs/
.idea/

# vite scratch
**/vite.config.ts.timestamp-*.mjs

# env files (commit .env.example instead)
app/.env
app/.env.*
!app/.env.example

# stray duplicates
**/* copy.*
```

- [ ] **Step 2: Untrack files (keep them on disk where they are real config)**

```bash
git rm --cached dy.net.csproj.user
git rm --cached app/vite.config.ts.timestamp-*.mjs
git rm --cached app/.env app/.env.development app/.env.github
```

- [ ] **Step 3: Provide a sanitized env template**

Create `app/.env.example` by copying `app/.env.development` and replacing any real values with placeholders. The only current value is the API base; write:

```
VITE_BASE_URL=http://localhost:10101/
```
(Adjust the variable name to match what `app/.env.development` actually uses — open it and mirror its key names with placeholder values.)

- [ ] **Step 4: Delete the dead "copy" duplicates**

```bash
git rm "app/src/components/layout/FrontView copy.vue"
git rm "app/src/pages/workplace/statics copy.vue"
```

- [ ] **Step 5: Stage `.gitignore` + example and commit**

```bash
git add .gitignore app/.env.example
git commit -m "chore: untrack dev/env/scratch files, drop dead copy components

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

- [ ] **Step 6: Verify**

```bash
git ls-files | grep -E '\.user$|timestamp-.*\.mjs|app/\.env($|\.)| copy\.' || echo "CLEAN: no tracked junk"
```
Expected: `CLEAN: no tracked junk`

---

### Task 0.3: Add `.dockerignore`

**Files:**
- Create: `.dockerignore`

- [ ] **Step 1: Create `.dockerignore`**

```dockerignore
.git
.gitignore
.gitattributes
**/obj
**/bin
app/node_modules
app/.env
app/.env.*
logs
db
**/*.user
**/vite.config.ts.timestamp-*.mjs
docs
*.md
```

- [ ] **Step 2: Commit**

```bash
git add .dockerignore
git commit -m "chore: add .dockerignore to shrink image and stop leaking source/secrets

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

- [ ] **Step 3 (USER ACTION): Sanity-check image build context**

In the user's environment with Docker:
```bash
docker build -t dysync-test -f Dockerfile .
```
Expected: build succeeds; image no longer contains `.git`/`node_modules` (optional: `docker run --rm dysync-test ls -a` shows no `.git`).

---

# PHASE 1 — Surgical Backend Fixes

> Each task is one isolated edit + commit. Build verification is batched at Task 1.6 (USER ACTION) because the agent has no .NET SDK.

### Task 1.1: Parameterize `ResetPwd` SQL (review #5 — injection)

**Files:**
- Modify: `repository/AdminUserRepository.cs:110-118`

- [ ] **Step 1: Replace the interpolated SQL with a parameterized command**

Replace exactly:

```csharp
        public bool ResetPwd(string pwd)
        {
            if (string.IsNullOrWhiteSpace(pwd)) pwd = "douyin2026";
            var password = Md5Util.Md5(pwd);

            string sql = $" Update login_user_info SET Password='{password}'";

            return this.Db.Ado.ExecuteCommand(sql) > 0;
        }
```

with:

```csharp
        public bool ResetPwd(string pwd)
        {
            if (string.IsNullOrWhiteSpace(pwd)) pwd = "douyin2026";
            var password = Md5Util.Md5(pwd);

            // 参数化，杜绝 SQL 注入。注意：无 WHERE 为单管理员场景的既有语义，
            // 行范围未改动以避免行为变更；多用户场景需另行加 WHERE。
            const string sql = "UPDATE login_user_info SET Password=@pwd";

            return this.Db.Ado.ExecuteCommand(sql, new SqlSugar.SugarParameter("@pwd", password)) > 0;
        }
```

> Note: behavior (resets all rows) is intentionally preserved here — changing row scope is a separate decision. This task only removes the injection vector. The hardcoded `"douyin2026"` default is addressed in the deferred `auth-credential-redesign` plan, not here.

- [ ] **Step 2: Inspection check**

Confirm there is no remaining `$"...{...}..."` interpolation feeding `Db.Ado.ExecuteCommand` in this file:
```bash
grep -n "ExecuteCommand" repository/AdminUserRepository.cs
```
Expected: only the parameterized call.

- [ ] **Step 3: Commit**

```bash
git add repository/AdminUserRepository.cs
git commit -m "fix(security): parameterize ResetPwd SQL to prevent injection (#5)

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

---

### Task 1.2: Make `UseTranAsync` actually atomic (review #6 — silent data loss)

**Files:**
- Modify: `repository/BaseRepository.cs:107-115`

- [ ] **Step 1: Await the transaction and return its real result**

Replace exactly:

```csharp
        public async Task<bool> UseTranAsync(Func<Task> action, Action<Exception> errorCallBack)
        {
            var res = Db.Ado.UseTranAsync(async () =>
              {
                  await action();
              }, errorCallBack: errorCallBack);

            return res.IsCompletedSuccessfully;
        }
```

with:

```csharp
        public async Task<bool> UseTranAsync(Func<Task> action, Action<Exception> errorCallBack)
        {
            var res = await Db.Ado.UseTranAsync(action, errorCallBack: errorCallBack);
            return res.IsSuccess;
        }
```

- [ ] **Step 2: Inspection check — confirm SqlSugar `DbResult` shape**

`Db.Ado.UseTranAsync(Func<Task>, Action<Exception>)` returns `Task<DbResult<bool>>` in SqlSugar 5.x; `DbResult<T>` exposes `.IsSuccess`. Verify by grepping other transaction usages in the repo for the expected member:
```bash
grep -rn "UseTranAsync\|\.IsSuccess\|DbResult" repository/ service/ | head
```
Expected: no other call relies on the old `IsCompletedSuccessfully` semantics that this change would break. (If any caller checked the boolean and branched on the old always-false behavior, note it for Task 1.6 smoke test.)

- [ ] **Step 3: Commit**

```bash
git add repository/BaseRepository.cs
git commit -m "fix(data): await UseTranAsync so transactions are actually atomic (#6)

Previously returned res.IsCompletedSuccessfully on a non-awaited task
(near-always false; body could run after method returned), breaking
ReDownloadViedoAsync/BatchInsertOrUpdate atomicity and risking data/file loss.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

---

### Task 1.3: Remove TLS certificate-validation bypass (review #10)

**Files:**
- Modify: `extension/ServiceExtension.cs:329-332`

- [ ] **Step 1: Delete the always-true callback**

Replace exactly:

```csharp
                    SslOptions = new SslClientAuthenticationOptions
                    {
                        RemoteCertificateValidationCallback = (_, __, ___, ____) => true
                    }
```

with:

```csharp
                    SslOptions = new SslClientAuthenticationOptions()
```

- [ ] **Step 2: Inspection check**

```bash
grep -n "RemoteCertificateValidationCallback\|=> true" extension/ServiceExtension.cs
```
Expected: no `RemoteCertificateValidationCallback` line remains.

- [ ] **Step 3: Commit**

```bash
git add extension/ServiceExtension.cs
git commit -m "fix(security): use default TLS cert validation for outbound HTTP (#10)

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

---

### Task 1.4: Lock down `VideoController` + `LogsController` (review #3, #4)

**Files:**
- Modify: `Controllers/VideoController.cs:10-12`
- Modify: `Controllers/LogsController.cs:1-23`

- [ ] **Step 1: Add class-level `[Authorize]` to `VideoController`**

`Controllers/VideoController.cs` already imports `Microsoft.AspNetCore.Authorization` (line 5) and the public share/play endpoints already carry `[AllowAnonymous]` (e.g. line 56), which overrides a class-level policy. Replace:

```csharp
    [Route("api/[controller]")]
    [ApiController]
    public class VideoController : ControllerBase
```

with:

```csharp
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class VideoController : ControllerBase
```

- [ ] **Step 2: Add `[Authorize]` + import to `LogsController` and harden `GetLog`**

In `Controllers/LogsController.cs`, replace the header block:

```csharp
using dy.net.model.dto;
using dy.net.service;
using Microsoft.AspNetCore.Mvc;

namespace dy.net.Controllers
{
    [Route("api/[controller]/[action]")]
    [ApiController]
    public class LogsController : ControllerBase
```

with:

```csharp
using dy.net.model.dto;
using dy.net.service;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace dy.net.Controllers
{
    [Route("api/[controller]/[action]")]
    [ApiController]
    [Authorize]
    public class LogsController : ControllerBase
```

- [ ] **Step 3: Add path-traversal containment in `GetLog`**

In `Controllers/LogsController.cs`, replace:

```csharp
        [HttpGet("/api/logs/GetLog/{type}/{date}")]
        public async Task<IActionResult> GetLog([FromRoute] string type, [FromRoute] string date)
        {
            var filePath = Path.Combine(webHostEnvironment.IsDevelopment() ? Directory.GetCurrentDirectory() : AppDomain.CurrentDomain.BaseDirectory, "logs", $"log-{type}-{date}.txt");
```

with:

```csharp
        [HttpGet("/api/logs/GetLog/{type}/{date}")]
        public async Task<IActionResult> GetLog([FromRoute] string type, [FromRoute] string date)
        {
            if (!System.Text.RegularExpressions.Regex.IsMatch(type ?? "", "^[A-Za-z]+$")
                || !System.Text.RegularExpressions.Regex.IsMatch(date ?? "", "^\\d{8}$"))
            {
                return BadRequest("非法参数");
            }
            var logsRoot = Path.GetFullPath(Path.Combine(webHostEnvironment.IsDevelopment() ? Directory.GetCurrentDirectory() : AppDomain.CurrentDomain.BaseDirectory, "logs"));
            var filePath = Path.GetFullPath(Path.Combine(logsRoot, $"log-{type}-{date}.txt"));
            if (!filePath.StartsWith(logsRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            {
                return BadRequest("非法路径");
            }
```

> The original line declaring `filePath` is now replaced; the rest of the method (the `File.Exists` check immediately following) is unchanged and still uses `filePath`.

- [ ] **Step 4: Inspection check**

```bash
grep -n "\[Authorize\]" Controllers/VideoController.cs Controllers/LogsController.cs
grep -n "logsRoot\|StartsWith" Controllers/LogsController.cs
```
Expected: class-level `[Authorize]` present in both; containment check present.

- [ ] **Step 5: Commit**

```bash
git add Controllers/VideoController.cs Controllers/LogsController.cs
git commit -m "fix(security): require auth on VideoController/LogsController + path guard (#3,#4)

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

---

### Task 1.5: Contain filename in anonymous `GetMp3` (review #4)

**Files:**
- Modify: `Controllers/ConfigController.cs:431-440`

- [ ] **Step 1: Read the current `GetMp3` body**

Open `Controllers/ConfigController.cs` around line 431-445 to see the exact existing body (it builds a path from `name` and returns the file).

- [ ] **Step 2: Add a bare-filename guard as the first lines of the method**

Immediately after the `GetMp3(... string name)` opening brace, insert:

```csharp
            if (string.IsNullOrWhiteSpace(name) || Path.GetFileName(name) != name)
            {
                return BadRequest("非法文件名");
            }
```

This rejects any `name` containing path separators or `..` (since `Path.GetFileName` strips directory components, a traversal input will not equal the original `name`). Leave the rest of the method unchanged.

- [ ] **Step 3: Inspection check**

```bash
grep -n "GetFileName(name) != name" Controllers/ConfigController.cs
```
Expected: one match inside `GetMp3`.

- [ ] **Step 4: Commit**

```bash
git add Controllers/ConfigController.cs
git commit -m "fix(security): reject path-traversal filenames in anonymous GetMp3 (#4)

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

---

### Task 1.6: Add global authorization fallback policy (review #4 — defense in depth)

> Highest behavior-risk change in this plan: every controller endpoint without explicit `[AllowAnonymous]` will now require a valid token. Static SPA files are served by separate middleware and are unaffected. Login (`AuthController` login action) and `[AllowAnonymous]` endpoints (`IsInit`, `DeskInit`, `CheckTag`, `GetMp3`, share/play) remain reachable. This MUST be smoke-tested by the user.

**Files:**
- Modify: `extension/ServiceExtension.cs` (`ConfigureJwtAuthentication`, ends at line 236)

- [ ] **Step 1: Add the Authorization using-directive**

`extension/ServiceExtension.cs` does not currently import `Microsoft.AspNetCore.Authorization`. Add it to the using block at the top of the file (alongside the existing ASP.NET Core usings):

```csharp
using Microsoft.AspNetCore.Authorization;
```

- [ ] **Step 2: Register a fallback policy at the end of `ConfigureJwtAuthentication`**

In `ConfigureJwtAuthentication`, the JWT block ends with `});` followed by the method's closing `}` (line 235-236). Insert the authorization registration between them. Replace:

```csharp
                };
            });
        }
```

(the final lines of `ConfigureJwtAuthentication`) with:

```csharp
                };
            });

            services.AddAuthorization(options =>
            {
                options.FallbackPolicy = new AuthorizationPolicyBuilder()
                    .RequireAuthenticatedUser()
                    .Build();
            });
        }
```

> If the closing snippet `};\n            });\n        }` is not unique in the file, scope the match to the `ConfigureJwtAuthentication` method by including a few preceding lines (the `ClockSkew = TimeSpan.FromSeconds(60)` line) in the match.

- [ ] **Step 3: Inspection check**

```bash
grep -n "FallbackPolicy\|AddAuthorization\|using Microsoft.AspNetCore.Authorization" extension/ServiceExtension.cs
```
Expected: using-directive + `AddAuthorization` + `FallbackPolicy` all present.

- [ ] **Step 4: Commit**

```bash
git add extension/ServiceExtension.cs
git commit -m "fix(security): add global auth FallbackPolicy (deny-by-default) (#4)

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

- [ ] **Step 5 (USER ACTION): Build + smoke test the whole Phase 1 in the .NET environment**

Run in the user's dev environment:
```bash
dotnet build dy.net.sln -c Release
```
Expected: build succeeds with no new errors (CS1998 warnings on unrelated pre-existing async methods are acceptable; they are deferred to `backend-perf-refactor`).

Then run the app and smoke-test:
1. `POST /api/Auth/login` with default creds → returns a token (login still works).
2. Without a token, `GET /api/logs/GetLog/debug/20260518` → `401` (was anonymous).
3. Without a token, a destructive `VideoController` endpoint → `401` (was anonymous).
4. `GET /api/logs/GetLog/..%2f..%2fappsettings/00000000` → `400` (traversal blocked).
5. `GET /api/Config/GetMp3?name=../appsettings.json` → `400` (traversal blocked).
6. `GET /api/Config/IsInit` (AllowAnonymous) → still `200` (fallback policy didn't over-block).
7. With a valid token, the normal dashboard flows still work (login → list videos → stats).
8. Trigger a re-download/delete that goes through `UseTranAsync` → completes and the success flag now reflects reality (no silent data loss).

If any smoke test fails, fix forward (do not leave a partially-applied Phase 1 on the branch) and re-run.

---

## Self-Review

**Spec coverage vs review report:**
- `#5` SQL injection → Task 1.1 ✓
- `#6` fake transaction → Task 1.2 ✓
- `#10` TLS bypass → Task 1.3 ✓
- `#3` LogsController unauth + traversal → Task 1.4 ✓
- `#4` unauth destructive endpoints / GetMp3 / fallback → Tasks 1.4, 1.5, 1.6 ✓
- CRLF churn (WARNING) → Task 0.1 ✓
- tracked junk / .env / copy files / .dockerignore (WARNING) → Tasks 0.2, 0.3 ✓
- `#1`,`#2`,`#7`,`#8`,`#9` and WARNING perf/refactor → explicitly deferred to named plans (subsystem-sized; #1+#2 must be designed together to avoid token-invalidation churn). Documented at top, not silently dropped.

**Placeholder scan:** No TBD/TODO; every code step shows exact before/after. The only "open" item (ResetPwd no-WHERE row scope) is explicitly called out as an intentional non-change with rationale, not a placeholder.

**Type consistency:** `UseTranAsync` returns `Task<bool>` unchanged (signature stable for all callers); `DbResult.IsSuccess` verified as the SqlSugar 5.x member in Task 1.2 Step 2 before relying on it. `[Authorize]`/`[AllowAnonymous]` interaction (action-level overrides class-level) is correct ASP.NET Core semantics. `SugarParameter` is the SqlSugar parameter type used consistently in Task 1.1.

**Risk note:** Task 1.6 (FallbackPolicy) is the only behavior-broadening change; it is ordered last and gated by an explicit USER smoke test enumerating the anonymous endpoints that must keep working.
