# Extract Dynamic-Video URL Build Logic — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Extract the dynamic-video URL build block from `DouyinBasicSyncJob.ProcessVideoList` into a pure, independently-testable `SyncDecisionHelper.BuildDynamicVideoUrls`, leaving the `config.DownDynamicVideo` switch and all downstream I/O orchestration in a thin job shell.

**Architecture:** Behavior-preserving "thin shell" extraction (ninth god-class slice). The first slice to reach *inside* an orchestration body (`ProcessVideoList`): a pure three-level loop (`Images` → `DynamicVideo.BitRate` → `PlayAddr.UrlList` prefix pick → `DouyinMergeVideoDto`) moves to `SyncDecisionHelper`; the `config.DownDynamicVideo` gate stays in the job. Golden-master characterization tests pin the helper. No new file, no new enum.

**Tech Stack:** .NET 8 (`net8.0`; local SDK 10 → all `dotnet` commands prefixed `DOTNET_ROLL_FORWARD=LatestMajor`), xUnit (`tests/dy.net.Tests`), C#.

**Spec:** `docs/superpowers/specs/2026-05-23-extract-builddynamicvideourls-design.md`

---

## File Structure

- **Modify:** `utils/SyncDecisionHelper.cs` — append one pure method `BuildDynamicVideoUrls`; existing 12 methods untouched.
- **Modify:** `job/DouyinBasicSyncJob.cs` — `ProcessVideoList` (lines 547-574, inside the `else` branch): replace the dynamic-URL build block with a guarded helper call; the rest of the method verbatim.
- **Modify:** `tests/dy.net.Tests/SyncDecisionHelperTests.cs` — append one `// ---- BuildDynamicVideoUrls ----` section (8 `[Fact]` + section-local helpers).
- **Modify:** `tests/README.md` — record the new pinned coverage.

`ProcessVideoList` is non-`virtual` (cannot be overridden) with one call site (`:421`, same file) → job-side change is confined to `DouyinBasicSyncJob.cs`.

---

## Task 1: Extract `BuildDynamicVideoUrls` + thin `ProcessVideoList`

**Files:**
- Modify: `utils/SyncDecisionHelper.cs` (append before the class-closing `}` — after `PickAuthorAvatarUrl`)
- Modify: `job/DouyinBasicSyncJob.cs:547-574` (the dynamic-URL build block inside `ProcessVideoList`'s `else` branch)

- [ ] **Step 1: Append `BuildDynamicVideoUrls` to `SyncDecisionHelper`**

In `utils/SyncDecisionHelper.cs`, insert this method immediately after `PickAuthorAvatarUrl`'s closing `}` and before the class-closing `}`. The file currently ends with the `PickAuthorAvatarUrl` method, then `    }` (class close), then `}` (namespace close). Insert between the method close and the class close:

```csharp

        /// <summary>
        /// 从 DouyinBasicSyncJob.ProcessVideoList 抽出的纯动态视频 URL 构建逻辑（无 I/O）。
        /// 行为逐字保留：遍历 item.Images → 每个 DynamicVideo.BitRate → 取 PlayAddr.UrlList
        /// 中首个以 https://www.douyin.com/aweme/v1/play 打头的 URL，命中则构造
        /// DouyinMergeVideoDto { Path, Height, Width } 入列；Images 为 null/空时返回空 list。
        /// 调用方（job 薄壳）保留 config.DownDynamicVideo 开关，仅在开关开启时调用本方法。
        /// PlayAddr.Height/Width 为非空 int，?? 1920 / ?? 1080 兜底为不可达死代码，逐字保留不删。
        /// 由特征化测试 SyncDecisionHelperTests 锁定当前行为。
        /// </summary>
        public static List<DouyinMergeVideoDto> BuildDynamicVideoUrls(Aweme item)
        {
            List<DouyinMergeVideoDto> dynamicVideoUrls = new List<DouyinMergeVideoDto>();
            // 当需要下载动态视频时，获取其他URL
            if (item.Images != null && item.Images.Count > 0)
            {
                foreach (var img in item.Images)
                {
                    if (img.DynamicVideo?.BitRate?.Count > 0)
                    {
                        foreach (var btv in img.DynamicVideo.BitRate)
                        {
                            var targetUrl = btv.PlayAddr?.UrlList?.FirstOrDefault(x => x.StartsWith("https://www.douyin.com/aweme/v1/play"));
                            if (targetUrl != null)
                            {
                                var height = btv.PlayAddr?.Height ?? 1920;
                                var width = btv.PlayAddr?.Width ?? 1080;
                                DouyinMergeVideoDto info = new DouyinMergeVideoDto
                                {
                                    Path = targetUrl,
                                    Height = height,
                                    Width = width
                                };
                                dynamicVideoUrls.Add(info);
                            }
                        }
                    }
                }
            }
            return dynamicVideoUrls;
        }
```

Notes for the implementer:
- This is a verbatim port of `job/DouyinBasicSyncJob.cs:548-573`. The ONLY change vs the original `if` is that `config.DownDynamicVideo &&` is dropped from the condition — that switch stays in the job shell (Step 2). The `item.Images != null && item.Images.Count > 0` check stays in the helper's `if`.
- The `// 当需要下载动态视频时，获取其他URL` comment moves into the helper verbatim (it sits above the helper's `if`). It reads slightly out of place here since the `DownDynamicVideo` switch it references now lives in the job shell — preserve it verbatim, do NOT reword (verbatim-port discipline).
- The `?? 1920` / `?? 1080` fallbacks are unreachable dead code (`PlayAddr.Height`/`Width` are non-nullable `int`, and reaching that line requires `btv.PlayAddr` non-null) — keep them verbatim, do NOT remove or "fix".
- `SyncDecisionHelper.cs` already has `using dy.net.model.dto;` (`DouyinMergeVideoDto`) and `using dy.net.model.response;` (`Aweme`, `VideoBitRate`, etc.); the project has `<ImplicitUsings>enable</ImplicitUsings>` covering `System.Linq` (`FirstOrDefault`) and `System.Collections.Generic` (`List<>`). Do NOT add any new `using`. No new file, no new enum.
- Match the 8-space method indent of the surrounding methods exactly.

- [ ] **Step 2: Thin the dynamic-URL build block in `ProcessVideoList`**

In `job/DouyinBasicSyncJob.cs`, inside `ProcessVideoList`'s `else` branch, replace the block at lines 547-574. **Read the method first to confirm the exact text** before editing. The block to replace is currently:

```csharp
                    //处理多个视频-组合的图文视频--类似动图。
                    List<DouyinMergeVideoDto> dynamicVideoUrls = new List<DouyinMergeVideoDto>();
                    // 当需要下载动态视频时，获取其他URL
                    if (config.DownDynamicVideo && item.Images != null && item.Images.Count > 0)
                    {
                        foreach (var img in item.Images)
                        {
                            if (img.DynamicVideo?.BitRate?.Count > 0)
                            {
                                foreach (var btv in img.DynamicVideo.BitRate)
                                {
                                    var targetUrl = btv.PlayAddr?.UrlList?.FirstOrDefault(x => x.StartsWith("https://www.douyin.com/aweme/v1/play"));
                                    if (targetUrl != null)
                                    {
                                        var height = btv.PlayAddr?.Height ?? 1920;
                                        var width = btv.PlayAddr?.Width ?? 1080;
                                        DouyinMergeVideoDto info = new DouyinMergeVideoDto
                                        {
                                            Path = targetUrl,
                                            Height = height,
                                            Width = width
                                        };
                                        dynamicVideoUrls.Add(info);
                                    }
                                }
                            }
                        }
                    }
```

Replace it with (note: the `//处理多个视频...` comment is KEPT in the shell; the `// 当需要下载动态视频时...` comment was moved into the helper in Step 1, so it does NOT appear here):

```csharp
                    //处理多个视频-组合的图文视频--类似动图。
                    List<DouyinMergeVideoDto> dynamicVideoUrls = new List<DouyinMergeVideoDto>();
                    if (config.DownDynamicVideo)
                    {
                        dynamicVideoUrls = SyncDecisionHelper.BuildDynamicVideoUrls(item);
                    }
```

Notes for the implementer:
- This is an in-method local block edit, NOT a whole-method replacement. `ProcessVideoList`'s signature, visibility, parameters, and everything from `// 处理核心逻辑` (line ~576) onward are verbatim — do NOT touch them.
- The `List<DouyinMergeVideoDto> dynamicVideoUrls = new List<DouyinMergeVideoDto>();` declaration stays: when `config.DownDynamicVideo` is false, `dynamicVideoUrls` remains an empty list and the subsequent `if (dynamicVideoUrls.Count > 0)` behaves identically to the original.
- The 20-space block indent must match the surrounding `else`-branch code exactly.
- Do NOT touch the call site of `ProcessVideoList` (`:421`).

- [ ] **Step 3: Build — verify 0 errors**

Run: `DOTNET_ROLL_FORWARD=LatestMajor dotnet build dy.net.csproj`
Expected: `Build succeeded. 0 Error(s)`.

- [ ] **Step 4: Run the existing suite — verify still green**

Run: `DOTNET_ROLL_FORWARD=LatestMajor dotnet test tests/dy.net.Tests/dy.net.Tests.csproj`
Expected: `Passed!  - Failed: 0` — **86 passed** (no new tests yet; the extraction must not break the existing golden masters).

- [ ] **Step 5: Commit**

Stage ONLY the two files — explicit paths, never `git add -A`:

```bash
git add utils/SyncDecisionHelper.cs job/DouyinBasicSyncJob.cs
git -c user.name='Claude Code' -c user.email='mjgenab@gmail.com' commit -m "$(cat <<'EOF'
refactor(job): extract dynamic-video URL build to SyncDecisionHelper

Move the dynamic-video URL build block (Images → DynamicVideo.BitRate
→ first PlayAddr.UrlList URL with the .../aweme/v1/play prefix →
DouyinMergeVideoDto) out of DouyinBasicSyncJob.ProcessVideoList into a
pure SyncDecisionHelper.BuildDynamicVideoUrls. The job keeps a thin
shell: the config.DownDynamicVideo switch and all downstream
ProcessDynamicVideo / merge / NFO I/O orchestration stay.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 2: Characterization tests for `BuildDynamicVideoUrls`

**Files:**
- Modify: `tests/dy.net.Tests/SyncDecisionHelperTests.cs` (append a new section before the class-closing `}`)

Golden-master tests pinning the helper's CURRENT behavior. The helper is a verbatim port, so first-run values ARE the golden values.

- [ ] **Step 1: Append the test section**

In `tests/dy.net.Tests/SyncDecisionHelperTests.cs`, insert the following block immediately after the last test method (`PickAuthorAvatarUrl_BothNull_ReturnsNull` — its closing `}`) and before the class-closing `}`:

```csharp

        // ---- BuildDynamicVideoUrls ----
        // pin: current behavior, not aspirational

        private const string PlayPrefix = "https://www.douyin.com/aweme/v1/play";

        private static PlayAddr DynPlayAddr(int height, int width, params string[] urls)
            => new PlayAddr { Height = height, Width = width, UrlList = urls.ToList() };

        private static VideoBitRate DynBitRate(PlayAddr playAddr)
            => new VideoBitRate { PlayAddr = playAddr };

        private static Video DynVideo(params VideoBitRate[] bitRates)
            => new Video { BitRate = bitRates.ToList() };

        private static ImageItemInfo DynImage(Video dynamicVideo)
            => new ImageItemInfo { DynamicVideo = dynamicVideo };

        private static Aweme AwemeWithImages(params ImageItemInfo[] images)
            => new Aweme { Images = images.ToList() };

        [Fact]
        public void BuildDynamicVideoUrls_ImagesNull_ReturnsEmptyList()
        {
            // Images == null → if-guard false → empty list (not null)
            var result = SyncDecisionHelper.BuildDynamicVideoUrls(new Aweme { Images = null });
            Assert.Empty(result);
        }

        [Fact]
        public void BuildDynamicVideoUrls_ImagesEmpty_ReturnsEmptyList()
        {
            var result = SyncDecisionHelper.BuildDynamicVideoUrls(AwemeWithImages());
            Assert.Empty(result);
        }

        [Fact]
        public void BuildDynamicVideoUrls_DynamicVideoNull_ImageContributesNothing()
        {
            // DynamicVideo == null → ?.BitRate?.Count short-circuits → image skipped
            var result = SyncDecisionHelper.BuildDynamicVideoUrls(
                AwemeWithImages(DynImage(null)));
            Assert.Empty(result);
        }

        [Fact]
        public void BuildDynamicVideoUrls_BitRateEmpty_ImageContributesNothing()
        {
            // BitRate present but empty → Count > 0 false → image skipped
            var result = SyncDecisionHelper.BuildDynamicVideoUrls(
                AwemeWithImages(DynImage(DynVideo())));
            Assert.Empty(result);
        }

        [Fact]
        public void BuildDynamicVideoUrls_PlayAddrNull_BitRateContributesNothing()
        {
            // PlayAddr == null → btv.PlayAddr?.UrlList short-circuits → targetUrl null → skipped
            var result = SyncDecisionHelper.BuildDynamicVideoUrls(
                AwemeWithImages(DynImage(DynVideo(DynBitRate(null)))));
            Assert.Empty(result);
        }

        [Fact]
        public void BuildDynamicVideoUrls_MatchingUrl_BuildsDtoWithPathAndPlayAddrDimensions()
        {
            var url = PlayPrefix + "/abc";
            var item = AwemeWithImages(DynImage(DynVideo(
                DynBitRate(DynPlayAddr(1280, 720, url)))));
            var result = SyncDecisionHelper.BuildDynamicVideoUrls(item);
            var dto = Assert.Single(result);
            Assert.Equal(url, dto.Path);
            Assert.Equal(1280, dto.Height);
            Assert.Equal(720, dto.Width);
        }

        [Fact]
        public void BuildDynamicVideoUrls_UrlListMixed_PicksFirstPlayPrefixUrl()
        {
            // FirstOrDefault(predicate): non-prefix URL skipped, first matching URL taken
            var match1 = PlayPrefix + "/1";
            var match2 = PlayPrefix + "/2";
            var item = AwemeWithImages(DynImage(DynVideo(
                DynBitRate(DynPlayAddr(100, 100, "https://other.com/a", match1, match2)))));
            var result = SyncDecisionHelper.BuildDynamicVideoUrls(item);
            var dto = Assert.Single(result);
            Assert.Equal(match1, dto.Path);
        }

        [Fact]
        public void BuildDynamicVideoUrls_MultipleImagesAndBitRates_CollectsEachMatchInEncounterOrder()
        {
            var a = PlayPrefix + "/a";
            var b = PlayPrefix + "/b";
            var c = PlayPrefix + "/c";
            var item = AwemeWithImages(
                DynImage(DynVideo(
                    DynBitRate(DynPlayAddr(10, 10, a)),
                    DynBitRate(DynPlayAddr(20, 20, b)))),
                DynImage(DynVideo(
                    DynBitRate(DynPlayAddr(30, 30, c)))));
            var result = SyncDecisionHelper.BuildDynamicVideoUrls(item);
            Assert.Collection(result,
                d => Assert.Equal(a, d.Path),
                d => Assert.Equal(b, d.Path),
                d => Assert.Equal(c, d.Path));
        }
```

Notes for the implementer:
- `Aweme`, `ImageItemInfo`, `Video`, `VideoBitRate`, `PlayAddr` are all in `dy.net.model.response`; `DouyinMergeVideoDto` is in `dy.net.model.dto`. Both namespaces are already imported at the top of the file (`using dy.net.model.response;`, `using dy.net.model.dto;`). `System.Linq` (`.ToList()`) resolves via ImplicitUsings. Do NOT add any `using` directives.
- The names `PlayPrefix`, `DynPlayAddr`, `DynBitRate`, `DynVideo`, `DynImage`, `AwemeWithImages` are new section-local helpers. Before inserting, scan the file for collisions: `grep -n "PlayPrefix\|DynPlayAddr\|DynBitRate\|DynVideo\|DynImage\|AwemeWithImages" tests/dy.net.Tests/SyncDecisionHelperTests.cs`. If any name already exists, rename the new helper consistently across all its uses and report the rename. (These names are expected to be free — prior sections used `AvatarImg`/`AwemeWithAvatars`, `Levels`, `CoverImg` etc. — but verify.)
- Model property names confirmed: `Aweme.Images` (`List<ImageItemInfo>`), `ImageItemInfo.DynamicVideo` (`Video`), `Video.BitRate` (`List<VideoBitRate>`), `VideoBitRate.PlayAddr` (`PlayAddr`), `PlayAddr.UrlList` (`List<string>`), `PlayAddr.Height`/`Width` (non-nullable `int`), `DouyinMergeVideoDto.Path`/`Height`/`Width`.
- Match the indentation of the surrounding test methods exactly (8-space method indent inside the class).
- Do NOT add a test for the `?? 1920`/`?? 1080` dead-code fallback — it is unreachable and cannot be triggered by any constructible input (see spec "Quirk").
- Do NOT modify any existing test or the helper.

- [ ] **Step 2: Run the new section — verify all 8 pass**

Run: `DOTNET_ROLL_FORWARD=LatestMajor dotnet test tests/dy.net.Tests/dy.net.Tests.csproj --filter "FullyQualifiedName~BuildDynamicVideoUrls"`
Expected: `Passed!  - Failed: 0` — **8 passed**.

If any fails: the helper is a verbatim port, so a failure means the test input was mis-traced. Re-trace by hand against the helper logic; fix the test input/expectation. Do NOT modify the helper. Never weaken an assertion.

- [ ] **Step 3: Run the full suite — verify 94 green**

Run: `DOTNET_ROLL_FORWARD=LatestMajor dotnet test tests/dy.net.Tests/dy.net.Tests.csproj`
Expected: `Passed!  - Failed: 0` — **94 passed** (86 + 8).

- [ ] **Step 4: Commit**

Stage ONLY the test file:

```bash
git add tests/dy.net.Tests/SyncDecisionHelperTests.cs
git -c user.name='Claude Code' -c user.email='mjgenab@gmail.com' commit -m "$(cat <<'EOF'
test: golden-master tests pinning BuildDynamicVideoUrls

8 characterization [Fact]s: Images null/empty → empty list,
DynamicVideo null and empty BitRate skip the image, null PlayAddr
skips the bitrate, a matching URL builds a DTO carrying PlayAddr
Height/Width, the .../aweme/v1/play prefix filter picks the first
matching URL, and multiple images × bitrates collect in encounter
order. Full suite 86 → 94.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 3: Update `tests/README.md` coverage doc

**Files:**
- Modify: `tests/README.md`

- [ ] **Step 1: Add `BuildDynamicVideoUrls` to the `SyncDecisionHelper` table row**

In `tests/README.md`, the "What is pinned" table has one row for `SyncDecisionHelper` (line 24). It currently ends with this item (the last before the closing ` |`):

```
`PickAuthorAvatarUrl` (头像 URL 选取：AvatarLarger 优先 → AvatarThumb 回落，各取 UrlList 首个 / 全空→null) |
```

Append `BuildDynamicVideoUrls` before the closing ` |`:

```
`PickAuthorAvatarUrl` (头像 URL 选取：AvatarLarger 优先 → AvatarThumb 回落，各取 UrlList 首个 / 全空→null), `BuildDynamicVideoUrls` (动态视频 URL 构建：遍历 Images→DynamicVideo.BitRate，取 PlayAddr.UrlList 首个 …/aweme/v1/play 前缀 URL 构造 DouyinMergeVideoDto / Images 空→空 list / 非匹配 URL 跳过 / 多 Image·BitRate 按序收集) |
```

- [ ] **Step 2: Update the "What is intentionally NOT covered" `DouyinBasicSyncJob` entry**

In the "## What is intentionally NOT covered (and why)" section, the first bullet (`**\`DouyinBasicSyncJob\` orchestration**`) lists extracted decision logic. It currently ends the `PickAuthorAvatarUrl` clause with this exact span (read the bullet first to confirm):

```
  的 NRE 路径保留不测) — all pinned (see table
```

Replace that span with (append a `BuildDynamicVideoUrls` clause after the `PickAuthorAvatarUrl` clause's close-paren):

```
  的 NRE 路径保留不测),
  `BuildDynamicVideoUrls` (`ProcessVideoList` 的动态视频 URL 构建段已抽出并 pinned；其
  `config.DownDynamicVideo` 开关、`else` 分支后续 `ProcessDynamicVideo`/`MergeMultipleVideosAsync`/
  `ProcessImageSetAndMergeToVideo` I/O 编排仍在 job 薄壳内、未覆盖；`?? 1920`/`?? 1080`
  不可达死代码保留不测) — all pinned (see table
```

The ` — all pinned (see table` suffix appears exactly once in the file, so this span is a unique match. Do NOT change the "Still uncovered:" list — `ProcessVideoList`'s remaining orchestration body is described by the new clause and need not be added to that list.

- [ ] **Step 3: Verify the doc reads correctly**

Run: `grep -n "BuildDynamicVideoUrls" tests/README.md`
Expected: 2 matches (the table row + the NOT-covered entry).

Run: `DOTNET_ROLL_FORWARD=LatestMajor dotnet test tests/dy.net.Tests/dy.net.Tests.csproj`
Expected: `Passed!  - Failed: 0` — **94 passed** (doc change must not affect the build/tests).

- [ ] **Step 4: Commit**

Stage `tests/README.md` and this plan file:

```bash
git add tests/README.md docs/superpowers/plans/2026-05-23-extract-builddynamicvideourls.md
git -c user.name='Claude Code' -c user.email='mjgenab@gmail.com' commit -m "$(cat <<'EOF'
docs: pin BuildDynamicVideoUrls coverage in tests/README

Also commits the ninth-slice implementation plan.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Final Steps (after all tasks)

- [ ] Push the commit chain to origin: `git push origin decompile/dy-sync-lib` — **do NOT merge, do NOT open a PR** (standing constraint). This environment may print a misleading "User cancelled dialog" or a transient TLS handshake error (`GnuTLS, handshake failed`) — retry up to 3×; verify the true state with `git status -sb` (expect `## decompile/dy-sync-lib...origin/decompile/dy-sync-lib` with no `ahead`).
- [ ] Update project memory (`project-dysync-security-hardening.md`): ninth slice done, `SyncDecisionHelper` now 13 pure methods, `SyncDecisionHelperTests` 77 cases, full suite 94 green, branch head = new push SHA.

---

## Self-Review

**Spec coverage:**
- `SyncDecisionHelper.BuildDynamicVideoUrls` (verbatim port, `config.DownDynamicVideo &&` dropped from the `if`) → Task 1 Step 1. ✓
- Thin `ProcessVideoList` block, `DownDynamicVideo` gate retained in the shell, control-flow equivalent → Task 1 Step 2. ✓
- `// 当需要下载动态视频时…` comment moved into the helper, `//处理多个视频…` kept in the shell → Task 1 Steps 1-2. ✓
- "Quirk" — `?? 1920`/`?? 1080` unreachable dead code preserved verbatim, not tested → Task 1 Step 1 doc comment + note, Task 2 Step 1 note. ✓
- 8 characterization `[Fact]`s (Images null, Images empty, DynamicVideo null, BitRate empty, PlayAddr null, matching URL builds DTO, mixed UrlList prefix filter, multiple images×bitrates order) → Task 2 Step 1. ✓
- `tests/README.md` updates (table row + NOT-covered clause) → Task 3. ✓
- Build/test via `DOTNET_ROLL_FORWARD=LatestMajor`, explicit `git add <path>`, push not merge → all task steps + Final Steps. ✓

**Placeholder scan:** No TBD/TODO; every code step shows complete code; every command shows expected output. ✓

**Type consistency:** `BuildDynamicVideoUrls(Aweme item)` returning `List<DouyinMergeVideoDto>` — identical across Task 1 (helper, job call) and Task 2 (8 test calls). Test helpers `DynPlayAddr(int,int,params string[])→PlayAddr`, `DynBitRate(PlayAddr)→VideoBitRate`, `DynVideo(params VideoBitRate[])→Video`, `DynImage(Video)→ImageItemInfo`, `AwemeWithImages(params ImageItemInfo[])→Aweme` are consistent across all uses. `DouyinMergeVideoDto.Path`/`Height`/`Width` match the model. ✓

**Test trace check:**
- ImagesNull — `Images=null` → `item.Images != null` false → empty list. ✓
- ImagesEmpty — `Images=[]` → `Count > 0` false → empty list. ✓
- DynamicVideoNull — `img.DynamicVideo?.BitRate?.Count` = null → `null > 0` false → image skipped → empty. ✓
- BitRateEmpty — `DynVideo()` → `BitRate=[]` → `0 > 0` false → image skipped → empty. ✓
- PlayAddrNull — `DynBitRate(null)` → `btv.PlayAddr?.UrlList` = null → `targetUrl` null → skipped → empty. ✓
- MatchingUrl — `UrlList=[PlayPrefix+"/abc"]`, `StartsWith` true → `targetUrl` = the URL; `Height=1280`,`Width=720` non-null ints → DTO `{Path=url,1280,720}`; `Assert.Single` → one DTO. ✓
- UrlListMixed — `["https://other.com/a", PlayPrefix+"/1", PlayPrefix+"/2"]`; `FirstOrDefault(StartsWith)` skips index 0, returns `PlayPrefix+"/1"` → `Path` = match1. ✓
- MultipleImagesAndBitRates — image1 bitrate a,b; image2 bitrate c → loop order a,b,c → `Assert.Collection` a,b,c. ✓
