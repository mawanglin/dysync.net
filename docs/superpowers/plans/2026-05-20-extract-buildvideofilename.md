# Extract `GetVideoFileName` Base Body Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Move `DouyinBasicSyncJob.GetVideoFileName` base body verbatim into `SyncDecisionHelper.BuildVideoFileName`, drop the two unused formal parameters (`cookie`/`config`) from the helper API, lift `VideoType` abstract property to a `videoType` param, and pin the behavior with 7 golden-master tests. `DouyinFollowedSyncJob.GetVideoFileName` override stays untouched.

**Architecture:** Same shape as the prior three slices (`IsAwemeValid` family, `BuildVideoEntity`, `PickBestVideoBitRate`). Copy the base body unchanged into the existing `SyncDecisionHelper` static class; the job retains a `protected virtual` expression-bodied delegate with full signature (cookie/config retained on virtual contract, helper drops them). The single existing override in `DouyinFollowedSyncJob` is independent of `base` and gets zero changes.

**Tech Stack:** .NET 8 (build via `DOTNET_ROLL_FORWARD=LatestMajor` on local SDK 10), xUnit, existing `dy.net.Tests` project.

**Spec:** `docs/superpowers/specs/2026-05-20-extract-buildvideofilename-design.md`

**Pre-conditions:**
- Branch `decompile/dy-sync-lib`, working tree clean, HEAD = `602c9f2` (spec commit).
- Baseline test count: `dy.net.Tests` = **53 green**, filtered `SyncDecisionHelperTests` = **36 green**.
- `job/DouyinBasicSyncJob.cs:229-253` contains the original `GetVideoFileName` base body; its two call sites are `ProcessSingleVideo:861` and `ProcessImageSetAndMergeToVideo:1068`.
- `utils/SyncDecisionHelper.cs` is the target helper (135 lines pre-task; already holds `GetNextCursor`/`IsAwemeValid`/`GetVideoTags`/`IsSyncLimitReached`/`BuildVideoEntity`/`PickBestVideoBitRate`). The class closes on **line 134**; insert before that.
- `tests/dy.net.Tests/SyncDecisionHelperTests.cs` is the target test file (single class, organized by `// ---- <Method> ----` section comments; the `PickBestVideoBitRate` section is currently last).
- `job/DouyinFollowedSyncJob.cs:74` contains an `override` that is independent (does not call `base`); must remain untouched.

---

## File Structure

| File | Action | Responsibility |
|------|--------|----------------|
| `utils/SyncDecisionHelper.cs` | Modify (append before line 134 class close) | Add `BuildVideoFileName(VideoTypeEnum, Aweme, DouyinCollectCate)` static method (verbatim move with two param adjustments: drop `cookie`/`config`, lift `VideoType` → `videoType`). |
| `job/DouyinBasicSyncJob.cs` | Modify (lines 229-253) | Replace method body with expression-bodied delegate to helper. Signature/visibility/XML doc unchanged. |
| `tests/dy.net.Tests/SyncDecisionHelperTests.cs` | Modify | Append 7 `[Fact]` tests in a new `// ---- BuildVideoFileName ----` section, after the `PickBestVideoBitRate` section, before final class close. |
| `tests/README.md` | Modify | Extend the `SyncDecisionHelper` row to mention `BuildVideoFileName`; update the `DouyinBasicSyncJob` "NOT covered" bullet. |
| `docs/superpowers/plans/2026-05-20-extract-buildvideofilename.md` | This file | Tracked in source for review. |

---

## Task 1: Extract method + add job-side delegate

**Files:**
- Modify: `utils/SyncDecisionHelper.cs` (append before class close on line 134)
- Modify: `job/DouyinBasicSyncJob.cs:229-253`
- No test changes — relies on existing 53-test suite as the regression safety net.

- [ ] **Step 1.1: Verify baseline is green**

Run:
```bash
DOTNET_ROLL_FORWARD=LatestMajor dotnet test tests/dy.net.Tests/dy.net.Tests.csproj --nologo
```
Expected last line: `已通过! - 失败:     0，通过:    53，已跳过:     0，总计:    53`

If not 53 green, STOP — the working tree has unexpected state; investigate before proceeding.

- [ ] **Step 1.2: Append `BuildVideoFileName` to `SyncDecisionHelper`**

In `utils/SyncDecisionHelper.cs`, locate the closing `}` of `PickBestVideoBitRate` (currently line 133) and the class-closing `}` on line 134. Insert the following block **between them** (one blank line above the new method, no blank line between the new method and the class-closing `}`):

```csharp
        /// <summary>
        /// 从 DouyinBasicSyncJob.GetVideoFileName 抽出的纯文件名构造逻辑。
        /// 行为逐字保留：cate=custom_collect 用 BitRate.Format（或 mp4 兜底）；
        /// videoType=dy_series/dy_mix 且 MixInfo?.Statis?.CurrentEpisode 链非 null
        /// → "S01E{D2}.mp4"；其余 → "{AwemeId}.mp4"。
        /// 原方法 cookie/config 参数在 base body 中未引用，故 helper 签名不带这两项。
        /// 抽象属性 VideoType 提升为 videoType 入参。
        /// 「TryParse 失败」分支在当前 model（MixStatis.CurrentEpisode 为 int）下不可达；
        /// 保留原代码不删，但不为其编写特征化测试。
        /// 由特征化测试 SyncDecisionHelperTests 锁定当前行为。
        /// </summary>
        public static string BuildVideoFileName(VideoTypeEnum videoType, Aweme item, DouyinCollectCate cate)
        {
            if (cate != null && cate.CateType == VideoTypeEnum.dy_custom_collect)
            {
                if (item.Video != null && item.Video.BitRate != null)
                    return $"{item.AwemeId}.{item.Video.BitRate.FirstOrDefault().Format}";
                return $"{item.AwemeId}.mp4";
            }
            else
            {
                if ((videoType == VideoTypeEnum.dy_series || videoType == VideoTypeEnum.dy_mix) && item.MixInfo?.Statis?.CurrentEpisode != null)
                {
                    // 第一步：将 CurrentEpisode 转换为整数（兼容字符串/数字类型）
                    if (int.TryParse(item.MixInfo.Statis.CurrentEpisode.ToString(), out int episodeNum))
                    {
                        // 第二步：格式化数字，确保 1-9 补 0，10+ 保持原样
                        string episodeStr = episodeNum.ToString("D2");
                        return $"S01E{episodeStr}.mp4";
                    }
                    // 容错：如果转换失败，使用原始值（避免程序报错）
                    return $"S01E{item.MixInfo.Statis.CurrentEpisode}.mp4";
                }
                return $"{item.AwemeId}.mp4";
            }
        }
```

Notes on the move:
- The only token-level substitution vs. the original job body at lines 229-253 is `VideoType` → `videoType`. Every other expression, string interpolation, brace, blank line, and Chinese comment ("第一步" / "第二步" / "容错") is preserved verbatim.
- Indentation: method declaration at 8 spaces (same level as the other helper methods), method body at 12 spaces, nested blocks step in by 4 more.
- Two outer signature params from the job (`DouyinCookie cookie`, `AppConfig config`) are intentionally **omitted** because the base body does not reference them. The job-side virtual signature still takes them — the helper does not.
- `using` directives in `utils/SyncDecisionHelper.cs` already cover `dy.net.model.dto` (where `VideoTypeEnum` lives), `dy.net.model.entity` (`DouyinCollectCate`), `dy.net.model.response` (`Aweme`). **Do not add any new `using`.**

- [ ] **Step 1.3: Replace `GetVideoFileName` body in the job with a delegate**

In `job/DouyinBasicSyncJob.cs`, replace lines 229-253 — the entire method body block (NOT the XML doc on lines 221-228):

```csharp
        protected virtual string GetVideoFileName(DouyinCookie cookie, Aweme item, AppConfig config, DouyinCollectCate cate)
        {
            if (cate != null && cate.CateType == VideoTypeEnum.dy_custom_collect)
            {
                if (item.Video != null && item.Video.BitRate != null)
                    return $"{item.AwemeId}.{item.Video.BitRate.FirstOrDefault().Format}";
                return $"{item.AwemeId}.mp4";
            }
            else
            {
                if ((VideoType == VideoTypeEnum.dy_series || VideoType == VideoTypeEnum.dy_mix) && item.MixInfo?.Statis?.CurrentEpisode != null)
                {
                    // 第一步：将 CurrentEpisode 转换为整数（兼容字符串/数字类型）
                    if (int.TryParse(item.MixInfo.Statis.CurrentEpisode.ToString(), out int episodeNum))
                    {
                        // 第二步：格式化数字，确保 1-9 补 0，10+ 保持原样
                        string episodeStr = episodeNum.ToString("D2");
                        return $"S01E{episodeStr}.mp4";
                    }
                    // 容错：如果转换失败，使用原始值（避免程序报错）
                    return $"S01E{item.MixInfo.Statis.CurrentEpisode}.mp4";
                }
                return $"{item.AwemeId}.mp4";
            }
        }
```

with the expression-bodied delegate:

```csharp
        protected virtual string GetVideoFileName(DouyinCookie cookie, Aweme item, AppConfig config, DouyinCollectCate cate)
            => SyncDecisionHelper.BuildVideoFileName(VideoType, item, cate);
```

Constraints:
- The XML doc comment block on lines 221-228 (`/// <summary>` ... `/// <returns>`) stays exactly as-is.
- The virtual signature is **unchanged**: same modifiers (`protected virtual`), same return type, same four parameters in the same order. `cookie` and `config` remain on the signature for virtual contract compatibility, even though `BuildVideoFileName` does not receive them.
- Do **not** touch the two call sites (`ProcessSingleVideo:861`, `ProcessImageSetAndMergeToVideo:1068` — still `var fileName = GetVideoFileName(cookie, item, config, cate);`).
- Do **not** touch `DouyinFollowedSyncJob.GetVideoFileName` (the override at `job/DouyinFollowedSyncJob.cs:74`).
- Do **not** add or remove blank lines around adjacent methods. The XML doc immediately following (currently for `GetAuthorAvatarBasePath` at line 254-`abstract` declaration) must remain separated by the same blank-line pattern as before.

- [ ] **Step 1.4: Build the solution**

Run:
```bash
DOTNET_ROLL_FORWARD=LatestMajor dotnet build dy.net.csproj --nologo
```
Expected: `已成功生成` / `Build succeeded` with **0 errors**. Warning count must not increase versus baseline (691 warnings, per prior slice records).

If the build fails with an unresolved `VideoTypeEnum` / `DouyinCollectCate` / `Aweme` symbol inside the helper, that means a `using` is missing — but **do not add one** without first verifying the file. Check the top of `utils/SyncDecisionHelper.cs`: the existing `using` block should already cover the three needed namespaces. If it does and the build still fails, STOP and report — something else is wrong.

- [ ] **Step 1.5: Run the full test suite**

Run:
```bash
DOTNET_ROLL_FORWARD=LatestMajor dotnet test tests/dy.net.Tests/dy.net.Tests.csproj --nologo
```
Expected: `通过:    53` — exactly the same count as Step 1.1. Zero failures.

The existing characterization tests cover behaviors reachable through other helpers; they will not test `BuildVideoFileName` directly. The fact that they remain green is the regression signal for "extraction did not break compilation or unrelated paths". `BuildVideoFileName`'s own behavior is pinned in Task 2.

- [ ] **Step 1.6: Verify `DouyinFollowedSyncJob` zero changes**

Run:
```bash
git diff -- job/DouyinFollowedSyncJob.cs
```
Expected: **empty output**. The override must be untouched.

```bash
git diff --stat
```
Expected: exactly 2 files modified (`utils/SyncDecisionHelper.cs`, `job/DouyinBasicSyncJob.cs`). No other file may appear.

- [ ] **Step 1.7: Commit**

```bash
git add utils/SyncDecisionHelper.cs job/DouyinBasicSyncJob.cs
git -c user.name='Claude Code' -c user.email='mjgenab@gmail.com' commit -m "$(cat <<'EOF'
refactor(job): extract GetVideoFileName base body to SyncDecisionHelper.BuildVideoFileName

逐字搬到 SyncDecisionHelper.BuildVideoFileName；
job 内保留 protected virtual 薄壳委托，virtual 签名（含
未引用的 cookie/config）不变；DouyinFollowedSyncJob.override
零改动；两个调用点不动。

helper 签名丢掉 base body 未引用的 cookie/config 参数（YAGNI），
VideoType 抽象属性提升为 videoType 入参，与 IsSyncLimitReached /
BuildVideoEntity 同手法。

行为零变更：custom_collect / mix-series episode / 默认分支判定、
int.TryParse + ToString("D2") 数字格式、Chinese 注释逐字保留。
「TryParse 失败」fallback 在当前 model 下不可达——保留代码不删。

现有 53 个测试全绿；BuildVideoFileName 的特征化测试在下一个 commit 加上。

Spec: docs/superpowers/specs/2026-05-20-extract-buildvideofilename-design.md

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

Verify the commit:
```bash
git log -1 --stat
```
Expected: 2 files changed; `utils/SyncDecisionHelper.cs` +~26 lines, `job/DouyinBasicSyncJob.cs` ~-25/+1 lines.

---

## Task 2: Characterization tests for `BuildVideoFileName`

**Files:**
- Modify: `tests/dy.net.Tests/SyncDecisionHelperTests.cs` (append before final class-closing `}` — after the existing `PickBestVideoBitRate` section)

These are golden-master tests — pin the **current** behavior. If a test fails during this task, the bug is in your test setup, not in `BuildVideoFileName` (which is verbatim from the job and was already shipped in Task 1).

- [ ] **Step 2.1: Add a `// ---- BuildVideoFileName ----` section + 7 `[Fact]` tests**

Open `tests/dy.net.Tests/SyncDecisionHelperTests.cs`. Find the **last** `}` (it closes class `SyncDecisionHelperTests`). The line immediately above it is the close of the last `[Fact]` in the `PickBestVideoBitRate` section (`PickBestVideoBitRate_SkipsBitRatesWithNullOrEmptyUrlList`). Insert the following block **before** the class-closing `}` (preserve one blank line between the previous Fact and the new section header):

```csharp
        // ---- BuildVideoFileName ----

        // pin: current behavior, not aspirational

        private static Aweme AwemeWithId(string awemeId)
            => new Aweme { AwemeId = awemeId };

        private static Aweme AwemeWithEpisode(string awemeId, int episode)
            => new Aweme
            {
                AwemeId = awemeId,
                MixInfo = new MixInfo { Statis = new MixStatis { CurrentEpisode = episode } },
            };

        private static Aweme AwemeWithBitRateFormat(string awemeId, string format)
            => new Aweme
            {
                AwemeId = awemeId,
                Video = new Video { BitRate = new List<VideoBitRate> { new VideoBitRate { Format = format } } },
            };

        private static Aweme AwemeWithVideoNull(string awemeId)
            => new Aweme { AwemeId = awemeId, Video = null };

        private static Aweme AwemeWithBitRateNull(string awemeId)
            => new Aweme { AwemeId = awemeId, Video = new Video { BitRate = null } };

        private static DouyinCollectCate Cate(VideoTypeEnum cateType)
            => new DouyinCollectCate { CateType = cateType };

        [Fact]
        public void BuildVideoFileName_CustomCollect_WithBitRate_UsesFormat()
        {
            var name = SyncDecisionHelper.BuildVideoFileName(
                VideoTypeEnum.dy_custom_collect,
                AwemeWithBitRateFormat("123", "webm"),
                Cate(VideoTypeEnum.dy_custom_collect));
            Assert.Equal("123.webm", name);
        }

        [Fact]
        public void BuildVideoFileName_CustomCollect_VideoNull_Mp4Fallback()
        {
            var name = SyncDecisionHelper.BuildVideoFileName(
                VideoTypeEnum.dy_custom_collect,
                AwemeWithVideoNull("123"),
                Cate(VideoTypeEnum.dy_custom_collect));
            Assert.Equal("123.mp4", name);
        }

        [Fact]
        public void BuildVideoFileName_CustomCollect_BitRateNull_Mp4Fallback()
        {
            var name = SyncDecisionHelper.BuildVideoFileName(
                VideoTypeEnum.dy_custom_collect,
                AwemeWithBitRateNull("123"),
                Cate(VideoTypeEnum.dy_custom_collect));
            Assert.Equal("123.mp4", name);
        }

        [Fact]
        public void BuildVideoFileName_Series_NumericEpisode_S01E_D2_Padded()
        {
            var name = SyncDecisionHelper.BuildVideoFileName(
                VideoTypeEnum.dy_series,
                AwemeWithEpisode("123", 5),
                cate: null);
            Assert.Equal("S01E05.mp4", name);
        }

        [Fact]
        public void BuildVideoFileName_Mix_NumericEpisode_S01E_D2_NotPadded()
        {
            var name = SyncDecisionHelper.BuildVideoFileName(
                VideoTypeEnum.dy_mix,
                AwemeWithEpisode("123", 12),
                cate: null);
            Assert.Equal("S01E12.mp4", name);
        }

        [Fact]
        public void BuildVideoFileName_DefaultBranch_AwemeIdMp4_WhenNotCustomCollectAndNotEpisodic()
        {
            var name = SyncDecisionHelper.BuildVideoFileName(
                VideoTypeEnum.dy_follows,
                AwemeWithId("123"),
                cate: null);
            Assert.Equal("123.mp4", name);
        }

        [Fact]
        public void BuildVideoFileName_CateNonCustomCollect_FollowsDefaultBranch()
        {
            var name = SyncDecisionHelper.BuildVideoFileName(
                VideoTypeEnum.dy_collects,
                AwemeWithId("123"),
                Cate(VideoTypeEnum.dy_collects));
            Assert.Equal("123.mp4", name);
        }
```

The six `private static` helpers (`AwemeWithId`, `AwemeWithEpisode`, `AwemeWithBitRateFormat`, `AwemeWithVideoNull`, `AwemeWithBitRateNull`, `Cate`) live inside the test class scoped to this section's intent. They are intentionally **not** merged with the `StdAweme`/`Ck`/`StdBitRate` helpers from the `BuildVideoEntity` section nor the `Br`/`BrNoPlayAddr`/`AwemeWith` helpers from the `PickBestVideoBitRate` section — each section keeps its own minimal Aweme builders to remain self-contained.

Notes:
- For #4 and #5, `cate: null` uses C# named-argument syntax to make it clear at the call site that the third positional arg is intentionally null.
- For #1/#2/#3, the helper's `videoType` argument is `dy_custom_collect`; the result depends on `cate.CateType == dy_custom_collect`, but the path is also independent of `videoType` — any `videoType` value would yield the same result in these cases. Using `dy_custom_collect` makes the test name and intent consistent.

- [ ] **Step 2.2: Run filtered tests**

Run:
```bash
DOTNET_ROLL_FORWARD=LatestMajor dotnet test tests/dy.net.Tests/dy.net.Tests.csproj --nologo --filter "FullyQualifiedName~SyncDecisionHelperTests"
```
Expected: `通过:    43` (36 prior + 7 new).

If any new test fails: STOP. Either a test setup field has a model-shape mismatch (e.g., `MixStatis` is in a different namespace than expected — check `model/response/DouyinVideoInfoResponse.cs:704`), or `BuildVideoFileName` is not byte-for-byte identical with the original. Read the failure message; do **not** weaken the assertion.

- [ ] **Step 2.3: Run the full suite**

Run:
```bash
DOTNET_ROLL_FORWARD=LatestMajor dotnet test tests/dy.net.Tests/dy.net.Tests.csproj --nologo
```
Expected: `通过:    60` (53 prior + 7 new), 0 failed, 0 skipped.

- [ ] **Step 2.4: Commit**

```bash
git add tests/dy.net.Tests/SyncDecisionHelperTests.cs
git -c user.name='Claude Code' -c user.email='mjgenab@gmail.com' commit -m "$(cat <<'EOF'
test: golden-master tests pinning SyncDecisionHelper.BuildVideoFileName

新增 7 个 [Fact] 覆盖 custom_collect 的 Format / mp4 兜底 /
mix-series 数字 episode 的 S01E{D2} 格式 / 默认分支 AwemeId.mp4 /
cate 非 custom_collect 走默认。

「TryParse 失败」fallback 不写测试——MixStatis.CurrentEpisode
为 int，ToString() 永远可被 TryParse 解析，不可达分支。

dy.net.Tests: 53 → 60 全绿。
SyncDecisionHelperTests filter: 36 → 43 全绿。

Spec: docs/superpowers/specs/2026-05-20-extract-buildvideofilename-design.md

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

Verify:
```bash
git log -1 --stat
```
Expected: 1 file changed; `tests/dy.net.Tests/SyncDecisionHelperTests.cs` +~110 lines.

---

## Task 3: Update `tests/README.md` + push

**Files:**
- Modify: `tests/README.md` — both the `SyncDecisionHelper` row in the `## What is pinned` table and the `DouyinBasicSyncJob` bullet under `## What is intentionally NOT covered`.

- [ ] **Step 3.1: Extend the `SyncDecisionHelper` row**

In `tests/README.md`, find the current `SyncDecisionHelper` row (it should already mention `GetNextCursor`, `IsAwemeValid`, `GetVideoTags`, `IsSyncLimitReached`, `BuildVideoEntity`, `PickBestVideoBitRate` — added in prior slices). The line is one long single-line markdown table row.

Append `, \`BuildVideoFileName\` (custom_collect Format vs mp4 兜底 / mix-series 数字 episode 的 S01E{D2} / 默认 AwemeId.mp4 / cate 非 custom_collect 走默认)` to the end of the `Locks` column, **immediately before the closing pipe**. The full updated line must read:

```markdown
| `SyncDecisionHelper` | `SyncDecisionHelperTests` | `GetNextCursor` (Cursor→MaxCursor→`"0"`, null-safe), `IsAwemeValid` (3-level null guard), `GetVideoTags` (per-level pick, missing→null), `IsSyncLimitReached` (cate 30-cap vs `BatchCount` cap, `OnlySyncNew` passthrough, `dy_follows` `!FullSync`, mix/series short-circuit), `BuildVideoEntity` (纯字段映射 / cate 标题覆盖 / `OnlyImgOrOnlyMp3` / `DyUserId` 分支 / `AuthorAvatarUrl` 回落 / `FileSize` 零回落), `PickBestVideoBitRate` (encoder=265 优先 H.265 + 回退 H.264 / 默认或 ≠265 仅 H.264 / 空或 null `UrlList` 跳过), `BuildVideoFileName` (custom_collect Format vs mp4 兜底 / mix-series 数字 episode 的 S01E{D2} / 默认 AwemeId.mp4 / cate 非 custom_collect 走默认) |
```

- [ ] **Step 3.2: Update the `DouyinBasicSyncJob` NOT-covered bullet**

In `tests/README.md`, find the current `DouyinBasicSyncJob orchestration` bullet (a multi-line markdown list item under `## What is intentionally NOT covered`). The current text reads:

```markdown
- **`DouyinBasicSyncJob` orchestration** — HTTP + filesystem + DB coupled with
  no seams. Pure decision logic extracted so far: `GetNextCursor`,
  `IsAwemeValid`, `GetVideoTags`, `IsSyncLimitReached`, `BuildVideoEntity` (除
  `DynamicVideos`/`NfoFileGenerator` 副作用块外), `PickBestVideoBitRate` —
  all pinned (see table above). Still uncovered: `CreateSaveFolder`,
  `GetVideoFileName`, `ProcessSingleVideo`/`ProcessDynamicVideo`/
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
  下不可达，保留代码不写测试) — all pinned (see table above). Still
  uncovered: `CreateSaveFolder`, `ProcessSingleVideo`/`ProcessDynamicVideo`/
  `ProcessImageSetAndMergeToVideo` orchestration bodies, `AutoDistinct`,
  `SaveVideos`, `DownVideoCover`, `DownAuthorAvatar`, `CleanupFailedVideos`,
  `HandleSyncCompletion` — all retain HTTP / FS / DB coupling and will be
  characterized as further seams are extracted in follow-up plans.
```

Diff intent:
1. Add `, \`BuildVideoFileName\` (基类体；DouyinFollowedSyncJob.GetVideoFileName override 仍是子类业务实现，未覆盖；「TryParse 失败」fallback 在当前 model 下不可达，保留代码不写测试)` to the "extracted so far" list.
2. **Remove `GetVideoFileName,`** from the "Still uncovered" list (because the base body is now pinned). Keep `CreateSaveFolder,` and the rest of the line unchanged.

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
Expected: `通过:    60` (unchanged from Task 2; docs edit cannot affect tests).

- [ ] **Step 3.5: Commit**

```bash
git add tests/README.md docs/superpowers/plans/2026-05-20-extract-buildvideofilename.md
git -c user.name='Claude Code' -c user.email='mjgenab@gmail.com' commit -m "$(cat <<'EOF'
docs: pin BuildVideoFileName coverage in tests/README

把 BuildVideoFileName 加入 SyncDecisionHelper 表格行；
DouyinBasicSyncJob 的 NOT-covered 清单移除 GetVideoFileName
（基类体已 pinned），并标注 DouyinFollowedSyncJob override 仍
未覆盖、「TryParse 失败」fallback 在当前 model 下不可达。

落地本刀实现计划：
docs/superpowers/plans/2026-05-20-extract-buildvideofilename.md

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

Verify:
```bash
git log --oneline -4
```
Expected (top → bottom):
1. `docs: pin BuildVideoFileName coverage in tests/README`
2. `test: golden-master tests pinning SyncDecisionHelper.BuildVideoFileName`
3. `refactor(job): extract GetVideoFileName base body to SyncDecisionHelper.BuildVideoFileName`
4. `docs: spec for extracting GetVideoFileName base body`

- [ ] **Step 3.6: Push to origin (no merge, no PR)**

```bash
git push origin decompile/dy-sync-lib
```
Expected: 4 new commits land on `origin/decompile/dy-sync-lib`. Confirm with:

```bash
git status -sb
```
Expected: `## decompile/dy-sync-lib...origin/decompile/dy-sync-lib` (no `[ahead N]`).

---

## Done criteria

- `git log --oneline -4` shows the four commits in the order above.
- `dotnet test` reports **60 passed / 0 failed**.
- `git status -sb` shows clean working tree and synced-with-origin.
- `job/DouyinBasicSyncJob.cs` body of `GetVideoFileName` is a single expression-bodied `=>` delegating to `SyncDecisionHelper.BuildVideoFileName(VideoType, item, cate)`. XML doc on lines 221-228 unchanged.
- `utils/SyncDecisionHelper.cs` contains `BuildVideoFileName` as its last method (after `PickBestVideoBitRate`).
- `tests/dy.net.Tests/SyncDecisionHelperTests.cs` contains the `// ---- BuildVideoFileName ----` section with 7 `[Fact]` tests.
- `tests/README.md` row for `SyncDecisionHelper` mentions `BuildVideoFileName`; the `DouyinBasicSyncJob` "NOT covered" bullet no longer lists `GetVideoFileName`.
- `git diff 602c9f2..HEAD -- job/DouyinFollowedSyncJob.cs` is empty (override untouched).
- `git diff 602c9f2..HEAD -- 'job/Douyin*SyncJob.cs' 'job/DouYin*SyncJob.cs'` shows changes **only** in `DouyinBasicSyncJob.cs`; all 7 sibling subclass files remain untouched.
- No merge, no PR.
