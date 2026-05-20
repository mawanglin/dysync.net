# Extract `GetBestMatchedVideoUrl` Pure Logic Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Move `DouyinBasicSyncJob.GetBestMatchedVideoUrl` verbatim into `SyncDecisionHelper.PickBestVideoBitRate`, leaving a thin static delegate in the job, and pin the behavior with golden-master characterization tests.

**Architecture:** Same shape as the prior two slices (`IsAwemeValid` family, `BuildVideoEntity`). Copy the method body unchanged into the existing `SyncDecisionHelper` static class; the job retains a `private static` expression-bodied delegate. No callsite changes, no subclass changes, no new abstractions.

**Tech Stack:** .NET 8 (build via `DOTNET_ROLL_FORWARD=LatestMajor` on local SDK 10), xUnit, existing `dy.net.Tests` project, Serilog (already referenced by helper).

**Spec:** `docs/superpowers/specs/2026-05-20-extract-pick-best-video-bitrate-design.md`

**Pre-conditions:**
- Branch `decompile/dy-sync-lib`, working tree clean.
- Baseline test count: `dy.net.Tests` = **46 green** (filtered `SyncDecisionHelperTests` = 29).
- `job/DouyinBasicSyncJob.cs:902-923` contains the original `GetBestMatchedVideoUrl`; its only callsite is `ProcessSingleVideo` at `job/DouyinBasicSyncJob.cs:844`.
- `utils/SyncDecisionHelper.cs` is the target helper (107 lines pre-task; already holds `GetNextCursor`/`IsAwemeValid`/`GetVideoTags`/`IsSyncLimitReached`/`BuildVideoEntity`).
- `tests/dy.net.Tests/SyncDecisionHelperTests.cs` is the target test file (single class, organized by `// ---- <Method> ----` section comments).

---

## File Structure

| File | Action | Responsibility |
|------|--------|----------------|
| `utils/SyncDecisionHelper.cs` | Modify | Add `PickBestVideoBitRate(Aweme, AppConfig)` static method (verbatim move). |
| `job/DouyinBasicSyncJob.cs` | Modify (lines 902-923) | Replace method body with one-line expression-bodied delegate to helper. |
| `tests/dy.net.Tests/SyncDecisionHelperTests.cs` | Modify | Append 7 `[Fact]` tests in a new `// ---- PickBestVideoBitRate ----` section. |
| `tests/README.md` | Modify | Extend the `SyncDecisionHelper` row to mention `BuildVideoEntity` (catch-up from prior slice) and `PickBestVideoBitRate` (this slice). |
| `docs/superpowers/plans/2026-05-20-extract-pick-best-video-bitrate.md` | Already exists (this file) | Tracked in source for review. |

---

## Task 1: Extract method + add job-side delegate

**Files:**
- Modify: `utils/SyncDecisionHelper.cs` (append before closing `}` of class)
- Modify: `job/DouyinBasicSyncJob.cs:902-923`
- No test changes — relies on existing 46-test suite as the regression safety net.

- [ ] **Step 1.1: Verify baseline is green**

Run:
```bash
DOTNET_ROLL_FORWARD=LatestMajor dotnet test tests/dy.net.Tests/dy.net.Tests.csproj --nologo
```
Expected last line: `已通过! - 失败:     0，通过:    46，已跳过:     0，总计:    46`

If not 46 green, STOP — the working tree has unexpected state; investigate before proceeding.

- [ ] **Step 1.2: Append `PickBestVideoBitRate` to `SyncDecisionHelper`**

In `utils/SyncDecisionHelper.cs`, insert the following method as the **last** method inside the `public static class SyncDecisionHelper` body (after `BuildVideoEntity`, before the class closing `}` on the line previously containing `}` for the class). Preserve a single blank line between methods.

```csharp
        /// <summary>
        /// 从 DouyinBasicSyncJob.GetBestMatchedVideoUrl 抽出的纯码流选择逻辑。
        /// 行为逐字保留：encoder=265 时优先 H.265，无则回退 H.264；
        /// 否则只挑 H.264。二者均按 BitRateValue 降序取首；
        /// 只考虑 PlayAddr.UrlList 非空（非 null 且 Any()）的码流。
        /// 由特征化测试 SyncDecisionHelperTests 锁定当前行为。
        /// </summary>
        public static VideoBitRate PickBestVideoBitRate(Aweme item, AppConfig config)
        {
            VideoBitRate v;
            if (config.VideoEncoder.HasValue && config.VideoEncoder.Value == 265)
            {
                v = item.Video.BitRate.Where(v => v.IsH265 == 1 && v.PlayAddr?.UrlList != null && v.PlayAddr.UrlList.Any())
                                .OrderByDescending(v => v.BitRateValue)
                                .FirstOrDefault();
                v ??= item.Video.BitRate.Where(v => v.IsH265 == 0 && v.PlayAddr?.UrlList != null && v.PlayAddr.UrlList.Any())
                                .OrderByDescending(v => v.BitRateValue)
                                .FirstOrDefault();
            }
            else
            {
                v = item.Video.BitRate.Where(v => v.IsH265 == 0 && v.PlayAddr?.UrlList != null && v.PlayAddr.UrlList.Any())
                                  .OrderByDescending(v => v.BitRateValue)
                                  .FirstOrDefault();
            }
            return v;
        }
```

Note: the **inner indentation of the second `OrderByDescending` line uses an extra space** (`                                  ` 34 spaces vs 32 spaces above) — this matches the existing job-side formatting at line 918. Preserve it verbatim.

- [ ] **Step 1.3: Replace `GetBestMatchedVideoUrl` body in the job with a delegate**

In `job/DouyinBasicSyncJob.cs`, replace lines 902-923 — the entire method:

```csharp
        private static VideoBitRate GetBestMatchedVideoUrl(Aweme item, AppConfig config)
        {
            VideoBitRate v;
            if (config.VideoEncoder.HasValue && config.VideoEncoder.Value == 265)
            {
                v = item.Video.BitRate.Where(v => v.IsH265 == 1 && v.PlayAddr?.UrlList != null && v.PlayAddr.UrlList.Any())
                                .OrderByDescending(v => v.BitRateValue)
                                .FirstOrDefault();
                v ??= item.Video.BitRate.Where(v => v.IsH265 == 0 && v.PlayAddr?.UrlList != null && v.PlayAddr.UrlList.Any())
                                .OrderByDescending(v => v.BitRateValue)
                                .FirstOrDefault();
            }

            else
            {
                v = item.Video.BitRate.Where(v => v.IsH265 == 0 && v.PlayAddr?.UrlList != null && v.PlayAddr.UrlList.Any())
                                  .OrderByDescending(v => v.BitRateValue)
                                  .FirstOrDefault();
            }

            return v;
        }
```

with the expression-bodied delegate (matches the `IsAwemeValid` delegate style at line 1288):

```csharp
        private static VideoBitRate GetBestMatchedVideoUrl(Aweme item, AppConfig config)
            => SyncDecisionHelper.PickBestVideoBitRate(item, config);
```

Do **not** touch the callsite at `ProcessSingleVideo` (line 844 — still reads `var v = GetBestMatchedVideoUrl(item, config);`).

Do **not** add or remove blank lines around the surrounding methods. The neighboring method below (currently `protected async Task<DouyinVideo> ProcessDynamicVideo` at line 935) should remain separated by exactly the same blank lines as before.

- [ ] **Step 1.4: Build the solution**

Run:
```bash
DOTNET_ROLL_FORWARD=LatestMajor dotnet build dy.net.csproj --nologo
```
Expected: `已成功生成` / `Build succeeded` with **0 errors**. Warnings, if any, are pre-existing — verify the warning count did not increase.

If build fails with unresolved `Aweme` / `AppConfig` / `VideoBitRate` symbols in the helper, confirm the `using` block at the top of `utils/SyncDecisionHelper.cs` already includes `dy.net.model.dto`, `dy.net.model.entity`, `dy.net.model.response`, `Serilog` (it does at the current HEAD). No new `using` is required because `VideoBitRate` lives in `dy.net.model.response` (file `model/response/DouyinVideoInfoResponse.cs:1192`).

- [ ] **Step 1.5: Run the full test suite**

Run:
```bash
DOTNET_ROLL_FORWARD=LatestMajor dotnet test tests/dy.net.Tests/dy.net.Tests.csproj --nologo
```
Expected: `通过:    46` — exactly the same count as Step 1.1. Zero failures. The existing characterization tests cover behaviors reachable through `BuildVideoEntity` / `IsAwemeValid` / etc., so they will not test `PickBestVideoBitRate` directly. The fact that they remain green is the regression signal for "extraction did not break compilation or unrelated paths". `PickBestVideoBitRate`'s own behavior is pinned in Task 2.

- [ ] **Step 1.6: Commit**

```bash
git add utils/SyncDecisionHelper.cs job/DouyinBasicSyncJob.cs
git -c user.name='Claude Code' -c user.email='mjgenab@gmail.com' commit -m "$(cat <<'EOF'
refactor(job): extract GetBestMatchedVideoUrl pure logic to SyncDecisionHelper.PickBestVideoBitRate

逐字搬到 SyncDecisionHelper.PickBestVideoBitRate；
job 内保留 private static 薄壳委托（对齐 IsAwemeValid 风格），
唯一调用点 ProcessSingleVideo 不动，7 个子类零改动。

行为零变更：BitRate 列表过滤条件、OrderByDescending、FirstOrDefault、
v ??= 回退、encoder=265 / 否则的分支判定逐字保留。
现有 46 个测试全绿；PickBestVideoBitRate 的特征化测试在下一个 commit 加上。

Spec: docs/superpowers/specs/2026-05-20-extract-pick-best-video-bitrate-design.md

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

Verify the commit with:
```bash
git log -1 --stat
```
Expected: 2 files changed; `utils/SyncDecisionHelper.cs` +~26 lines, `job/DouyinBasicSyncJob.cs` ~-22/+1 lines.

---

## Task 2: Characterization tests for `PickBestVideoBitRate`

**Files:**
- Modify: `tests/dy.net.Tests/SyncDecisionHelperTests.cs` (append before final closing `}` of the class)

These are golden-master tests — pin the **current** behavior. If a test fails during this task, the bug is in your test setup, not in `PickBestVideoBitRate` (which is verbatim from the job and was already shipped).

- [ ] **Step 2.1: Add a `// ---- PickBestVideoBitRate ----` section + 7 `[Fact]` tests**

Open `tests/dy.net.Tests/SyncDecisionHelperTests.cs`. Locate the **last** `}` (line ~351 — closes class `SyncDecisionHelperTests`). Immediately **before** that closing `}`, insert:

```csharp
        // ---- PickBestVideoBitRate ----

        // pin: current behavior, not aspirational

        private static VideoBitRate Br(int bitRateValue, int isH265, List<string> urlList)
            => new VideoBitRate
            {
                BitRateValue = bitRateValue,
                IsH265 = isH265,
                PlayAddr = new PlayAddr { UrlList = urlList },
            };

        private static VideoBitRate BrNoPlayAddr(int bitRateValue, int isH265)
            => new VideoBitRate { BitRateValue = bitRateValue, IsH265 = isH265, PlayAddr = null };

        private static Aweme AwemeWith(params VideoBitRate[] bitRates)
            => new Aweme { Video = new Video { BitRate = bitRates.ToList() } };

        [Fact]
        public void PickBestVideoBitRate_H265Preferred_PicksHighestH265()
        {
            var h265Lo = Br(1000, 1, new List<string> { "a" });
            var h265Hi = Br(5000, 1, new List<string> { "a" });
            var h264Hi = Br(9000, 0, new List<string> { "a" });
            var item = AwemeWith(h265Lo, h265Hi, h264Hi);
            var picked = SyncDecisionHelper.PickBestVideoBitRate(
                item, new AppConfig { VideoEncoder = 265 });
            Assert.Same(h265Hi, picked);
        }

        [Fact]
        public void PickBestVideoBitRate_H265Preferred_FallsBackToH264_WhenNoPlayableH265()
        {
            var h265Empty = Br(8000, 1, new List<string>());
            var h265Null = BrNoPlayAddr(7000, 1);
            var h264Lo = Br(2000, 0, new List<string> { "a" });
            var h264Hi = Br(6000, 0, new List<string> { "a" });
            var item = AwemeWith(h265Empty, h265Null, h264Lo, h264Hi);
            var picked = SyncDecisionHelper.PickBestVideoBitRate(
                item, new AppConfig { VideoEncoder = 265 });
            Assert.Same(h264Hi, picked);
        }

        [Fact]
        public void PickBestVideoBitRate_H265Preferred_ReturnsNull_WhenNothingPlayable()
        {
            var h265Empty = Br(5000, 1, new List<string>());
            var h264NullUrl = Br(4000, 0, null);
            var h264NoPlayAddr = BrNoPlayAddr(3000, 0);
            var item = AwemeWith(h265Empty, h264NullUrl, h264NoPlayAddr);
            var picked = SyncDecisionHelper.PickBestVideoBitRate(
                item, new AppConfig { VideoEncoder = 265 });
            Assert.Null(picked);
        }

        [Fact]
        public void PickBestVideoBitRate_DefaultEncoder_PicksHighestH264()
        {
            var h265Hi = Br(9000, 1, new List<string> { "a" });
            var h264Lo = Br(2000, 0, new List<string> { "a" });
            var h264Hi = Br(6000, 0, new List<string> { "a" });
            var item = AwemeWith(h265Hi, h264Lo, h264Hi);
            var picked = SyncDecisionHelper.PickBestVideoBitRate(
                item, new AppConfig { VideoEncoder = null });
            Assert.Same(h264Hi, picked);
        }

        [Fact]
        public void PickBestVideoBitRate_DefaultEncoder_NeverReturnsH265()
        {
            var h265Hi = Br(9000, 1, new List<string> { "a" });
            var h264Hi = Br(6000, 0, new List<string> { "a" });
            var item = AwemeWith(h265Hi, h264Hi);
            var picked = SyncDecisionHelper.PickBestVideoBitRate(
                item, new AppConfig { VideoEncoder = null });
            Assert.NotNull(picked);
            Assert.Equal(0, picked.IsH265);
        }

        [Fact]
        public void PickBestVideoBitRate_EncoderNot265_PicksH264Only()
        {
            // VideoEncoder.HasValue=true 但 != 265 → 走 else 分支（同默认）
            var h265Hi = Br(9000, 1, new List<string> { "a" });
            var h264Hi = Br(6000, 0, new List<string> { "a" });
            var item = AwemeWith(h265Hi, h264Hi);
            var picked = SyncDecisionHelper.PickBestVideoBitRate(
                item, new AppConfig { VideoEncoder = 264 });
            Assert.Same(h264Hi, picked);
        }

        [Fact]
        public void PickBestVideoBitRate_SkipsBitRatesWithNullOrEmptyUrlList()
        {
            // 故意让"不可播放"的码率更高，验证它们被过滤而不是入选。
            var h264Empty = Br(9999, 0, new List<string>());
            var h264NullList = Br(8888, 0, null);
            var h264NoPlayAddr = BrNoPlayAddr(7777, 0);
            var h264Playable = Br(100, 0, new List<string> { "x" });
            var item = AwemeWith(h264Empty, h264NullList, h264NoPlayAddr, h264Playable);
            var picked = SyncDecisionHelper.PickBestVideoBitRate(
                item, new AppConfig { VideoEncoder = null });
            Assert.Same(h264Playable, picked);
        }
```

The three `private static` helpers (`Br` / `BrNoPlayAddr` / `AwemeWith`) live inside the test class to keep the existing `BuildVideoEntity` section's `StdBitRate`/`StdAweme` helpers (`tests/dy.net.Tests/SyncDecisionHelperTests.cs:~250-310`) untouched and avoid coupling between sections.

- [ ] **Step 2.2: Run filtered tests**

Run:
```bash
DOTNET_ROLL_FORWARD=LatestMajor dotnet test tests/dy.net.Tests/dy.net.Tests.csproj --nologo --filter "FullyQualifiedName~SyncDecisionHelperTests"
```
Expected: `通过:    36` (29 prior + 7 new).

If any of the 7 new tests fails: STOP. Either the test setup is wrong (the helpers `Br`/`BrNoPlayAddr`/`AwemeWith` may have shape mismatches with current model), or — unlikely but worth confirming — `PickBestVideoBitRate` is not byte-for-byte identical with the original. Read the failure message; do **not** weaken the assertion.

- [ ] **Step 2.3: Run the full suite**

Run:
```bash
DOTNET_ROLL_FORWARD=LatestMajor dotnet test tests/dy.net.Tests/dy.net.Tests.csproj --nologo
```
Expected: `通过:    53` (46 prior + 7 new), 0 failed, 0 skipped.

- [ ] **Step 2.4: Commit**

```bash
git add tests/dy.net.Tests/SyncDecisionHelperTests.cs
git -c user.name='Claude Code' -c user.email='mjgenab@gmail.com' commit -m "$(cat <<'EOF'
test: golden-master tests pinning SyncDecisionHelper.PickBestVideoBitRate

新增 7 个 [Fact] 覆盖 H.265 优先 + H.264 回退 / encoder=null 默认 /
encoder≠265 / null 或 empty UrlList 跳过等行为。

测试用 Assert.Same 锁定具体 VideoBitRate 实例，确保"挑哪一条"
的语义不变；不依赖 BitRateValue 文字值。

dy.net.Tests: 46 → 53 全绿。
SyncDecisionHelperTests filter: 29 → 36 全绿。

Spec: docs/superpowers/specs/2026-05-20-extract-pick-best-video-bitrate-design.md

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

Verify:
```bash
git log -1 --stat
```
Expected: 1 file changed; `tests/dy.net.Tests/SyncDecisionHelperTests.cs` +~95 lines.

---

## Task 3: Update `tests/README.md` (pin doc + catch-up)

**Files:**
- Modify: `tests/README.md` (line 24 — `SyncDecisionHelper` row in the `## What is pinned` table; lines 40-44 — `DouyinBasicSyncJob orchestration` bullet under `## What is intentionally NOT covered`)

Why two updates: the previous slice (BuildVideoEntity) shipped its tests but did **not** update the README table — so the row currently omits both `BuildVideoEntity` and `PickBestVideoBitRate`. Bundle the catch-up here.

- [ ] **Step 3.1: Extend the `SyncDecisionHelper` row**

In `tests/README.md`, find this line (currently line 24):

```markdown
| `SyncDecisionHelper` | `SyncDecisionHelperTests` | `GetNextCursor` (Cursor→MaxCursor→`"0"`, null-safe), `IsAwemeValid` (3-level null guard), `GetVideoTags` (per-level pick, missing→null), `IsSyncLimitReached` (cate 30-cap vs `BatchCount` cap, `OnlySyncNew` passthrough, `dy_follows` `!FullSync`, mix/series short-circuit) |
```

Replace it with:

```markdown
| `SyncDecisionHelper` | `SyncDecisionHelperTests` | `GetNextCursor` (Cursor→MaxCursor→`"0"`, null-safe), `IsAwemeValid` (3-level null guard), `GetVideoTags` (per-level pick, missing→null), `IsSyncLimitReached` (cate 30-cap vs `BatchCount` cap, `OnlySyncNew` passthrough, `dy_follows` `!FullSync`, mix/series short-circuit), `BuildVideoEntity` (纯字段映射 / cate 标题覆盖 / `OnlyImgOrOnlyMp3` / `DyUserId` 分支 / `AuthorAvatarUrl` 回落 / `FileSize` 零回落), `PickBestVideoBitRate` (encoder=265 优先 H.265 + 回退 H.264 / 默认或 ≠265 仅 H.264 / 空或 null `UrlList` 跳过) |
```

- [ ] **Step 3.2: Update the "NOT covered" bullet**

In `tests/README.md`, find the current `DouyinBasicSyncJob orchestration` bullet (lines 40-44):

```markdown
- **`DouyinBasicSyncJob` orchestration** — HTTP + filesystem + DB coupled with
  no seams. Its side-effect-free decision logic has been extracted to
  `SyncDecisionHelper` and is now pinned (see table above); the remaining
  orchestration/HTTP/FS/DB body stays uncovered until further seams are
  extracted in follow-up plans.
```

Replace it with:

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

- [ ] **Step 3.3: Verify the file**

Run:
```bash
git diff tests/README.md
```
Expected: a clean diff touching only line 24 (the table row) and lines 40-44 (the bullet). No accidental whitespace edits elsewhere.

Sanity-check with the linter-equivalent: there should be no trailing whitespace on the changed lines (the diff hunk's `+` lines should mirror the format of the existing surrounding lines, none of which have trailing spaces).

- [ ] **Step 3.4: Build + test as a smoke pass**

Docs change cannot break code, but run as belt-and-braces in case any tool watches docs:

```bash
DOTNET_ROLL_FORWARD=LatestMajor dotnet test tests/dy.net.Tests/dy.net.Tests.csproj --nologo
```
Expected: `通过:    53` unchanged.

- [ ] **Step 3.5: Commit**

```bash
git add tests/README.md docs/superpowers/plans/2026-05-20-extract-pick-best-video-bitrate.md
git -c user.name='Claude Code' -c user.email='mjgenab@gmail.com' commit -m "$(cat <<'EOF'
docs: pin PickBestVideoBitRate + BuildVideoEntity coverage in tests/README

补全前一刀 BuildVideoEntity 漏更新的 README 表格行，
并加入本刀 PickBestVideoBitRate 的覆盖项；同时扩写
DouyinBasicSyncJob 的"未覆盖"清单，列出剩余的纯/混合方法。

落地本刀实现计划：
docs/superpowers/plans/2026-05-20-extract-pick-best-video-bitrate.md

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

Verify:
```bash
git log --oneline -4
```
Expected (top → bottom):
1. `docs: pin PickBestVideoBitRate + BuildVideoEntity coverage in tests/README`
2. `test: golden-master tests pinning SyncDecisionHelper.PickBestVideoBitRate`
3. `refactor(job): extract GetBestMatchedVideoUrl pure logic to SyncDecisionHelper.PickBestVideoBitRate`
4. `docs: spec for extracting GetBestMatchedVideoUrl pure logic`

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
- `dotnet test` reports **53 passed / 0 failed**.
- `git status -sb` shows clean working tree and synced-with-origin.
- `job/DouyinBasicSyncJob.cs` body of `GetBestMatchedVideoUrl` is a single expression-bodied `=>` delegating to `SyncDecisionHelper.PickBestVideoBitRate`.
- `utils/SyncDecisionHelper.cs` contains `PickBestVideoBitRate` as its last method.
- `tests/dy.net.Tests/SyncDecisionHelperTests.cs` contains the `// ---- PickBestVideoBitRate ----` section with 7 `[Fact]` tests.
- `tests/README.md` row for `SyncDecisionHelper` mentions both `BuildVideoEntity` and `PickBestVideoBitRate`.
- No subclass files in `job/` were modified (`git log --stat -1 HEAD~3..HEAD -- job/Douyin*SyncJob.cs job/DouYin*SyncJob.cs` should show only `DouyinBasicSyncJob.cs`).
- No merge, no PR.
