# Extract Image URL Extraction Logic — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Extract the image-URL extraction LINQ chain from `DouyinBasicSyncJob.ProcessImageSetAndMergeToVideo` into a pure, independently-testable `SyncDecisionHelper.BuildImageUrls`, leaving all merge/I/O orchestration in the job.

**Architecture:** Behavior-preserving "thin shell" extraction (10th god-class slice). The static-image sibling of slice 9's `BuildDynamicVideoUrls`: a pure LINQ transform of `item.Images` (`Images → UrlList first URL + dimensions → DouyinMergeVideoDto`) moves to `SyncDecisionHelper`; the job keeps a one-line call plus the existing null/empty guard and all merge I/O. Golden-master characterization tests pin the helper. No new file, no new enum.

**Tech Stack:** .NET 8 (`net8.0`; local SDK 10 → all `dotnet` commands prefixed `DOTNET_ROLL_FORWARD=LatestMajor`), xUnit (`tests/dy.net.Tests`), C#.

**Spec:** `docs/superpowers/specs/2026-05-23-extract-buildimageurls-design.md`

---

## File Structure

- **Modify:** `utils/SyncDecisionHelper.cs` — append one pure method `BuildImageUrls`; existing 13 methods untouched.
- **Modify:** `job/DouyinBasicSyncJob.cs` — `ProcessImageSetAndMergeToVideo` (lines 929-934, the `try`-block opening): replace the image-URL LINQ chain with a one-line helper call; the rest of the method verbatim.
- **Modify:** `tests/dy.net.Tests/SyncDecisionHelperTests.cs` — append one `// ---- BuildImageUrls ----` section (8 `[Fact]` + 2 section-local helpers).
- **Modify:** `tests/README.md` — record the new pinned coverage.

`ProcessImageSetAndMergeToVideo` is non-`virtual` (cannot be overridden) with one call site (`:623`, same file) → job-side change is confined to `DouyinBasicSyncJob.cs`.

---

## Task 1: Extract `BuildImageUrls` + thin `ProcessImageSetAndMergeToVideo`

**Files:**
- Modify: `utils/SyncDecisionHelper.cs` (append before the class-closing `}` — after the existing last method `BuildDynamicVideoUrls`)
- Modify: `job/DouyinBasicSyncJob.cs:929-934` (the image-URL LINQ chain inside `ProcessImageSetAndMergeToVideo`'s `try` block)

- [ ] **Step 1: Append `BuildImageUrls` to `SyncDecisionHelper`**

In `utils/SyncDecisionHelper.cs`, insert this method immediately after `BuildDynamicVideoUrls`'s closing `}` and before the class-closing `}`. The file currently ends with the `BuildDynamicVideoUrls` method (its body ends `            return dynamicVideoUrls;\n        }`), then `    }` (class close), then `}` (namespace close). Insert between the method close and the class close:

```csharp

        /// <summary>
        /// 从 DouyinBasicSyncJob.ProcessImageSetAndMergeToVideo 抽出的纯图片 URL 提取逻辑（无 I/O）。
        /// 行为逐字保留：遍历 item.Images，保留 UrlList 非空者，取每张图 UrlList 首个 URL 与宽高
        /// 构造 DouyinMergeVideoDto，再滤掉 Path 为空白者。
        /// 注意 item.Images 为 null 时 ?. 短路 → 返回 null（非空 list），与 BuildDynamicVideoUrls 不同；
        /// 调用方守卫同时吃 null 与空 list。只取每张图 UrlList 首个 URL，首个为空白则整张图被丢弃。
        /// 由特征化测试 SyncDecisionHelperTests 锁定当前行为。
        /// </summary>
        public static List<DouyinMergeVideoDto> BuildImageUrls(Aweme item)
        {
            // 提取图片URL列表
            return item.Images?
                .Where(img => img.UrlList != null && img.UrlList.Any())
                .Select(img => new DouyinMergeVideoDto { Path = img.UrlList.FirstOrDefault(), Height = img.Height, Width = img.Width })
                .Where(img => !string.IsNullOrWhiteSpace(img.Path))
                .ToList();
        }
```

Notes:
- This is a verbatim port of the LINQ chain at `job/DouyinBasicSyncJob.cs:930-934`. The `.Where`/`.Select`/`.ToList()` calls were flush at column 16 in the original method; here they are re-indented one level under the `return` to match the helper's body nesting — this is cosmetic only, no behavior change.
- The leading `?.` on `item.Images` is preserved verbatim: when `item.Images` is null, the whole chain short-circuits and the method returns `null` (NOT an empty list). This differs from `BuildDynamicVideoUrls` (which returns an empty list) — keep it exactly as written.
- The `// 提取图片URL列表` comment moves into the helper verbatim.
- `SyncDecisionHelper.cs` already has `using dy.net.model.dto;` (`DouyinMergeVideoDto`) and `using dy.net.model.response;` (`Aweme`); the project has `<ImplicitUsings>enable</ImplicitUsings>` covering `System.Linq` and `System.Collections.Generic`. Do NOT add any new `using`. No new file, no new enum.
- Match the 8-space method indent of the surrounding methods exactly.

- [ ] **Step 2: Thin the image-URL chain in `ProcessImageSetAndMergeToVideo`**

In `job/DouyinBasicSyncJob.cs`, inside `ProcessImageSetAndMergeToVideo`'s `try` block, replace the block at lines 929-934. **Read the method first to confirm the exact text** before editing. The block to replace is currently (16-space indent — it sits inside `try` inside the method):

```csharp
                // 提取图片URL列表
                List<DouyinMergeVideoDto> imageUrls = item.Images?
                .Where(img => img.UrlList != null && img.UrlList.Any())
                .Select(img => new DouyinMergeVideoDto { Path = img.UrlList.FirstOrDefault(), Height = img.Height, Width = img.Width })
                .Where(img => !string.IsNullOrWhiteSpace(img.Path))
                .ToList();
```

Replace it with the single line (the `// 提取图片URL列表` comment moved into the helper in Step 1, so it does NOT appear here):

```csharp
                List<DouyinMergeVideoDto> imageUrls = SyncDecisionHelper.BuildImageUrls(item);
```

Notes:
- This is an in-method local block edit, NOT a whole-method replacement. `ProcessImageSetAndMergeToVideo`'s signature, the `try`/`catch`, the `if (imageUrls == null || !imageUrls.Any()) return null;` guard right after, and everything else are verbatim — do NOT touch them.
- `imageUrls` is used again later in the method (the `if` guard, the `MediaMergeRequest.ImageUrls` assignment, the `coverUrl` derivation) — those references read the same local variable and must NOT change.
- The 16-space indent must match the surrounding `try`-block code exactly.
- Do NOT touch the call site of `ProcessImageSetAndMergeToVideo` (`:623`).

- [ ] **Step 3: Build — verify 0 errors**

Run: `DOTNET_ROLL_FORWARD=LatestMajor dotnet build dy.net.csproj`
Expected: `Build succeeded. 0 Error(s)`.

(The local SDK is 10 and the project targets net8.0 — the `DOTNET_ROLL_FORWARD=LatestMajor` prefix is REQUIRED on every `dotnet` command or the build/test host fails to launch.)

- [ ] **Step 4: Run the existing suite — verify still green**

Run: `DOTNET_ROLL_FORWARD=LatestMajor dotnet test tests/dy.net.Tests/dy.net.Tests.csproj`
Expected: `Passed!  - Failed: 0` — **94 passed** (no new tests yet; the extraction must not break the existing golden masters).

- [ ] **Step 5: Commit**

Stage ONLY the two files — explicit paths, never `git add -A`:

```bash
git add utils/SyncDecisionHelper.cs job/DouyinBasicSyncJob.cs
git -c user.name='Claude Code' -c user.email='mjgenab@gmail.com' commit -m "$(cat <<'EOF'
refactor(job): extract image URL extraction to SyncDecisionHelper

Move the image-URL extraction LINQ chain (Images → UrlList first URL +
dimensions → DouyinMergeVideoDto, dropping blank-Path entries) out of
DouyinBasicSyncJob.ProcessImageSetAndMergeToVideo into a pure
SyncDecisionHelper.BuildImageUrls. The static-image sibling of slice 9's
BuildDynamicVideoUrls. The job keeps a one-line call plus the existing
null/empty guard and all merge / cover / avatar I/O.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 2: Characterization tests for `BuildImageUrls`

**Files:**
- Modify: `tests/dy.net.Tests/SyncDecisionHelperTests.cs` (append a new section before the class-closing `}`)

Golden-master tests pinning the helper's CURRENT behavior. The helper is a verbatim port, so first-run values ARE the golden values.

- [ ] **Step 1: Append the test section**

In `tests/dy.net.Tests/SyncDecisionHelperTests.cs`, insert the following block immediately after the last test method (`BuildDynamicVideoUrls_MultipleImagesAndBitRates_CollectsEachMatchInEncounterOrder` — its closing `}`) and before the class-closing `}`:

```csharp

        // ---- BuildImageUrls ----
        // pin: current behavior, not aspirational

        private static ImageItemInfo ImageUrlItem(int height, int width, params string[] urls)
            => new ImageItemInfo { Height = height, Width = width, UrlList = urls.ToList() };

        private static Aweme AwemeWithImageItems(params ImageItemInfo[] images)
            => new Aweme { Images = images.ToList() };

        [Fact]
        public void BuildImageUrls_ImagesNull_ReturnsNull()
        {
            // item.Images == null → ?. short-circuits → returns null (NOT an empty list)
            var result = SyncDecisionHelper.BuildImageUrls(new Aweme { Images = null });
            Assert.Null(result);
        }

        [Fact]
        public void BuildImageUrls_ImagesEmpty_ReturnsEmptyList()
        {
            // item.Images == [] → chain runs → empty (non-null) list
            var result = SyncDecisionHelper.BuildImageUrls(AwemeWithImageItems());
            Assert.Empty(result);
        }

        [Fact]
        public void BuildImageUrls_UrlListNull_ImageFilteredOut()
        {
            // UrlList == null → first .Where(UrlList != null) drops the image
            var item = AwemeWithImageItems(new ImageItemInfo { Height = 10, Width = 10, UrlList = null });
            var result = SyncDecisionHelper.BuildImageUrls(item);
            Assert.Empty(result);
        }

        [Fact]
        public void BuildImageUrls_UrlListEmpty_ImageFilteredOut()
        {
            // UrlList == [] → first .Where(UrlList.Any()) drops the image
            var item = AwemeWithImageItems(ImageUrlItem(10, 10));
            var result = SyncDecisionHelper.BuildImageUrls(item);
            Assert.Empty(result);
        }

        [Fact]
        public void BuildImageUrls_ValidImage_BuildsDtoWithFirstUrlAndDimensions()
        {
            var item = AwemeWithImageItems(ImageUrlItem(1920, 1080, "u1"));
            var result = SyncDecisionHelper.BuildImageUrls(item);
            var dto = Assert.Single(result);
            Assert.Equal("u1", dto.Path);
            Assert.Equal(1920, dto.Height);
            Assert.Equal(1080, dto.Width);
        }

        [Fact]
        public void BuildImageUrls_MultipleUrls_TakesFirstUrl()
        {
            // .Select takes UrlList.FirstOrDefault()
            var item = AwemeWithImageItems(ImageUrlItem(10, 10, "u1", "u2"));
            var result = SyncDecisionHelper.BuildImageUrls(item);
            var dto = Assert.Single(result);
            Assert.Equal("u1", dto.Path);
        }

        [Fact]
        public void BuildImageUrls_FirstUrlBlank_ImageFilteredOut()
        {
            // first URL is whitespace → Path is blank → second .Where drops the image
            // even though a valid second URL exists (only the first URL is ever considered)
            var item = AwemeWithImageItems(ImageUrlItem(10, 10, "   ", "u2"));
            var result = SyncDecisionHelper.BuildImageUrls(item);
            Assert.Empty(result);
        }

        [Fact]
        public void BuildImageUrls_MultipleImagesMixed_KeepsOnlyValidInEncounterOrder()
        {
            var item = AwemeWithImageItems(
                ImageUrlItem(1, 1, "valid1"),
                new ImageItemInfo { Height = 2, Width = 2, UrlList = null },
                ImageUrlItem(3, 3, "   ", "x"),
                ImageUrlItem(4, 4, "valid2"));
            var result = SyncDecisionHelper.BuildImageUrls(item);
            Assert.Collection(result,
                d => Assert.Equal("valid1", d.Path),
                d => Assert.Equal("valid2", d.Path));
        }
```

Notes:
- `Aweme`, `ImageItemInfo` are in `dy.net.model.response`; `DouyinMergeVideoDto` is in `dy.net.model.dto`. Both namespaces are already imported at the top of the file. `System.Linq` (`.ToList()`) resolves via `<ImplicitUsings>enable</ImplicitUsings>`. Do NOT add any `using` directives.
- The names `ImageUrlItem`, `AwemeWithImageItems` are new section-local helpers. BEFORE inserting, run `grep -n "ImageUrlItem\|AwemeWithImageItems" tests/dy.net.Tests/SyncDecisionHelperTests.cs`. If either name already exists, rename the new helper consistently across all its uses and report the rename. (Note: slice 9 already uses `AwemeWithImages` / `DynImage` etc. — `ImageUrlItem`/`AwemeWithImageItems` are deliberately distinct, but verify.)
- Match the indentation of the surrounding test methods exactly (8-space method indent inside the class).
- Do NOT modify any existing test or the helper. Test-only change.

- [ ] **Step 2: Run the new section — verify all 8 pass**

Run: `DOTNET_ROLL_FORWARD=LatestMajor dotnet test tests/dy.net.Tests/dy.net.Tests.csproj --filter "FullyQualifiedName~BuildImageUrls"`
Expected: `Passed!  - Failed: 0` — **8 passed**.

If any fails: the helper is a verbatim port, so a failure means the test input was mis-traced. Re-trace by hand against the helper logic; fix the TEST input/expectation. Do NOT modify the helper. Never weaken an assertion.

- [ ] **Step 3: Run the full suite — verify 102 green**

Run: `DOTNET_ROLL_FORWARD=LatestMajor dotnet test tests/dy.net.Tests/dy.net.Tests.csproj`
Expected: `Passed!  - Failed: 0` — **102 passed** (94 + 8).

- [ ] **Step 4: Commit**

Stage ONLY the test file:

```bash
git add tests/dy.net.Tests/SyncDecisionHelperTests.cs
git -c user.name='Claude Code' -c user.email='mjgenab@gmail.com' commit -m "$(cat <<'EOF'
test: golden-master tests pinning BuildImageUrls

8 characterization [Fact]s: Images null → null (vs empty list when
Images is []), images with null/empty UrlList filtered out, a valid
image builds a DTO with the first URL + dimensions, only the first
UrlList URL is taken, a blank first URL drops the whole image, and a
mixed image list keeps only valid entries in encounter order. Full
suite 94 → 102.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 3: Update `tests/README.md` coverage doc

**Files:**
- Modify: `tests/README.md`

- [ ] **Step 1: Add `BuildImageUrls` to the `SyncDecisionHelper` table row**

In `tests/README.md`, the "What is pinned" table has one row for `SyncDecisionHelper` (line 24). It currently ends with this exact item (the last entry before the closing ` |`):

```
`BuildDynamicVideoUrls` (动态视频 URL 构建：遍历 Images→DynamicVideo.BitRate，取 PlayAddr.UrlList 首个 …/aweme/v1/play 前缀 URL 构造 DouyinMergeVideoDto / Images 空→空 list / 非匹配 URL 跳过 / 多 Image·BitRate 按序收集) |
```

Append `BuildImageUrls` before the closing ` |` — i.e. replace that exact text with:

```
`BuildDynamicVideoUrls` (动态视频 URL 构建：遍历 Images→DynamicVideo.BitRate，取 PlayAddr.UrlList 首个 …/aweme/v1/play 前缀 URL 构造 DouyinMergeVideoDto / Images 空→空 list / 非匹配 URL 跳过 / 多 Image·BitRate 按序收集), `BuildImageUrls` (图片 URL 提取：遍历 Images 取每张图 UrlList 首个 URL + 宽高构造 DouyinMergeVideoDto / UrlList 空或首 URL 空白→滤除 / Images=null→null、Images=空→空 list / 多图保序) |
```

- [ ] **Step 2: Update the "What is intentionally NOT covered" `DouyinBasicSyncJob` entry**

In the "## What is intentionally NOT covered (and why)" section, the first bullet (`**`DouyinBasicSyncJob` orchestration**`) currently ends its `BuildDynamicVideoUrls` clause with this exact span (read the bullet first to confirm — it spans two lines in the file):

```
  不可达死代码保留不测) — all pinned (see table
```

Replace that exact span with (this appends a new `BuildImageUrls` clause after the `BuildDynamicVideoUrls` clause's close-paren, then resumes the original ` — all pinned (see table` text):

```
  不可达死代码保留不测),
  `BuildImageUrls` (`ProcessImageSetAndMergeToVideo` 的图片 URL 提取段已抽出并 pinned；其
  `MergeToVideo` 合成、`config.DownImageVideo` 校验与文件清理、`coverUrl` 派生、
  `DownAuthorAvatar`/`DownVideoCover` I/O、`virtualBitRate` 构造（含 `FileInfo` 读盘）、
  `CreateVideoEntity` 调用与特殊字段赋值、整体 `try/catch` 仍在 job、未覆盖) — all pinned (see table
```

The ` — all pinned (see table` suffix occurs exactly once in the file, so this span is a unique match. Do NOT change the "Still uncovered:" list that follows.

- [ ] **Step 3: Verify the doc reads correctly**

Run: `grep -n "BuildImageUrls" tests/README.md`
Expected: 2 matches (the table row + the NOT-covered entry).

Run: `DOTNET_ROLL_FORWARD=LatestMajor dotnet test tests/dy.net.Tests/dy.net.Tests.csproj`
Expected: `Passed!  - Failed: 0` — **102 passed** (a doc change must not affect the build/tests).

- [ ] **Step 4: Commit**

Stage `tests/README.md` and this plan file:

```bash
git add tests/README.md docs/superpowers/plans/2026-05-23-extract-buildimageurls.md
git -c user.name='Claude Code' -c user.email='mjgenab@gmail.com' commit -m "$(cat <<'EOF'
docs: pin BuildImageUrls coverage in tests/README

Also commits the tenth-slice implementation plan.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Final Steps (after all tasks)

- [ ] Push the commit chain to origin: `git push origin decompile/dy-sync-lib` — **do NOT merge, do NOT open a PR** (standing constraint). This environment may print a misleading "User cancelled dialog" or a transient TLS handshake error (`GnuTLS, handshake failed`) — retry up to 3×; verify the true state with `git status -sb` (expect `## decompile/dy-sync-lib...origin/decompile/dy-sync-lib` with no `ahead`).
- [ ] Update project memory (`project-dysync-security-hardening.md`): tenth slice done, `SyncDecisionHelper` now 14 pure methods, `SyncDecisionHelperTests` 85 cases, full suite 102 green, branch head = new push SHA.

---

## Self-Review

**Spec coverage:**
- `SyncDecisionHelper.BuildImageUrls` (verbatim LINQ port, `?.` preserved so null Images → null) → Task 1 Step 1. ✓
- Thin `ProcessImageSetAndMergeToVideo` block, `imageUrls` guard + downstream uses retained → Task 1 Step 2. ✓
- `// 提取图片URL列表` comment moved into the helper → Task 1 Steps 1-2. ✓
- "Quirk 1" — null Images → null (not empty list) → Task 1 Step 1 doc comment + note, Task 2 test `ImagesNull_ReturnsNull` vs `ImagesEmpty_ReturnsEmptyList`. ✓
- "Quirk 2" — only the first UrlList URL considered, blank first URL drops the image → Task 1 Step 1 doc comment, Task 2 tests `MultipleUrls_TakesFirstUrl` + `FirstUrlBlank_ImageFilteredOut`. ✓
- 8 characterization `[Fact]`s (Images null, Images empty, UrlList null, UrlList empty, valid image → DTO, multiple URLs → first, first URL blank → filtered, mixed images → order) → Task 2 Step 1. ✓
- `tests/README.md` updates (table row + NOT-covered clause) → Task 3. ✓
- Build/test via `DOTNET_ROLL_FORWARD=LatestMajor`, explicit `git add <path>`, push not merge → all task steps + Final Steps. ✓

**Placeholder scan:** No TBD/TODO; every code step shows complete code; every command shows expected output. ✓

**Type consistency:** `BuildImageUrls(Aweme item)` returning `List<DouyinMergeVideoDto>` — identical across Task 1 (helper, job call) and Task 2 (8 test calls). Test helpers `ImageUrlItem(int,int,params string[])→ImageItemInfo` and `AwemeWithImageItems(params ImageItemInfo[])→Aweme` are consistent across all uses. `ImageItemInfo.UrlList`/`Height`/`Width` and `DouyinMergeVideoDto.Path`/`Height`/`Width` match the model. ✓

**Test trace check:**
- ImagesNull — `Images=null` → `item.Images?` short-circuits → `null`. ✓
- ImagesEmpty — `Images=[]` → `[].Where().Select().Where().ToList()` → empty list. ✓
- UrlListNull — `UrlList=null` → `.Where(UrlList != null ...)` false → filtered → empty. ✓
- UrlListEmpty — `UrlList=[]` → `.Where(... UrlList.Any())` false → filtered → empty. ✓
- ValidImage — `UrlList=["u1"]` → `.Select` → `Path="u1"`, `Height=1920`, `Width=1080`; second `.Where` passes → 1 DTO. ✓
- MultipleUrls — `UrlList=["u1","u2"]` → `FirstOrDefault()="u1"` → `Path="u1"`. ✓
- FirstUrlBlank — `UrlList=["   ","u2"]` → first `.Where` passes (2 elements) → `Path=FirstOrDefault()="   "` → second `.Where(!IsNullOrWhiteSpace)` false → filtered → empty. ✓
- MultipleImagesMixed — img1 `["valid1"]` kept, img2 `UrlList=null` filtered, img3 `["   ","x"]` filtered (blank first), img4 `["valid2"]` kept → `[valid1, valid2]` in order. ✓
