# Extract Merge-Video Cover-URL Pick Logic — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Extract the 3-line cover-URL ternary from `DouyinBasicSyncJob.ProcessImageSetAndMergeToVideo` (lines 985-987) into a pure, independently-testable `SyncDecisionHelper.PickMergeVideoCoverUrl(DouyinCollectCate cate, Aweme item, List<DouyinMergeVideoDto> imageUrls) → string`, leaving all surrounding I/O (avatar / virtualBitRate / DownVideoCover / CreateVideoEntity) in the job.

**Architecture:** Behavior-preserving "thin shell" extraction (12th god-class slice). Direct sibling of slice 6's `PickCoverUrl` — completes the cover-decision family. Cate branch falls back `MixInfo → imageUrls → Music`; non-cate branch uses only `imageUrls`. The job keeps a one-line call. Golden-master characterization tests pin the helper. No new file, no new enum.

**Tech Stack:** .NET 8 (`net8.0`; local SDK 10 → all `dotnet` commands prefixed `DOTNET_ROLL_FORWARD=LatestMajor`), xUnit (`tests/dy.net.Tests`), C#.

**Spec:** `docs/superpowers/specs/2026-05-24-extract-pickmergevideocoverurl-design.md`

---

## File Structure

- **Modify:** `utils/SyncDecisionHelper.cs` — append one pure method `PickMergeVideoCoverUrl`; existing 15 methods untouched.
- **Modify:** `job/DouyinBasicSyncJob.cs:985-987` (the 3-line ternary inside `ProcessImageSetAndMergeToVideo`'s `try` block): replace with a one-line helper call; the rest of the method verbatim.
- **Modify:** `tests/dy.net.Tests/SyncDecisionHelperTests.cs` — append one `// ---- PickMergeVideoCoverUrl ----` section (9 `[Fact]` + 1 new section-local helper `MergeDto`, reusing existing `CoverImg`/`Cate` from prior sections).
- **Modify:** `tests/README.md` — record the new pinned coverage.

`ProcessImageSetAndMergeToVideo` is `protected async Task<DouyinVideo>` (non-`virtual`, no overrides) with one call site (`:623` from `ProcessVideoList`, same file) → job-side change is confined to `DouyinBasicSyncJob.cs`.

---

## Task 1: Extract `PickMergeVideoCoverUrl` + thin `ProcessImageSetAndMergeToVideo`

**Files:**
- Modify: `utils/SyncDecisionHelper.cs` (append before the class-closing `}` — after the existing last method `CollectAlternateVideoUrls`)
- Modify: `job/DouyinBasicSyncJob.cs:985-987` (the cover-URL ternary inside `ProcessImageSetAndMergeToVideo`'s `try` block)

- [ ] **Step 1: Append `PickMergeVideoCoverUrl` to `SyncDecisionHelper`**

In `utils/SyncDecisionHelper.cs`, insert this method immediately after `CollectAlternateVideoUrls`'s closing `}` and before the class-closing `}`. The file currently ends with `CollectAlternateVideoUrls`'s body (`            return otherUrls;\n        }`), then `    }` (class close), then `}` (namespace close). Insert between the method close and the class close:

```csharp

        /// <summary>
        /// 从 DouyinBasicSyncJob.ProcessImageSetAndMergeToVideo 抽出的纯合成视频封面 URL 选取（无 I/O）。
        /// 与第六刀 PickCoverUrl 互为兄弟：单视频/动态视频 cover 用 PickCoverUrl，合成视频 cover 用本方法。
        /// 分支条件 cate is not null && cate.CateType != dy_custom_collect → cate 分支
        /// （兜底链 MixInfo → imageUrls → Music，全 ?. 安全；任一段 null/空 list 触发下一段）；
        /// 否则（cate 为 null 或 cate.CateType == dy_custom_collect）→ 非-cate 分支
        /// （只取 imageUrls.FirstOrDefault()?.Path，不查 MixInfo、不查 Music）。
        /// imageUrls 在生产调用方已由 `if(imageUrls==null||!Any())` 守护非空，但 helper 契约
        /// 仍以 FirstOrDefault()?.Path 防御性处理空 list（返回 null）。
        /// 由特征化测试 SyncDecisionHelperTests 锁定当前行为。
        /// </summary>
        public static string PickMergeVideoCoverUrl(DouyinCollectCate cate, Aweme item, List<DouyinMergeVideoDto> imageUrls)
        {
            return cate is not null && cate.CateType != VideoTypeEnum.dy_custom_collect
                ? (item.MixInfo?.CoverUrl?.UrlList?.FirstOrDefault() ?? imageUrls.FirstOrDefault()?.Path ?? item.Music?.CoverHd?.UrlList?.FirstOrDefault())
                : imageUrls.FirstOrDefault()?.Path;
        }
```

Notes:
- This is a verbatim port of the ternary at `job/DouyinBasicSyncJob.cs:985-987`. The only changes vs the original:
  1. `var coverUrl = ...` is replaced with `return ...` (the helper is a single-expression return).
  2. The original's irregular indent (`?` and `:` at 14 spaces, `var coverUrl` at 16 spaces — off by 2) is normalized to consistent 16-space `?`/`:` under the helper's `return` at 12-space indent. This is cosmetic only, no behavior change. Same precedent as slice 9/10 LINQ-chain re-indents.
- All required types live in already-imported namespaces (`DouyinCollectCate`/`VideoTypeEnum` from `dy.net.model.entity` per slice 6; `Aweme`/`MixInfo`/`Music` from `dy.net.model.response`; `DouyinMergeVideoDto` from `dy.net.model.dto` per slice 10). Do NOT add any `using`. No new file, no new enum.
- Match the 8-space method indent of the surrounding methods exactly.

- [ ] **Step 2: Thin the cover-URL ternary in `ProcessImageSetAndMergeToVideo`**

In `job/DouyinBasicSyncJob.cs`, inside `ProcessImageSetAndMergeToVideo`'s `try` block, replace the 3-line ternary at lines 985-987. **Read the method first to confirm the exact text** before editing. The block to replace is currently (note the irregular indent — `var coverUrl` at 16 spaces, `?` and `:` at 14 spaces):

```csharp
                var coverUrl = cate is not null && cate.CateType != VideoTypeEnum.dy_custom_collect
              ? (item.MixInfo?.CoverUrl?.UrlList?.FirstOrDefault() ?? imageUrls.FirstOrDefault()?.Path ?? item.Music?.CoverHd?.UrlList?.FirstOrDefault())
              : imageUrls.FirstOrDefault()?.Path;
```

Replace it with the single line at 16-space indent (matching the surrounding `try` block):

```csharp
                var coverUrl = SyncDecisionHelper.PickMergeVideoCoverUrl(cate, item, imageUrls);
```

Notes:
- This is an in-method local block edit, NOT a whole-method replacement. The method signature, `try`/`catch`, the upstream `imageUrls` build (`:908`) and null/empty guard (`:911-914`), the merge-video file-validation branch (`:964-983`), the downstream `DownAuthorAvatar` call (`:990`), `virtualBitRate` construction (`:993-1001`), `DownVideoCover` call (`:1003`), `CreateVideoEntity` call (`:1006`), the special-field assignment (`:1010-1013`), and the `catch` block are ALL verbatim — do NOT touch.
- `coverUrl` is used at `:1003` as the first positional arg to `DownVideoCover(coverUrl, savePath, cookie, config)` — must remain unchanged. The variable is still declared inline via `var ... = SyncDecisionHelper...` so its type (`string`) is unchanged.
- The 16-space indent must match the surrounding `try`-block code exactly.
- Do NOT touch the call site of `ProcessImageSetAndMergeToVideo` (`:623` in `ProcessVideoList`).
- The original's irregular `?`/`:` 14-space under-indent disappears (no ternary in the job anymore).

- [ ] **Step 3: Build — verify 0 errors**

Run: `DOTNET_ROLL_FORWARD=LatestMajor dotnet build dy.net.csproj`
Expected: `Build succeeded. 0 Error(s)` (or `0 个错误` if Chinese locale).

(The local SDK is 10 and the project targets net8.0 — the `DOTNET_ROLL_FORWARD=LatestMajor` prefix is REQUIRED on every `dotnet` command or the build/test host fails to launch.)

- [ ] **Step 4: Run the existing suite — verify still green**

Run: `DOTNET_ROLL_FORWARD=LatestMajor dotnet test tests/dy.net.Tests/dy.net.Tests.csproj`
Expected: `Passed!  - Failed: 0` — **111 passed** (no new tests yet; the extraction must not break the existing golden masters).

- [ ] **Step 5: Commit**

Stage ONLY the two files — explicit paths, never `git add -A`:

```bash
git add utils/SyncDecisionHelper.cs job/DouyinBasicSyncJob.cs
git -c user.name='Claude Code' -c user.email='mjgenab@gmail.com' commit -m "$(cat <<'EOF'
refactor(job): extract merge-video cover-URL pick to SyncDecisionHelper

Move the 3-line cover-URL ternary out of
DouyinBasicSyncJob.ProcessImageSetAndMergeToVideo into a pure
SyncDecisionHelper.PickMergeVideoCoverUrl(DouyinCollectCate cate, Aweme
item, List<DouyinMergeVideoDto> imageUrls) -> string. Sibling of slice
6's PickCoverUrl (single/dynamic video cover) — completes the
cover-decision family. The job keeps the one-line call plus all
surrounding I/O (avatar download, virtualBitRate construction,
DownVideoCover, CreateVideoEntity).

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 2: Characterization tests for `PickMergeVideoCoverUrl`

**Files:**
- Modify: `tests/dy.net.Tests/SyncDecisionHelperTests.cs` (append a new section before the class-closing `}`)

Golden-master tests pinning the helper's CURRENT behavior. The helper is a verbatim port, so first-run values ARE the golden values.

- [ ] **Step 1: Append the test section**

In `tests/dy.net.Tests/SyncDecisionHelperTests.cs`, insert the following block immediately after the last test method (`CollectAlternateVideoUrls_MultipleBitRatesAndUrls_PreservesEncounterOrder` — its closing `}` is currently the last `}` before the class-closing `}`) and before the class-closing `}`:

```csharp

        // ---- PickMergeVideoCoverUrl ----
        // pin: current behavior, not aspirational

        // Reuses existing section-local helpers:
        //   CoverImg(params string[] urls) → ImageInfo (defined in PickCoverUrl section)
        //   Cate(VideoTypeEnum cateType)   → DouyinCollectCate (defined in BuildVideoFileName section)
        // Adds one new helper:

        private static DouyinMergeVideoDto MergeDto(string path)
            => new DouyinMergeVideoDto { Path = path };

        [Fact]
        public void PickMergeVideoCoverUrl_CateNull_UsesNonCateBranch_ReturnsFirstImageUrlPath()
        {
            // cate == null → non-cate branch → reads only imageUrls (MixInfo/Music ignored even if present)
            var imageUrls = new List<DouyinMergeVideoDto> { MergeDto("img1"), MergeDto("img2") };
            var item = new Aweme();
            var result = SyncDecisionHelper.PickMergeVideoCoverUrl(cate: null, item, imageUrls);
            Assert.Equal("img1", result);
        }

        [Fact]
        public void PickMergeVideoCoverUrl_CateCustomCollect_UsesNonCateBranch_ReturnsFirstImageUrlPath()
        {
            // cate.CateType == dy_custom_collect → still non-cate branch (the explicit carve-out)
            // MixInfo/Music present but MUST be ignored.
            var imageUrls = new List<DouyinMergeVideoDto> { MergeDto("img1") };
            var item = new Aweme
            {
                MixInfo = new MixInfo { CoverUrl = CoverImg("mix1") },
                Music = new Music { CoverHd = CoverImg("mu1") },
            };
            var result = SyncDecisionHelper.PickMergeVideoCoverUrl(Cate(VideoTypeEnum.dy_custom_collect), item, imageUrls);
            Assert.Equal("img1", result);
        }

        [Fact]
        public void PickMergeVideoCoverUrl_CateNonCustomCollect_MixInfoPresent_PrefersMixInfo()
        {
            // cate-branch arm 1: MixInfo.CoverUrl.UrlList[0] wins; imageUrls/Music ignored.
            var imageUrls = new List<DouyinMergeVideoDto> { MergeDto("img1") };
            var item = new Aweme
            {
                MixInfo = new MixInfo { CoverUrl = CoverImg("mix1") },
                Music = new Music { CoverHd = CoverImg("mu1") },
            };
            var result = SyncDecisionHelper.PickMergeVideoCoverUrl(Cate(VideoTypeEnum.dy_mix), item, imageUrls);
            Assert.Equal("mix1", result);
        }

        [Fact]
        public void PickMergeVideoCoverUrl_CateNonCustomCollect_MixInfoNull_FallsBackToImageUrls()
        {
            // cate-branch arm 2: MixInfo == null → ?? imageUrls; Music NOT consulted because imageUrls wins first.
            var imageUrls = new List<DouyinMergeVideoDto> { MergeDto("img1") };
            var item = new Aweme
            {
                MixInfo = null,
                Music = new Music { CoverHd = CoverImg("mu1") },
            };
            var result = SyncDecisionHelper.PickMergeVideoCoverUrl(Cate(VideoTypeEnum.dy_mix), item, imageUrls);
            Assert.Equal("img1", result);
        }

        [Fact]
        public void PickMergeVideoCoverUrl_CateNonCustomCollect_MixInfoUrlListEmpty_FallsBackToImageUrls()
        {
            // cate-branch arm 2 edge: chain non-null all the way to UrlList, but UrlList empty →
            // FirstOrDefault() returns null → ?? imageUrls.
            var imageUrls = new List<DouyinMergeVideoDto> { MergeDto("img1") };
            var item = new Aweme
            {
                MixInfo = new MixInfo { CoverUrl = CoverImg() },
                Music = new Music { CoverHd = CoverImg("mu1") },
            };
            var result = SyncDecisionHelper.PickMergeVideoCoverUrl(Cate(VideoTypeEnum.dy_mix), item, imageUrls);
            Assert.Equal("img1", result);
        }

        [Fact]
        public void PickMergeVideoCoverUrl_CateNonCustomCollect_MixInfoAndImageUrlsEmpty_FallsBackToMusic()
        {
            // cate-branch arm 3: MixInfo null AND imageUrls empty → Music.CoverHd wins.
            var imageUrls = new List<DouyinMergeVideoDto>();
            var item = new Aweme
            {
                MixInfo = null,
                Music = new Music { CoverHd = CoverImg("mu1") },
            };
            var result = SyncDecisionHelper.PickMergeVideoCoverUrl(Cate(VideoTypeEnum.dy_mix), item, imageUrls);
            Assert.Equal("mu1", result);
        }

        [Fact]
        public void PickMergeVideoCoverUrl_CateNonCustomCollect_AllSourcesNull_ReturnsNull()
        {
            // cate-branch all 3 sources unavailable → final null.
            var imageUrls = new List<DouyinMergeVideoDto>();
            var item = new Aweme { MixInfo = null, Music = null };
            var result = SyncDecisionHelper.PickMergeVideoCoverUrl(Cate(VideoTypeEnum.dy_mix), item, imageUrls);
            Assert.Null(result);
        }

        [Fact]
        public void PickMergeVideoCoverUrl_CateNull_EmptyImageUrls_ReturnsNull()
        {
            // non-cate branch with empty imageUrls → null (defensive contract; production
            // caller guards at :911 but the helper handles it cleanly via FirstOrDefault()?.Path).
            // MixInfo/Music present but ignored because non-cate branch never consults them.
            var imageUrls = new List<DouyinMergeVideoDto>();
            var item = new Aweme
            {
                MixInfo = new MixInfo { CoverUrl = CoverImg("mix1") },
                Music = new Music { CoverHd = CoverImg("mu1") },
            };
            var result = SyncDecisionHelper.PickMergeVideoCoverUrl(cate: null, item, imageUrls);
            Assert.Null(result);
        }

        [Fact]
        public void PickMergeVideoCoverUrl_CateNonCustomCollect_ImageUrlsHasMultiple_PrefersFirstNotMusic()
        {
            // FirstOrDefault() returns imageUrls[0], NOT a later element; and Music is NOT consulted
            // because imageUrls is non-empty in the ?? chain.
            var imageUrls = new List<DouyinMergeVideoDto> { MergeDto("first"), MergeDto("second") };
            var item = new Aweme
            {
                MixInfo = null,
                Music = new Music { CoverHd = CoverImg("mu1") },
            };
            var result = SyncDecisionHelper.PickMergeVideoCoverUrl(Cate(VideoTypeEnum.dy_mix), item, imageUrls);
            Assert.Equal("first", result);
        }
```

Notes:
- The new section-local helper `MergeDto(string)` is unique to this section. BEFORE inserting, run `grep -n "MergeDto\b" tests/dy.net.Tests/SyncDecisionHelperTests.cs`. If the name already exists, rename consistently across all its uses and report the rename.
- `CoverImg(params string[] urls)` is defined in the PickCoverUrl section (around line 631) and returns `ImageInfo`. It is a `private static` member of the test class → in scope from this section. Reuse, do NOT re-declare.
- `Cate(VideoTypeEnum cateType)` is defined in the BuildVideoFileName section (around line 483) and returns `DouyinCollectCate`. Same reuse rule. Do NOT re-declare.
- `Aweme`, `MixInfo`, `Music`, `DouyinMergeVideoDto`, `DouyinCollectCate`, `VideoTypeEnum`, `ImageInfo` all already resolved by file-top `using` directives (lines 1-4). `System.Collections.Generic` (`List<T>`) and `System.Linq` resolve via `<ImplicitUsings>enable</ImplicitUsings>`. Do NOT add any `using`.
- Match the indentation of the surrounding test methods exactly (8-space method indent inside the class).
- Do NOT modify any existing test or the helper. Test-only change.

- [ ] **Step 2: Run the new section — verify all 9 pass**

Run: `DOTNET_ROLL_FORWARD=LatestMajor dotnet test tests/dy.net.Tests/dy.net.Tests.csproj --filter "FullyQualifiedName~PickMergeVideoCoverUrl"`
Expected: `Passed!  - Failed: 0` — **9 passed**.

If any fails: the helper is a verbatim port, so a failure means the test input was mis-traced. Re-trace by hand against the helper logic; fix the TEST input/expectation. Do NOT modify the helper. Never weaken an assertion.

- [ ] **Step 3: Run the full suite — verify 120 green**

Run: `DOTNET_ROLL_FORWARD=LatestMajor dotnet test tests/dy.net.Tests/dy.net.Tests.csproj`
Expected: `Passed!  - Failed: 0` — **120 passed** (111 + 9).

- [ ] **Step 4: Commit**

Stage ONLY the test file:

```bash
git add tests/dy.net.Tests/SyncDecisionHelperTests.cs
git -c user.name='Claude Code' -c user.email='mjgenab@gmail.com' commit -m "$(cat <<'EOF'
test: golden-master tests pinning PickMergeVideoCoverUrl

9 characterization [Fact]s: cate=null → non-cate branch (imageUrls
only, MixInfo/Music ignored); cate.CateType=dy_custom_collect carve-out
→ same non-cate branch; cate-branch fallback chain MixInfo → imageUrls
→ Music with each arm independently pinned (MixInfo wins, MixInfo=null
falls to imageUrls, UrlList empty also falls to imageUrls, imageUrls
empty falls to Music, all null → null); non-cate path with empty
imageUrls → null (defensive contract); FirstOrDefault() takes first
element and doesn't consult Music when imageUrls is non-empty. Full
suite 111 → 120.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 3: Update `tests/README.md` coverage doc

**Files:**
- Modify: `tests/README.md`

- [ ] **Step 1: Add `PickMergeVideoCoverUrl` to the `SyncDecisionHelper` table row**

In `tests/README.md`, the "What is pinned" table has one row for `SyncDecisionHelper` (line 24). It currently ends with this exact item (the last entry before the closing ` |`):

```
`CollectAlternateVideoUrls` (候选视频 URL 收集：遍历 item.Video.BitRate，跳过 null bit 与 PlayAddr/UrlList null·空 / payurl == excludeUrl ordinal 区分大小写 / 跨 BitRate 不去重 / 双层保序) |
```

Append `PickMergeVideoCoverUrl` before the closing ` |` — i.e. replace that exact text with:

```
`CollectAlternateVideoUrls` (候选视频 URL 收集：遍历 item.Video.BitRate，跳过 null bit 与 PlayAddr/UrlList null·空 / payurl == excludeUrl ordinal 区分大小写 / 跨 BitRate 不去重 / 双层保序), `PickMergeVideoCoverUrl` (合成视频封面 URL 选取：cate 分支 MixInfo→imageUrls→Music 三级兜底 / 非-cate 或 cate.CateType=dy_custom_collect 仅取 imageUrls 首个 / 与 PickCoverUrl 互为兄弟·收齐 cover 决策家族) |
```

- [ ] **Step 2: Update the "What is intentionally NOT covered" `DouyinBasicSyncJob` entry**

In the "## What is intentionally NOT covered (and why)" section, the first bullet (`**`DouyinBasicSyncJob` orchestration**`) currently ends its `CollectAlternateVideoUrls` clause with this exact span (read the bullet first to confirm — it spans two lines in the file, lines 73-74):

```
  `item.Video` / `item.Video.BitRate` 为 null 的 NRE 路径保留不测) — all pinned (see table
```

Replace that exact span with (this appends a new `PickMergeVideoCoverUrl` clause after the `CollectAlternateVideoUrls` clause's close-paren, then resumes the original ` — all pinned (see table` text):

```
  `item.Video` / `item.Video.BitRate` 为 null 的 NRE 路径保留不测),
  `PickMergeVideoCoverUrl` (`ProcessImageSetAndMergeToVideo` 的合成视频封面 URL 选取
  已抽出并 pinned，与 slice 6 `PickCoverUrl` 互为兄弟、收齐 cover 决策家族；
  其下游 `DownAuthorAvatar`/`DownVideoCover` I/O、`virtualBitRate` 构造（含 `FileInfo`
  读盘）、`CreateVideoEntity` 调用与特殊字段赋值、整体 `try/catch` 仍在 job 薄壳内、
  未覆盖；`imageUrls`/`item` 为 null 的 NRE 路径保留不测，由调用方 :911 守护) — all pinned (see table
```

The ` — all pinned (see table` suffix occurs exactly once in the file, so this span is a unique match. Do NOT change the "Still uncovered:" list that follows.

- [ ] **Step 3: Verify the doc reads correctly**

Run: `grep -n "PickMergeVideoCoverUrl" tests/README.md`
Expected: 2 matches (the table row + the NOT-covered entry).

Run: `grep -c "all pinned (see table" tests/README.md`
Expected: `1` (the suffix should remain unique).

Run: `DOTNET_ROLL_FORWARD=LatestMajor dotnet test tests/dy.net.Tests/dy.net.Tests.csproj`
Expected: `Passed!  - Failed: 0` — **120 passed** (a doc change must not affect the build/tests).

- [ ] **Step 4: Commit**

Stage `tests/README.md` and this plan file:

```bash
git add tests/README.md docs/superpowers/plans/2026-05-24-extract-pickmergevideocoverurl.md
git -c user.name='Claude Code' -c user.email='mjgenab@gmail.com' commit -m "$(cat <<'EOF'
docs: pin PickMergeVideoCoverUrl coverage in tests/README

Also commits the twelfth-slice implementation plan.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Final Steps (after all tasks)

- [ ] Push the commit chain to origin: `git push origin decompile/dy-sync-lib` — **do NOT merge, do NOT open a PR** (standing constraint). This environment may print a misleading "User cancelled dialog" or a transient TLS handshake error (`GnuTLS, handshake failed`) — retry up to 3×; verify the true state with `git status -sb` (expect `## decompile/dy-sync-lib...origin/decompile/dy-sync-lib` with no `ahead`).
- [ ] Update project memory (`project-dysync-security-hardening.md`): twelfth slice done, `SyncDecisionHelper` now 16 pure methods, `SyncDecisionHelperTests` 103 cases, full suite 120 green, branch head = new push SHA.

---

## Self-Review

**Spec coverage:**
- `SyncDecisionHelper.PickMergeVideoCoverUrl` (verbatim port, ternary → return statement) → Task 1 Step 1. ✓
- Thin `ProcessImageSetAndMergeToVideo` block, `coverUrl` downstream use at `:1003` retained → Task 1 Step 2. ✓
- "Quirk 1" — branch decision (cate=null / dy_custom_collect / other) → Task 2 tests #1, #2, #3 (one per case). ✓
- "Quirk 2" — `dy_custom_collect` carve-out → Task 2 test `CateCustomCollect_UsesNonCateBranch_*` with MixInfo+Music present-but-ignored. ✓
- "Quirk 3" — cate-branch fallback chain → Task 2 tests #3 (MixInfo wins), #4 (MixInfo null), #5 (UrlList empty), #6 (Music wins), #7 (all null). ✓
- "Quirk 4" — non-cate branch ignores MixInfo/Music → Task 2 test #2 (also #8, which sets MixInfo+Music but cate=null → result is null because non-cate doesn't consult them). ✓
- "Quirk 5" — defensive empty-imageUrls → Task 2 test #8. ✓
- "Quirk 6" — `FirstOrDefault()` order → Task 2 test #9. ✓
- 9 characterization `[Fact]`s with 1 new section-local helper (`MergeDto`) reusing existing `CoverImg`/`Cate` → Task 2 Step 1. ✓
- `tests/README.md` updates (table row + NOT-covered clause) → Task 3 Steps 1-2. ✓
- Build/test via `DOTNET_ROLL_FORWARD=LatestMajor`, explicit `git add <path>`, push not merge → all task steps + Final Steps. ✓

**Placeholder scan:** No TBD/TODO; every code step shows complete code; every command shows expected output. ✓

**Type consistency:** `PickMergeVideoCoverUrl(DouyinCollectCate, Aweme, List<DouyinMergeVideoDto>) → string` — identical across Task 1 (helper, job call) and Task 2 (9 test calls). Test helper `MergeDto(string) → DouyinMergeVideoDto` consistent across all uses. Reused helpers `CoverImg(params string[]) → ImageInfo` and `Cate(VideoTypeEnum) → DouyinCollectCate` exist in the test file (verified at lines 631 and 483 respectively). `Aweme.MixInfo`, `MixInfo.CoverUrl`, `Music.CoverHd`, `DouyinMergeVideoDto.Path`, `DouyinCollectCate.CateType` all verified against slice 6's `PickCoverUrl` tests which already exercise these chains. ✓

**Test trace check:**
- CateNull #1 — `cate=null` → cond=false → non-cate branch → `imageUrls.FirstOrDefault()?.Path` → `imageUrls[0].Path` = `"img1"`. ✓
- CateCustomCollect #2 — `cate.CateType=dy_custom_collect` → cond=`true && false`=false → non-cate branch → `"img1"` (MixInfo/Music ignored). ✓
- MixInfoPresent #3 — `cate.CateType=dy_mix ≠ dy_custom_collect` → cond=true → cate branch → `MixInfo.CoverUrl.UrlList[0]` = `"mix1"`. ✓
- MixInfoNull #4 — cate branch; `MixInfo?` null → ?? `imageUrls.FirstOrDefault()?.Path` = `"img1"`. ✓
- MixInfoUrlListEmpty #5 — cate branch; `CoverImg()` makes `UrlList=[]` → `FirstOrDefault()=null` → ?? `imageUrls[0].Path` = `"img1"`. ✓
- MixInfoAndImageUrlsEmpty #6 — cate branch; MixInfo=null → `imageUrls.FirstOrDefault()` (empty list) → `null?.Path` = null → ?? `Music.CoverHd.UrlList[0]` = `"mu1"`. ✓
- AllSourcesNull #7 — cate branch; MixInfo=null, imageUrls=[], Music=null → all three nulls → final null. ✓
- CateNull empty imageUrls #8 — cate=null → non-cate branch → `imageUrls.FirstOrDefault()` (empty) → `null?.Path` = null. MixInfo/Music NOT consulted. ✓
- FirstOrDefault order #9 — cate branch; MixInfo=null → imageUrls non-empty → `FirstOrDefault()` = `MergeDto("first")` → `.Path` = `"first"`. Music NOT consulted because `??` is short-circuit. ✓
