# Extract Author Avatar URL Pick Logic — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Extract the avatar-URL priority pick from `DouyinBasicSyncJob.DownAuthorAvatar` into a pure, independently-testable `SyncDecisionHelper.PickAuthorAvatarUrl`, leaving all FS/HTTP I/O in a thin job shell.

**Architecture:** Behavior-preserving "thin shell" extraction (eighth god-class slice), symmetric with slice 6's `PickCoverUrl`. One pure one-liner method moves to `SyncDecisionHelper`; `DownAuthorAvatar` keeps its signature, guards, and all I/O. Golden-master characterization tests pin the helper. No new file, no new enum.

**Tech Stack:** .NET 8 (`net8.0`; local SDK 10 → all `dotnet` commands prefixed `DOTNET_ROLL_FORWARD=LatestMajor`), xUnit (`tests/dy.net.Tests`), C#.

**Spec:** `docs/superpowers/specs/2026-05-22-extract-author-avatar-url-design.md`

---

## File Structure

- **Modify:** `utils/SyncDecisionHelper.cs` — append one pure method `PickAuthorAvatarUrl`; existing 11 methods untouched.
- **Modify:** `job/DouyinBasicSyncJob.cs` — `DownAuthorAvatar` (lines 1121-1140): replace one line (the inline `??` pick) with a helper call; everything else verbatim.
- **Modify:** `tests/dy.net.Tests/SyncDecisionHelperTests.cs` — append one `// ---- PickAuthorAvatarUrl ----` section (4 `[Fact]` + 2 section-local helpers).
- **Modify:** `tests/README.md` — record the new pinned coverage.

`DownAuthorAvatar` is non-`virtual` (cannot be overridden) → job-side change is confined to `DouyinBasicSyncJob.cs`.

---

## Task 1: Extract `PickAuthorAvatarUrl` + thin `DownAuthorAvatar`

**Files:**
- Modify: `utils/SyncDecisionHelper.cs` (append before the class-closing `}` — after `ResolveDuplicateVideoAction`)
- Modify: `job/DouyinBasicSyncJob.cs:1121-1140` (the `DownAuthorAvatar` method)

- [ ] **Step 1: Append `PickAuthorAvatarUrl` to `SyncDecisionHelper`**

In `utils/SyncDecisionHelper.cs`, insert this method immediately after `ResolveDuplicateVideoAction`'s closing `}` and before the class-closing `}`. The file currently ends with `        }\n    }\n}` (the `ResolveDuplicateVideoAction` method close, the class close, the namespace close). Insert between the method close and the class close:

```csharp

        /// <summary>
        /// 从 DouyinBasicSyncJob.DownAuthorAvatar 抽出的纯头像 URL 选取逻辑（无 I/O）。
        /// 行为逐字保留：优先高清 AvatarLarger，回落 AvatarThumb，各取 UrlList 首个。
        /// 注意对 item.Author 无 ?. 空安全——原代码 Author==null 守卫先跑，调用方（job 薄壳）
        /// 保留该守卫并负责只在 Author 非 null 时调用；逐字保留不补守卫。
        /// 由特征化测试 SyncDecisionHelperTests 锁定当前行为。
        /// </summary>
        public static string PickAuthorAvatarUrl(Aweme item)
        {
            // 优先获取高清头像
            return item.Author.AvatarLarger?.UrlList?.FirstOrDefault() ?? item.Author.AvatarThumb?.UrlList?.FirstOrDefault();
        }
```

`SyncDecisionHelper.cs` already has `using dy.net.model.response;` (where `Aweme` lives) and the project has `<ImplicitUsings>enable</ImplicitUsings>` covering `System.Linq` (`FirstOrDefault`). Do NOT add any new `using`. No new file, no new enum.

- [ ] **Step 2: Thin `DownAuthorAvatar` to delegate to the helper**

In `job/DouyinBasicSyncJob.cs`, replace the **entire body** of the `DownAuthorAvatar` method (currently lines 1121-1140). The new method — signature, visibility, parameter order unchanged:

```csharp
        protected async Task<string> DownAuthorAvatar(DouyinCookie cookie, Aweme item,AppConfig config)
        {
            if (config.CloseNfo) return string.Empty;
            if (item.Author == null) return string.Empty;
            var avatarUrl = SyncDecisionHelper.PickAuthorAvatarUrl(item);
            if (string.IsNullOrWhiteSpace(avatarUrl)) return string.Empty;

            // 拼接头像保存路径
            var avatarSavePath = Path.Combine(GetAuthorAvatarBasePath(cookie), $"{item.Author.Uid}.jpg");
            var avatarDir = Path.GetDirectoryName(avatarSavePath);
            // 创建头像保存文件夹
            if (!Directory.Exists(avatarDir)) Directory.CreateDirectory(avatarDir);
            // 如果头像文件不存在，则下载
            if (!File.Exists(avatarSavePath))
            {
                await douyinHttpClientService.DownloadAsync(avatarUrl, avatarSavePath, cookie.Cookies);
            }
            return avatarSavePath;
        }
```

Notes for the implementer:
- The ONLY change vs the original: the two lines `// 优先获取高清头像` + `var avatarUrl = item.Author.AvatarLarger?.UrlList?.FirstOrDefault() ?? item.Author.AvatarThumb?.UrlList?.FirstOrDefault();` become the single line `var avatarUrl = SyncDecisionHelper.PickAuthorAvatarUrl(item);` (the `// 优先获取高清头像` comment moves into the helper). Everything else — both `return string.Empty` guards, the blank guard, the `Path.Combine`/`Directory`/`File`/`DownloadAsync` I/O, all other comments — is verbatim.
- `DownAuthorAvatar` sits at 8-space method indent inside the class — match the surrounding methods exactly.
- Verify the original method spans exactly lines 1121-1140 before replacing (read it first to confirm the exact text).
- Do NOT touch the 3 call sites of `DownAuthorAvatar` (`ProcessSingleVideo:799`, `ProcessDynamicVideo:865`, `ProcessImageSetAndMergeToVideo:1038`).

- [ ] **Step 3: Build — verify 0 errors**

Run: `DOTNET_ROLL_FORWARD=LatestMajor dotnet build dy.net.csproj`
Expected: `Build succeeded. 0 Error(s)`.

- [ ] **Step 4: Run the existing suite — verify still green**

Run: `DOTNET_ROLL_FORWARD=LatestMajor dotnet test tests/dy.net.Tests/dy.net.Tests.csproj`
Expected: `Passed!  - Failed: 0` — **82 passed** (no new tests yet; the extraction must not break the existing golden masters).

- [ ] **Step 5: Commit**

Stage ONLY the two files — explicit paths, never `git add -A`:

```bash
git add utils/SyncDecisionHelper.cs job/DouyinBasicSyncJob.cs
git -c user.name='Claude Code' -c user.email='mjgenab@gmail.com' commit -m "$(cat <<'EOF'
refactor(job): extract author avatar URL pick to SyncDecisionHelper

Move the avatar-URL priority pick (AvatarLarger → AvatarThumb, each
UrlList first) out of DouyinBasicSyncJob.DownAuthorAvatar into a pure
SyncDecisionHelper.PickAuthorAvatarUrl. The job keeps a thin shell:
CloseNfo / Author-null / blank guards, the GetAuthorAvatarBasePath
path build, and Directory / File / DownloadAsync I/O all stay.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 2: Characterization tests for `PickAuthorAvatarUrl`

**Files:**
- Modify: `tests/dy.net.Tests/SyncDecisionHelperTests.cs` (append a new section before the class-closing `}`)

Golden-master tests pinning the helper's CURRENT behavior. The helper is a verbatim port, so first-run values ARE the golden values.

- [ ] **Step 1: Append the test section**

In `tests/dy.net.Tests/SyncDecisionHelperTests.cs`, insert the following block immediately after the last test method (`ResolveDuplicateVideoAction_CurrentTypeMissingFromList_FallsBackToMaxValueSort_Skips` — its closing `}`) and before the class-closing `}`:

```csharp

        // ---- PickAuthorAvatarUrl ----
        // pin: current behavior, not aspirational

        private static ImageInfo AvatarImg(params string[] urls)
            => new ImageInfo { UrlList = urls.ToList() };

        private static Aweme AwemeWithAvatars(ImageInfo larger, ImageInfo thumb)
            => new Aweme { Author = new Author { AvatarLarger = larger, AvatarThumb = thumb } };

        [Fact]
        public void PickAuthorAvatarUrl_AvatarLargerPresent_TakesLargerFirst()
        {
            var item = AwemeWithAvatars(AvatarImg("L1", "L2"), AvatarImg("T1"));
            Assert.Equal("L1", SyncDecisionHelper.PickAuthorAvatarUrl(item));
        }

        [Fact]
        public void PickAuthorAvatarUrl_AvatarLargerNull_FallsBackToThumb()
        {
            var item = AwemeWithAvatars(null, AvatarImg("T1"));
            Assert.Equal("T1", SyncDecisionHelper.PickAuthorAvatarUrl(item));
        }

        [Fact]
        public void PickAuthorAvatarUrl_AvatarLargerEmptyUrlList_FallsBackToThumb()
        {
            // AvatarLarger present but UrlList empty → FirstOrDefault() is null → ?? falls through
            var item = AwemeWithAvatars(AvatarImg(), AvatarImg("T1"));
            Assert.Equal("T1", SyncDecisionHelper.PickAuthorAvatarUrl(item));
        }

        [Fact]
        public void PickAuthorAvatarUrl_BothNull_ReturnsNull()
        {
            var item = AwemeWithAvatars(null, null);
            Assert.Null(SyncDecisionHelper.PickAuthorAvatarUrl(item));
        }
```

Notes for the implementer:
- `Aweme`, `Author`, `ImageInfo` are all in `dy.net.model.response`, already imported at the top of the file (`using dy.net.model.response;`). `System.Linq` (`.ToList()`) resolves via ImplicitUsings — existing tests already use LINQ/`Path.*` with no explicit `using`. Do NOT add any `using` directives.
- The helper names `AvatarImg` and `AwemeWithAvatars` are new. Before inserting, scan the file for any existing method with either name (e.g. `grep -n "AvatarImg\|AwemeWithAvatars" tests/dy.net.Tests/SyncDecisionHelperTests.cs`). If either name already exists, rename the new helper (e.g. `AvatarUrlImg` / `AwemeWithAuthorAvatars`) consistently across all its uses and report the rename. (Slice 6 used `CoverImg` and `AwemeWith…` names — `AvatarImg`/`AwemeWithAvatars` are expected to be free, but verify.)
- Match the indentation of the surrounding test methods exactly (8-space method indent inside the class).
- Do NOT add an `Author == null` test — that path NREs by design (see spec "Quirk"); pinning a crash is forbidden.
- Do NOT modify any existing test or the helper.

- [ ] **Step 2: Run the new section — verify all 4 pass**

Run: `DOTNET_ROLL_FORWARD=LatestMajor dotnet test tests/dy.net.Tests/dy.net.Tests.csproj --filter "FullyQualifiedName~PickAuthorAvatarUrl"`
Expected: `Passed!  - Failed: 0` — **4 passed**.

If any fails: the helper is a verbatim port, so a failure means the test input was mis-traced. Re-trace by hand against the helper logic (`AvatarLarger?.UrlList?.FirstOrDefault() ?? AvatarThumb?.UrlList?.FirstOrDefault()`); fix the test input/expectation. Do NOT modify the helper. Never weaken an assertion.

- [ ] **Step 3: Run the full suite — verify 86 green**

Run: `DOTNET_ROLL_FORWARD=LatestMajor dotnet test tests/dy.net.Tests/dy.net.Tests.csproj`
Expected: `Passed!  - Failed: 0` — **86 passed** (82 + 4).

- [ ] **Step 4: Commit**

Stage ONLY the test file:

```bash
git add tests/dy.net.Tests/SyncDecisionHelperTests.cs
git -c user.name='Claude Code' -c user.email='mjgenab@gmail.com' commit -m "$(cat <<'EOF'
test: golden-master tests pinning PickAuthorAvatarUrl

4 characterization [Fact]s: AvatarLarger preferred (first of UrlList),
fallback to AvatarThumb when AvatarLarger is null or has an empty
UrlList, and null when both are absent. Filtered 65→69, full 82→86.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 3: Update `tests/README.md` coverage doc

**Files:**
- Modify: `tests/README.md`

- [ ] **Step 1: Add `PickAuthorAvatarUrl` to the `SyncDecisionHelper` table row**

In `tests/README.md`, the "What is pinned" table has one row for `SyncDecisionHelper`. It currently ends with this item (last before the closing ` |`):

```
`ResolveDuplicateVideoAction` (优先级去重判定：空表→默认 dy_favorite 最高 / 配置表→Sort 最小者最高 / 四层嵌套产出 SkipDownload·ReplaceExisting / 缺项 Sort 回退 int.MaxValue) |
```

Append `PickAuthorAvatarUrl` before the closing ` |`:

```
`ResolveDuplicateVideoAction` (优先级去重判定：空表→默认 dy_favorite 最高 / 配置表→Sort 最小者最高 / 四层嵌套产出 SkipDownload·ReplaceExisting / 缺项 Sort 回退 int.MaxValue), `PickAuthorAvatarUrl` (头像 URL 选取：AvatarLarger 优先 → AvatarThumb 回落，各取 UrlList 首个 / 全空→null) |
```

- [ ] **Step 2: Update the "What is intentionally NOT covered" `DouyinBasicSyncJob` entry**

In the "## What is intentionally NOT covered (and why)" section, the first bullet (`**\`DouyinBasicSyncJob\` orchestration**`) lists extracted decision logic, then a "Still uncovered:" list. Read the bullet to confirm exact current wording. It currently contains (after the slice-7 edit):

```
  `ResolveDuplicateVideoAction` (`AutoDistinct` 的四层嵌套优先级判定已抽出并
  pinned；其 `config.AutoDistinct`/`File.Exists` 守卫、`JsonConvert` 反序列化、
  `DeleteOldViedo`/`DeleteById` I/O、本地文件缺失分支（`OnlyImgOrOnlyMp3` 判定）仍在
  job 薄壳内、未覆盖；`priorityLevels` 为 null 的 NRE 路径保留不测；薄壳 `DeleteOldViedo`
  的 try/catch 归一化为本刀唯一行为偏差、不在测试覆盖内) — all pinned (see table
  above). Still uncovered:
  `ProcessSingleVideo`/`ProcessDynamicVideo`/`ProcessImageSetAndMergeToVideo`
  orchestration bodies, `SaveVideos`, `DownAuthorAvatar`,
  `CleanupFailedVideos`, `HandleSyncCompletion` — all retain HTTP / FS / DB
  coupling and will be characterized as further seams are extracted in
  follow-up plans.
```

Replace that span with (append the `PickAuthorAvatarUrl` clause after the `ResolveDuplicateVideoAction` clause's close-paren; REMOVE `DownAuthorAvatar` from the "Still uncovered" list):

```
  `ResolveDuplicateVideoAction` (`AutoDistinct` 的四层嵌套优先级判定已抽出并
  pinned；其 `config.AutoDistinct`/`File.Exists` 守卫、`JsonConvert` 反序列化、
  `DeleteOldViedo`/`DeleteById` I/O、本地文件缺失分支（`OnlyImgOrOnlyMp3` 判定）仍在
  job 薄壳内、未覆盖；`priorityLevels` 为 null 的 NRE 路径保留不测；薄壳 `DeleteOldViedo`
  的 try/catch 归一化为本刀唯一行为偏差、不在测试覆盖内),
  `PickAuthorAvatarUrl` (`DownAuthorAvatar` 的头像 URL 选取已抽出并 pinned；其
  `CloseNfo`/`Author`/blank 守卫、`GetAuthorAvatarBasePath`/`Path.Combine` 路径派生、
  `Directory`/`File`/`DownloadAsync` I/O 仍在 job 薄壳内、未覆盖；`Author == null`
  的 NRE 路径保留不测) — all pinned (see table
  above). Still uncovered:
  `ProcessSingleVideo`/`ProcessDynamicVideo`/`ProcessImageSetAndMergeToVideo`
  orchestration bodies, `SaveVideos`,
  `CleanupFailedVideos`, `HandleSyncCompletion` — all retain HTTP / FS / DB
  coupling and will be characterized as further seams are extracted in
  follow-up plans.
```

- [ ] **Step 3: Verify the doc reads correctly**

Run: `grep -n "PickAuthorAvatarUrl" tests/README.md`
Expected: 2 matches (the table row + the NOT-covered entry).

Run: `grep -n "DownAuthorAvatar" tests/README.md`
Expected: 1 match — inside the new `PickAuthorAvatarUrl` clause ("`DownAuthorAvatar` 的头像 URL 选取已抽出"). It must NO LONGER appear in the "Still uncovered" list.

Run: `DOTNET_ROLL_FORWARD=LatestMajor dotnet test tests/dy.net.Tests/dy.net.Tests.csproj`
Expected: `Passed!  - Failed: 0` — **86 passed** (doc change must not affect the build/tests).

- [ ] **Step 4: Commit**

Stage `tests/README.md` and this plan file:

```bash
git add tests/README.md docs/superpowers/plans/2026-05-22-extract-author-avatar-url.md
git -c user.name='Claude Code' -c user.email='mjgenab@gmail.com' commit -m "$(cat <<'EOF'
docs: pin PickAuthorAvatarUrl coverage in tests/README

Also commits the eighth-slice implementation plan.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Final Steps (after all tasks)

- [ ] Push the commit chain to origin: `git push origin decompile/dy-sync-lib` — **do NOT merge, do NOT open a PR** (standing constraint). This environment may need a retry on a transient TLS handshake error; verify with `git status -sb` (expect `## decompile/dy-sync-lib...origin/decompile/dy-sync-lib` with no `ahead`).
- [ ] Update project memory (`project-dysync-security-hardening.md`): eighth slice done, `SyncDecisionHelper` now 12 pure methods, `SyncDecisionHelperTests` 69 cases, full suite 86 green, branch head = new push SHA.

---

## Self-Review

**Spec coverage:**
- `SyncDecisionHelper.PickAuthorAvatarUrl` (verbatim port of the `??` pick) → Task 1 Step 1. ✓
- Thin `DownAuthorAvatar` shell, all guards/I/O retained, only the pick line changed → Task 1 Step 2. ✓
- `BuildAuthorAvatarPath` deliberately NOT extracted → not a task; the thin shell in Task 1 Step 2 keeps `Path.Combine` in the job. ✓
- "Quirk" (no `?.` on `item.Author`, preserved verbatim; `Author == null` NRE not pinned) → Task 1 Step 1 doc comment + Task 2 Step 1 note. ✓
- 4 characterization `[Fact]`s (Larger-preferred, Larger-null fallback, Larger-empty fallback, both-null → null) → Task 2 Step 1. ✓
- `tests/README.md` updates (table row + NOT-covered entry, `DownAuthorAvatar` removed from "Still uncovered") → Task 3. ✓
- Build/test via `DOTNET_ROLL_FORWARD=LatestMajor`, explicit `git add <path>`, push not merge → all task steps + Final Steps. ✓

**Placeholder scan:** No TBD/TODO; every code step shows complete code; every command shows expected output. ✓

**Type consistency:** `PickAuthorAvatarUrl(Aweme item)` returning `string` — identical across Task 1 (helper, job call) and Task 2 (4 test calls). Test helpers `AvatarImg(params string[])→ImageInfo` and `AwemeWithAvatars(ImageInfo, ImageInfo)→Aweme` are consistent across all 4 test uses. `ImageInfo.UrlList` is `List<string>` (confirmed). ✓

**Test trace check:** Test 1 — `AvatarImg("L1","L2")` → `UrlList=["L1","L2"]` → `FirstOrDefault()="L1"` → returns `"L1"`. Test 2 — `larger=null` → `null?.UrlList?...` = null → `?? "T1"`. Test 3 — `AvatarImg()` → `UrlList=[]` → `FirstOrDefault()=null` → `?? "T1"`. Test 4 — both null → `null ?? null` = null. All match the asserted values. ✓
