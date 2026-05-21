# Extract `CreateSaveFolder` Pure Path Logic Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Move the pure path-construction part of `DouyinBasicSyncJob.CreateSaveFolder` into `SyncDecisionHelper.BuildVideoSaveFolderCandidates` (returns a `(primary, collisionResolved)` tuple); the job keeps a `protected virtual` shell that owns the `Directory.Exists`/`CreateDirectory` I/O orchestration. Pin the helper with 5 golden-master tests. All 6 subclass overrides and 3 call sites stay untouched.

**Architecture:** Same shape as the prior four slices (`IsAwemeValid` family, `BuildVideoEntity`, `PickBestVideoBitRate`, `BuildVideoFileName`). This slice differs in that `CreateSaveFolder` mixes pure logic with I/O — only the pure path construction is extracted; `Directory.*` calls remain in the job's thin virtual shell. The helper drops the 3 formal params unused by the base body (`config`/`followed`/`cate`); the job-side virtual signature keeps all 5 for contract compatibility. The one structural deviation — the helper computes both candidate paths eagerly, whereas the original only computed `collisionResolved` in the `else` branch — is observably equivalent because `SanitizeLinuxFileName` and `Path.Combine` are pure functions.

**Tech Stack:** .NET 8 (build via `DOTNET_ROLL_FORWARD=LatestMajor` on local SDK 10), xUnit, existing `dy.net.Tests` project.

**Spec:** `docs/superpowers/specs/2026-05-21-extract-buildsavefolder-candidates-design.md`

**Pre-conditions:**
- Branch `decompile/dy-sync-lib`, working tree clean, HEAD = `a1324e4` (spec commit).
- Baseline test count: `dy.net.Tests` = **60 green**, filtered `SyncDecisionHelperTests` = **43 green**.
- `job/DouyinBasicSyncJob.cs:203-219` contains the original `CreateSaveFolder` base body; its XML doc is on lines 194-202; its 3 call sites are `ProcessSingleVideo:836`, `ProcessDynamicVideo:895`, `ProcessImageSetAndMergeToVideo:1041`.
- `utils/SyncDecisionHelper.cs` is the target helper (172 lines pre-task; already holds `GetNextCursor`/`IsAwemeValid`/`GetVideoTags`/`IsSyncLimitReached`/`BuildVideoEntity`/`PickBestVideoBitRate`/`BuildVideoFileName`). `BuildVideoFileName` closes on **line 170**; the class-closing `}` is **line 171**; insert the new method between them.
- `tests/dy.net.Tests/SyncDecisionHelperTests.cs` is the target test file (556 lines, single class organized by `// ---- <Method> ----` section comments; the `BuildVideoFileName` section is currently last, ending line 554; class-closing `}` is line 555).
- 6 subclass `CreateSaveFolder` overrides exist and must all stay untouched: `DouyinCollectCustomSyncJob:17`, `DouyinMixSyncJob:28`, `DouyinSeriesSyncJob:27`, `DouYinCollectSyncJob.cs:41`, `DouYinFavoritSyncJob.cs:46`, `DouyinFollowedSyncJob:46`.
- `dy.net.csproj` has `<ImplicitUsings>enable</ImplicitUsings>` (net8.0), so `System.IO` (`Path`/`Directory`) is globally imported — no `using` needed in the helper. `DouyinFileNameHelper` shares the `dy.net.utils` namespace with `SyncDecisionHelper`.

---

## File Structure

| File | Action | Responsibility |
|------|--------|----------------|
| `utils/SyncDecisionHelper.cs` | Modify (append before line 171 class close) | Add `BuildVideoSaveFolderCandidates(DouyinCookie, Aweme)` static method returning `(string primary, string collisionResolved)`. Verbatim move of the pure path construction; drops `config`/`followed`/`cate`. |
| `job/DouyinBasicSyncJob.cs` | Modify (lines 203-219) | Replace method body with a delegate-to-helper shell that retains the `Directory.Exists`/`CreateDirectory` I/O. Signature/visibility/XML doc unchanged. |
| `tests/dy.net.Tests/SyncDecisionHelperTests.cs` | Modify | Append 5 `[Fact]` tests in a new `// ---- BuildVideoSaveFolderCandidates ----` section, after the `BuildVideoFileName` section, before final class close. |
| `tests/README.md` | Modify | Extend the `SyncDecisionHelper` row to mention `BuildVideoSaveFolderCandidates`; update the `DouyinBasicSyncJob` "NOT covered" bullet. |
| `docs/superpowers/plans/2026-05-21-extract-buildsavefolder-candidates.md` | This file | Tracked in source for review. |

---

## Task 1: Extract method + add job-side delegate

**Files:**
- Modify: `utils/SyncDecisionHelper.cs` (append before class close on line 171)
- Modify: `job/DouyinBasicSyncJob.cs:203-219`
- No test changes — relies on the existing 60-test suite as the regression safety net.

- [ ] **Step 1.1: Verify baseline is green**

Run:
```bash
DOTNET_ROLL_FORWARD=LatestMajor dotnet test tests/dy.net.Tests/dy.net.Tests.csproj --nologo
```
Expected last line: `已通过! - 失败:     0，通过:    60，已跳过:     0，总计:    60`

If not 60 green, STOP — the working tree has unexpected state; investigate before proceeding.

- [ ] **Step 1.2: Append `BuildVideoSaveFolderCandidates` to `SyncDecisionHelper`**

In `utils/SyncDecisionHelper.cs`, locate the closing `}` of `BuildVideoFileName` (currently line 170) and the class-closing `}` on line 171. Insert the following block **between them** (one blank line above the new method, no blank line between the new method and the class-closing `}`):

```csharp

        /// <summary>
        /// 从 DouyinBasicSyncJob.CreateSaveFolder 抽出的纯路径构造逻辑（无 I/O）。
        /// 行为逐字保留：item.Desc/AwemeId 经 SanitizeLinuxFileName 清洗为子目录名，
        /// 返回两条候选路径——primary（cookie.SavePath/子目录）与 collisionResolved
        /// （撞名时的 cookie.SavePath/子目录_AwemeId）。
        /// 目录存在性判断与 Directory.CreateDirectory 的 I/O 编排留在 job 内。
        /// 原方法 config/followed/cate 参数在 base body 中未引用，故 helper 签名不带这三项。
        /// 两条候选一并求值；SanitizeLinuxFileName 与 Path.Combine 均为纯函数，
        /// 提前求值与原方法 else 分支的延迟求值可观察行为等价。
        /// 由特征化测试 SyncDecisionHelperTests 锁定当前行为。
        /// </summary>
        public static (string primary, string collisionResolved) BuildVideoSaveFolderCandidates(DouyinCookie cookie, Aweme item)
        {
            var subFolder = DouyinFileNameHelper.SanitizeLinuxFileName(item.Desc, item.AwemeId, true);
            return (
                Path.Combine(cookie.SavePath, subFolder),
                Path.Combine(cookie.SavePath, subFolder + "_" + item.AwemeId)
            );
        }
```

Notes on the move:
- The `subFolder` computation and both `Path.Combine` calls are taken **verbatim** from the job body at lines 203-219. The `SanitizeLinuxFileName(..., true)` argument, the `"_"` string literal, and the `subFolder + "_" + item.AwemeId` concatenation are preserved exactly.
- Indentation: method declaration at 8 spaces (same level as the other helper methods), method body at 12 spaces, the tuple elements step in 4 more.
- Three outer signature params from the job (`AppConfig config`, `DouyinFollowed followed`, `DouyinCollectCate cate`) are intentionally **omitted** because the base body does not reference them. The job-side virtual signature still takes them — the helper does not.
- The helper computes **both** candidate paths eagerly. The original only computed `collisionResolved` inside the `else` branch. This is observably equivalent: `SanitizeLinuxFileName` (string/regex only — see `utils/DouyinFileNameHelper.cs`) and `Path.Combine` are pure, deterministic, side-effect-free. This is the only structural deviation in this slice and is documented in the XML doc above.
- `using` directives: `dy.net.csproj` has `<ImplicitUsings>enable</ImplicitUsings>`, so `System.IO` (`Path`) is in the global using set. `DouyinCookie`/`Aweme` are already used elsewhere in this file (e.g. `IsSyncLimitReached`, `BuildVideoEntity`). `DouyinFileNameHelper` shares the `dy.net.utils` namespace. **Do not add any new `using`.**

- [ ] **Step 1.3: Replace `CreateSaveFolder` body in the job with a delegate shell**

In `job/DouyinBasicSyncJob.cs`, replace lines 203-219 — the entire method body block (NOT the XML doc on lines 194-202):

```csharp
        protected virtual string CreateSaveFolder(DouyinCookie cookie, Aweme item, AppConfig config, DouyinFollowed followed, DouyinCollectCate cate)
        {
            var subFolder = DouyinFileNameHelper.SanitizeLinuxFileName(item.Desc, item.AwemeId, true);
            var folder = Path.Combine(cookie.SavePath, subFolder);
            if (!Directory.Exists(folder))
            {
                Directory.CreateDirectory(folder);
            }
            else
            {
                //说明文件夹存在，检查里面有没有文件，如果已经有视频文件了，说明视频标题相同，那么应该重新创建文件夹,+id

                folder = Path.Combine(cookie.SavePath, subFolder + "_" + item.AwemeId);
            }
            return folder;

        }
```

with the delegate shell (the I/O stays in the job):

```csharp
        protected virtual string CreateSaveFolder(DouyinCookie cookie, Aweme item, AppConfig config, DouyinFollowed followed, DouyinCollectCate cate)
        {
            var (primary, collisionResolved) = SyncDecisionHelper.BuildVideoSaveFolderCandidates(cookie, item);
            if (!Directory.Exists(primary))
            {
                Directory.CreateDirectory(primary);
                return primary;
            }
            //说明文件夹存在，检查里面有没有文件，如果已经有视频文件了，说明视频标题相同，那么应该重新创建文件夹,+id
            return collisionResolved;
        }
```

Constraints:
- The XML doc comment block on lines 194-202 (`/// <summary>` ... `/// <returns>`) stays exactly as-is.
- The virtual signature is **unchanged**: same modifiers (`protected virtual`), same return type, same five parameters in the same order. `config`/`followed`/`cate` remain on the signature for virtual contract compatibility, even though `BuildVideoSaveFolderCandidates` does not receive them.
- The `Directory.Exists` / `Directory.CreateDirectory` calls (I/O) stay in the job. The control flow is equivalent: original `if(!Exists){Create;}else{folder=...;}return folder;` ⇒ new `if(!Exists){Create;return primary;} return collisionResolved;`. For any input, both produce the same return value and the same single (or zero) `CreateDirectory` call.
- The撞名 comment (`//说明文件夹存在...`) is preserved verbatim.
- Do **not** touch the 3 call sites (`ProcessSingleVideo:836`, `ProcessDynamicVideo:895`, `ProcessImageSetAndMergeToVideo:1041` — still `CreateSaveFolder(cookie, item, config, followed, cate)`).
- Do **not** touch any of the 6 subclass overrides (`DouyinCollectCustomSyncJob`, `DouyinMixSyncJob`, `DouyinSeriesSyncJob`, `DouYinCollectSyncJob.cs`, `DouYinFavoritSyncJob.cs`, `DouyinFollowedSyncJob`).
- Do **not** add or remove blank lines around adjacent methods. The XML doc that follows (for `GetVideoFileName`, currently starting line 221) must remain separated by the same blank-line pattern as before.

- [ ] **Step 1.4: Build the solution**

Run:
```bash
DOTNET_ROLL_FORWARD=LatestMajor dotnet build dy.net.csproj --nologo
```
Expected: `已成功生成` / `Build succeeded` with **0 errors**. Warning count must not increase versus baseline.

If the build fails with an unresolved `DouyinCookie` / `Aweme` / `Path` / `DouyinFileNameHelper` symbol inside the helper, that means a `using` is missing — but **do not add one** without first verifying the file. `DouyinCookie`/`Aweme` are already referenced by existing helper methods; `Path` comes from implicit `System.IO`; `DouyinFileNameHelper` is same-namespace. If the build still fails, STOP and report — something else is wrong.

- [ ] **Step 1.5: Run the full test suite**

Run:
```bash
DOTNET_ROLL_FORWARD=LatestMajor dotnet test tests/dy.net.Tests/dy.net.Tests.csproj --nologo
```
Expected: `通过:    60` — exactly the same count as Step 1.1. Zero failures.

The existing characterization tests do not test `BuildVideoSaveFolderCandidates` directly. Their staying green is the regression signal for "extraction did not break compilation or unrelated paths". The new helper's own behavior is pinned in Task 2.

- [ ] **Step 1.6: Verify the 6 subclass overrides are zero-changed**

Run:
```bash
git diff --stat
```
Expected: exactly 2 files modified (`utils/SyncDecisionHelper.cs`, `job/DouyinBasicSyncJob.cs`). No other file may appear — in particular, none of `job/DouyinCollectCustomSyncJob.cs`, `job/DouyinMixSyncJob.cs`, `job/DouyinSeriesSyncJob.cs`, `job/DouYinCollectSyncJob.cs`, `job/DouYinFavoritSyncJob.cs`, `job/DouyinFollowedSyncJob.cs`.

- [ ] **Step 1.7: Commit**

```bash
git add utils/SyncDecisionHelper.cs job/DouyinBasicSyncJob.cs
git -c user.name='Claude Code' -c user.email='mjgenab@gmail.com' commit -m "$(cat <<'EOF'
refactor(job): extract CreateSaveFolder pure path logic to SyncDecisionHelper.BuildVideoSaveFolderCandidates

把 CreateSaveFolder 的纯路径构造（SanitizeLinuxFileName + Path.Combine）
逐字搬到 SyncDecisionHelper.BuildVideoSaveFolderCandidates，返回
(primary, collisionResolved) 元组；job 内保留 protected virtual
薄壳，Directory.Exists/CreateDirectory 的 I/O 编排留在 job。

helper 签名丢掉 base body 未引用的 config/followed/cate 参数（YAGNI），
virtual 签名（五参数）不变。helper 提前求值两条候选——
SanitizeLinuxFileName/Path.Combine 均为纯函数，与原 else 分支
延迟求值可观察行为等价。

6 个子类 CreateSaveFolder override 零改动；3 个调用点不动。

现有 60 个测试全绿；BuildVideoSaveFolderCandidates 的特征化测试
在下一个 commit 加上。

Spec: docs/superpowers/specs/2026-05-21-extract-buildsavefolder-candidates-design.md

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

Verify the commit:
```bash
git log -1 --stat
```
Expected: 2 files changed; `utils/SyncDecisionHelper.cs` +~20 lines, `job/DouyinBasicSyncJob.cs` ~-17/+10 lines.

---

## Task 2: Characterization tests for `BuildVideoSaveFolderCandidates`

**Files:**
- Modify: `tests/dy.net.Tests/SyncDecisionHelperTests.cs` (append before final class-closing `}` on line 555 — after the existing `BuildVideoFileName` section)

These are golden-master tests — pin the **current** behavior. If a test fails during this task, the bug is in your test setup, not in `BuildVideoSaveFolderCandidates` (which is verbatim from the job and was already shipped in Task 1). The helper has no I/O — tests are pure in-memory and never touch the filesystem.

- [ ] **Step 2.1: Add a `// ---- BuildVideoSaveFolderCandidates ----` section + 5 `[Fact]` tests**

Open `tests/dy.net.Tests/SyncDecisionHelperTests.cs`. Find the **last** `}` on line 555 (it closes class `SyncDecisionHelperTests`). The line immediately above it (554) is the close of the last `[Fact]` in the `BuildVideoFileName` section (`BuildVideoFileName_CateNonCustomCollect_FollowsDefaultBranch`). Insert the following block **before** the class-closing `}` (preserve one blank line between the previous Fact and the new section header):

```csharp

        // ---- BuildVideoSaveFolderCandidates ----

        // pin: current behavior, not aspirational

        private static DouyinCookie FolderCookie(string savePath)
            => new DouyinCookie { SavePath = savePath };

        private static Aweme FolderAweme(string desc, string awemeId)
            => new Aweme { Desc = desc, AwemeId = awemeId };

        [Fact]
        public void BuildVideoSaveFolderCandidates_Primary_CombinesSavePathWithSanitizedDesc()
        {
            var (primary, _) = SyncDecisionHelper.BuildVideoSaveFolderCandidates(
                FolderCookie("/data"),
                FolderAweme("我的视频", "123"));

            var expected = Path.Combine(
                "/data",
                DouyinFileNameHelper.SanitizeLinuxFileName("我的视频", "123", true));
            Assert.Equal(expected, primary);
        }

        [Fact]
        public void BuildVideoSaveFolderCandidates_CollisionResolved_AppendsUnderscoreAwemeId()
        {
            var (_, collisionResolved) = SyncDecisionHelper.BuildVideoSaveFolderCandidates(
                FolderCookie("/data"),
                FolderAweme("我的视频", "123"));

            var subFolder = DouyinFileNameHelper.SanitizeLinuxFileName("我的视频", "123", true);
            var expected = Path.Combine("/data", subFolder + "_" + "123");
            Assert.Equal(expected, collisionResolved);
        }

        [Fact]
        public void BuildVideoSaveFolderCandidates_BlankDesc_FallsBackToAwemeIdAsSubFolder()
        {
            var (primary, collisionResolved) = SyncDecisionHelper.BuildVideoSaveFolderCandidates(
                FolderCookie("/data"),
                FolderAweme("", "123"));

            var subFolder = DouyinFileNameHelper.SanitizeLinuxFileName("", "123", true);
            Assert.Equal(Path.Combine("/data", subFolder), primary);
            Assert.Equal(Path.Combine("/data", subFolder + "_" + "123"), collisionResolved);
        }

        [Fact]
        public void BuildVideoSaveFolderCandidates_IllegalChars_SanitizedIntoSubFolder()
        {
            var (primary, _) = SyncDecisionHelper.BuildVideoSaveFolderCandidates(
                FolderCookie("/data"),
                FolderAweme("a/b:c", "123"));

            var expected = Path.Combine(
                "/data",
                DouyinFileNameHelper.SanitizeLinuxFileName("a/b:c", "123", true));
            Assert.Equal(expected, primary);
        }

        [Fact]
        public void BuildVideoSaveFolderCandidates_BothCandidates_ShareSameSanitizedSubFolder()
        {
            var (primary, collisionResolved) = SyncDecisionHelper.BuildVideoSaveFolderCandidates(
                FolderCookie("/data"),
                FolderAweme("我的视频", "123"));

            // collisionResolved 恰为 primary 词根 + "_" + AwemeId（仅后缀不同，词根一致）
            Assert.Equal(primary + "_" + "123", collisionResolved);
        }
```

Notes:
- The two `private static` helpers (`FolderCookie`, `FolderAweme`) are scoped to this section's intent. They are intentionally **not** merged with `StdAweme`/`Ck`/`StdBitRate` (BuildVideoEntity section), `Br`/`BrNoPlayAddr`/`AwemeWith` (PickBestVideoBitRate section), or `AwemeWithId`/`AwemeWithEpisode`/`Cate` (BuildVideoFileName section) — each section keeps its own minimal builders to remain self-contained. The names `FolderCookie`/`FolderAweme` are deliberately distinct from every existing helper name in the file to avoid a duplicate-member compile error.
- Expected values are computed **live** with `Path.Combine` + `DouyinFileNameHelper.SanitizeLinuxFileName` rather than hardcoded. This pins "the helper equals that composition of the two pure functions" without baking in a platform's path separator or the exact Sanitize output — which is the correct characterization target for this slice.
- Test #5 pins the platform-independent invariant `collisionResolved == primary + "_" + AwemeId`: since `Path.Combine(savePath, subFolder)` and `Path.Combine(savePath, subFolder + "_" + awemeId)` share the same `savePath` + separator + `subFolder` prefix, the second is exactly the first with `"_" + awemeId` appended.
- `using dy.net.utils;` is already at the top of the file (line 4), so `DouyinFileNameHelper` and `SyncDecisionHelper` resolve. `Path` comes from implicit `System.IO`. No new `using` needed.

- [ ] **Step 2.2: Run filtered tests**

Run:
```bash
DOTNET_ROLL_FORWARD=LatestMajor dotnet test tests/dy.net.Tests/dy.net.Tests.csproj --nologo --filter "FullyQualifiedName~SyncDecisionHelperTests"
```
Expected: `通过:    48` (43 prior + 5 new).

If any new test fails: STOP. Either a test setup field has a model-shape mismatch (`DouyinCookie.SavePath` / `Aweme.Desc` / `Aweme.AwemeId` are the only fields used — all are plain settable string properties exercised by other sections), or `BuildVideoSaveFolderCandidates` is not byte-for-byte identical with the original. Read the failure message; do **not** weaken the assertion.

- [ ] **Step 2.3: Run the full suite**

Run:
```bash
DOTNET_ROLL_FORWARD=LatestMajor dotnet test tests/dy.net.Tests/dy.net.Tests.csproj --nologo
```
Expected: `通过:    65` (60 prior + 5 new), 0 failed, 0 skipped.

- [ ] **Step 2.4: Commit**

```bash
git add tests/dy.net.Tests/SyncDecisionHelperTests.cs
git -c user.name='Claude Code' -c user.email='mjgenab@gmail.com' commit -m "$(cat <<'EOF'
test: golden-master tests pinning SyncDecisionHelper.BuildVideoSaveFolderCandidates

新增 5 个 [Fact] 覆盖 primary = SavePath/sanitized-subFolder /
collisionResolved 追加 _{AwemeId} / 空 Desc 走 AwemeId 兜底 /
非法字符经 SanitizeLinuxFileName / 两候选共享同一 subFolder 词根。

期望值用 Path.Combine + SanitizeLinuxFileName 现场计算，不硬编码
跨平台路径；helper 无 I/O，测试纯内存、不碰文件系统。

dy.net.Tests: 60 → 65 全绿。
SyncDecisionHelperTests filter: 43 → 48 全绿。

Spec: docs/superpowers/specs/2026-05-21-extract-buildsavefolder-candidates-design.md

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

Verify:
```bash
git log -1 --stat
```
Expected: 1 file changed; `tests/dy.net.Tests/SyncDecisionHelperTests.cs` +~70 lines.

---

## Task 3: Update `tests/README.md` + push

**Files:**
- Modify: `tests/README.md` — both the `SyncDecisionHelper` row in the `## What is pinned` table and the `DouyinBasicSyncJob` bullet under `## What is intentionally NOT covered`.

- [ ] **Step 3.1: Extend the `SyncDecisionHelper` row**

In `tests/README.md`, find the current `SyncDecisionHelper` row (a single-line markdown table row; it should already mention `GetNextCursor`, `IsAwemeValid`, `GetVideoTags`, `IsSyncLimitReached`, `BuildVideoEntity`, `PickBestVideoBitRate`, `BuildVideoFileName` — added in prior slices).

Append `, \`BuildVideoSaveFolderCandidates\` (primary = SavePath/sanitized-subFolder / collisionResolved 追加 _{AwemeId} / 空 Desc 走 AwemeId 兜底 / 非法字符经 Sanitize)` to the end of the `Locks` column, **immediately before the closing pipe**. The full updated line must read:

```markdown
| `SyncDecisionHelper` | `SyncDecisionHelperTests` | `GetNextCursor` (Cursor→MaxCursor→`"0"`, null-safe), `IsAwemeValid` (3-level null guard), `GetVideoTags` (per-level pick, missing→null), `IsSyncLimitReached` (cate 30-cap vs `BatchCount` cap, `OnlySyncNew` passthrough, `dy_follows` `!FullSync`, mix/series short-circuit), `BuildVideoEntity` (纯字段映射 / cate 标题覆盖 / `OnlyImgOrOnlyMp3` / `DyUserId` 分支 / `AuthorAvatarUrl` 回落 / `FileSize` 零回落), `PickBestVideoBitRate` (encoder=265 优先 H.265 + 回退 H.264 / 默认或 ≠265 仅 H.264 / 空或 null `UrlList` 跳过), `BuildVideoFileName` (custom_collect Format vs mp4 兜底 / mix-series 数字 episode 的 S01E{D2} / 默认 AwemeId.mp4 / cate 非 custom_collect 走默认), `BuildVideoSaveFolderCandidates` (primary = SavePath/sanitized-subFolder / collisionResolved 追加 _{AwemeId} / 空 Desc 走 AwemeId 兜底 / 非法字符经 Sanitize) |
```

If the row's existing prefix differs from the above in any way other than the appended `BuildVideoSaveFolderCandidates` clause, keep the existing prefix verbatim and only append the new clause before the closing pipe — do not rewrite the rest of the row from this plan.

- [ ] **Step 3.2: Update the `DouyinBasicSyncJob` NOT-covered bullet**

In `tests/README.md`, find the current `DouyinBasicSyncJob orchestration` bullet (a multi-line markdown list item under `## What is intentionally NOT covered`). After the prior slice it reads (text may have minor wording drift — match on the `Still uncovered:` list contents):

```markdown
- **`DouyinBasicSyncJob` orchestration** — HTTP + filesystem + DB coupled with
  no seams. Pure decision logic extracted so far: `GetNextCursor`,
  `IsAwemeValid`, `GetVideoTags`, `IsSyncLimitReached`, `BuildVideoEntity` (除
  `DynamicVideos`/`NfoFileGenerator` 副作用块外), `PickBestVideoBitRate`,
  `BuildVideoFileName` (基类体；`DouyinFollowedSyncJob.GetVideoFileName`
  override 仍是子类业务实现，未覆盖；「TryParse 失败」fallback 在当前 model
  下不可达，保留代码不写测试) — all pinned (see table above). Still
  uncovered: `CreateSaveFolder`, `ProcessSingleVideo`/`ProcessDynamicVideo`/
  `ProcessImageSetAndMergeToVideo` orchestration bodies, `AutoDistinct`,
  `SaveVideos`, `DownVideoCover`, `DownAuthorAvatar`, `CleanupFailedVideos`,
  `HandleSyncCompletion` — all retain HTTP / FS / DB coupling and will be
  characterized as further seams are extracted in follow-up plans.
```

Replace it with:

```markdown
- **`DouyinBasicSyncJob` orchestration** — HTTP + filesystem + DB coupled with
  no seams. Pure decision logic extracted so far: `GetNextCursor`,
  `IsAwemeValid`, `GetVideoTags`, `IsSyncLimitReached`, `BuildVideoEntity` (除
  `DynamicVideos`/`NfoFileGenerator` 副作用块外), `PickBestVideoBitRate`,
  `BuildVideoFileName` (基类体；`DouyinFollowedSyncJob.GetVideoFileName`
  override 仍是子类业务实现，未覆盖；「TryParse 失败」fallback 在当前 model
  下不可达，保留代码不写测试), `BuildVideoSaveFolderCandidates`
  (`CreateSaveFolder` 基类体的纯路径构造已抽出并 pinned；其
  `Directory.Exists`/`CreateDirectory` I/O 编排仍在 job 薄壳内、未覆盖；
  6 个子类 `CreateSaveFolder` override 仍是子类业务实现，未覆盖) — all
  pinned (see table above). Still uncovered:
  `ProcessSingleVideo`/`ProcessDynamicVideo`/`ProcessImageSetAndMergeToVideo`
  orchestration bodies, `AutoDistinct`, `SaveVideos`, `DownVideoCover`,
  `DownAuthorAvatar`, `CleanupFailedVideos`, `HandleSyncCompletion` — all
  retain HTTP / FS / DB coupling and will be characterized as further seams
  are extracted in follow-up plans.
```

Diff intent:
1. Add the `, \`BuildVideoSaveFolderCandidates\` (...)` clause to the "extracted so far" list.
2. **Remove `CreateSaveFolder,`** from the "Still uncovered" list (because its base-body pure logic is now pinned; the residual I/O orchestration is noted inside the new clause). Keep the rest of the "Still uncovered" list unchanged.

- [ ] **Step 3.3: Verify the diff is clean**

Run:
```bash
git diff tests/README.md
```
Expected:
- Two hunks: one on the table row (single-line change), one on the bullet (multi-line replacement).
- No trailing-whitespace edits anywhere else.
- No other files touched.

- [ ] **Step 3.4: Smoke test**

Run:
```bash
DOTNET_ROLL_FORWARD=LatestMajor dotnet test tests/dy.net.Tests/dy.net.Tests.csproj --nologo
```
Expected: `通过:    65` (unchanged from Task 2; docs edit cannot affect tests).

- [ ] **Step 3.5: Commit**

```bash
git add tests/README.md docs/superpowers/plans/2026-05-21-extract-buildsavefolder-candidates.md
git -c user.name='Claude Code' -c user.email='mjgenab@gmail.com' commit -m "$(cat <<'EOF'
docs: pin BuildVideoSaveFolderCandidates coverage in tests/README

把 BuildVideoSaveFolderCandidates 加入 SyncDecisionHelper 表格行；
DouyinBasicSyncJob 的 NOT-covered 清单移除 CreateSaveFolder
（基类体纯路径逻辑已 pinned），并标注 Directory.* I/O 编排仍在
job 薄壳内未覆盖、6 个子类 override 未覆盖。

落地本刀实现计划：
docs/superpowers/plans/2026-05-21-extract-buildsavefolder-candidates.md

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

Verify:
```bash
git log --oneline -4
```
Expected (top → bottom):
1. `docs: pin BuildVideoSaveFolderCandidates coverage in tests/README`
2. `test: golden-master tests pinning SyncDecisionHelper.BuildVideoSaveFolderCandidates`
3. `refactor(job): extract CreateSaveFolder pure path logic to SyncDecisionHelper.BuildVideoSaveFolderCandidates`
4. `docs: spec for extracting CreateSaveFolder pure path logic`

- [ ] **Step 3.6: Push to origin (no merge, no PR)**

```bash
git push origin decompile/dy-sync-lib
```
Expected: 4 new commits land on `origin/decompile/dy-sync-lib`. A `User cancelled dialog` message can be misleading — always confirm the real state with:

```bash
git status -sb
```
Expected: `## decompile/dy-sync-lib...origin/decompile/dy-sync-lib` (no `[ahead N]` / `[领先 N]`).

---

## Done criteria

- `git log --oneline -4` shows the four commits in the order above.
- `dotnet test` reports **65 passed / 0 failed / 0 skipped**.
- `git status -sb` shows clean working tree and synced-with-origin.
- `job/DouyinBasicSyncJob.cs` body of `CreateSaveFolder` is the delegate shell: `BuildVideoSaveFolderCandidates` tuple deconstruction + `Directory.Exists`/`CreateDirectory` I/O. XML doc on lines 194-202 unchanged. Five-parameter virtual signature unchanged.
- `utils/SyncDecisionHelper.cs` contains `BuildVideoSaveFolderCandidates` as its last method (after `BuildVideoFileName`).
- `tests/dy.net.Tests/SyncDecisionHelperTests.cs` contains the `// ---- BuildVideoSaveFolderCandidates ----` section with 5 `[Fact]` tests.
- `tests/README.md` row for `SyncDecisionHelper` mentions `BuildVideoSaveFolderCandidates`; the `DouyinBasicSyncJob` "NOT covered" bullet no longer lists `CreateSaveFolder`.
- `git diff a1324e4..HEAD -- 'job/Douyin*SyncJob.cs' 'job/DouYin*SyncJob.cs'` shows changes **only** in `DouyinBasicSyncJob.cs`; all 6 subclass override files (`DouyinCollectCustomSyncJob.cs`, `DouyinMixSyncJob.cs`, `DouyinSeriesSyncJob.cs`, `DouYinCollectSyncJob.cs`, `DouYinFavoritSyncJob.cs`, `DouyinFollowedSyncJob.cs`) remain untouched.
- No merge, no PR.
