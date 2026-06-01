# Force Password Change on First Login (Keep Default Account) — Design Spec

**Status:** Approved 2026-06-01

**Campaign:** Code-review CRITICAL remediation (report `docs/code-review-2026-05-18.md` #1).
Third of three remaining CRITICAL fixes.

## Goal

The admin account is seeded on every startup with the well-known hardcoded default `douyin/douyin2026` (`Program.cs:214` → `AdminUserService.InitUser`). The password-hashing half of #1 was already fixed (`PasswordUtil` PBKDF2 + transparent MD5 upgrade); what remains is that the **default credential persists** — a fresh install is wide open to anyone who knows the default.

Per review decision: **keep the default account** (preserve out-of-box UX) but **force a password change on first login** so the default cannot survive the admin's first session.

## Enforcement level (review decision)

**Backend flag + frontend prompt** (the softer of the two options considered; the server-side hard-gate via a restricted JWT claim was explicitly *not* chosen).

### Honest limitations (recorded, not hidden)

- The flag does **not** block authenticated API calls server-side; a client that ignores the frontend prompt can still operate without changing the password.
- The default credential is still usable in the window **between install and the admin's first login** (the flag only forces a change once the legit admin logs in).

These are accepted tradeoffs of the chosen level. Hardening (restricted-token filter; or seeding a random password instead of a known default) remains a documented follow-up.

## Backend changes

1. **`model/entity/AdminUserInfo.cs`** — add column, following the existing `UpdateTime` precedent (`IsNullable = true` on a non-nullable CLR type so SqlSugar CodeFirst adds a nullable column to existing tables; existing rows read back as `false`, so **existing deployments are not retroactively forced** — only a freshly seeded default is):
   ```csharp
   /// <summary>是否必须修改密码（以默认凭据首次创建后强制改密）。</summary>
   [SqlSugar.SugarColumn(IsNullable = true)]
   public bool MustChangePwd { get; set; }
   ```

2. **`service/AdminUserService.InitUser`** — set `MustChangePwd = true` on the seeded entity. This is the only `InitUser` caller (`Program.cs` default seed), so the flag is set exactly for the default-credential seed.

3. **`repository/AdminUserRepository.UpdatePwd`** — on successful change, set `user.MustChangePwd = false` before `UpdateAsync` (clears the force once the user picks their own password).

4. **`Controllers/AuthController.Login`** — add `mustChangePwd = user.MustChangePwd` to the success response object so the frontend can react.

## Frontend changes

5. **`app/src/store/account.ts`** — add `mustChangePwd: false` to state; in `login()` success branch set `this.mustChangePwd = response.data.mustChangePwd === true`.

6. **`app/src/pages/login/Login.vue` `onLoginSuccess`** — if `useAccountStore().mustChangePwd`, `message.warning('首次登录请立即修改默认密码')` and open the global personal drawer on the change-password tab (`personalRef`/`MyPersonal.show(true)`) instead of going straight to the dashboard. Soft prompt (drawer is closable) — consistent with the chosen enforcement level.

## File-level scope

- **Modify (backend):** `model/entity/AdminUserInfo.cs`, `service/AdminUserService.cs`, `repository/AdminUserRepository.cs`, `Controllers/AuthController.cs`.
- **Modify (frontend):** `app/src/store/account.ts`, `app/src/pages/login/Login.vue` (+ wiring in `App.vue` if needed to reach the drawer from login).
- **Modify (tests):** `tests/dy.net.Tests/TestDb.cs` (add `AdminUserInfo` to `InitTables`), new `AdminUserRepositoryTests` characterization class.

## Testing (golden-master, backend)

Extend `TestDb` to `InitTables<DouyinVideo, DouyinCookie, AdminUserInfo>` (additive, harmless to existing suites). New `AdminUserRepositoryTests` against the real SQLite stack:

- `InitUser` on empty table → row created with **`MustChangePwd == true`** (new behavior pinned) and password verifies.
- `InitUser` when a user already exists → returns `(-1, "系统用户已存在")`, no second row (existing behavior).
- `UpdatePwd` with correct old password → `MustChangePwd == false`, new password verifies, returns `(0, "更新成功")`.
- `UpdatePwd` with wrong old password → `(-1, "原密码错误")`, flag unchanged.
- `UpdatePwd` with unknown `UserId` → `(-1, "用户不存在")`.

`AuthController.Login` (JWT issuance) and the frontend prompt are integration/UI concerns with no pure-logic seam; verified by build + manual reasoning, not golden master. Frontend cannot be browser-verified in this environment — recorded as such.
