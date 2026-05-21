# Extract Cover Decision Logic Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Extract the pure logic of both `DouyinBasicSyncJob.DownVideoCover` overloads into two new `SyncDecisionHelper` methods — `PickCoverUrl` (cover-URL priority cascade) and `BuildCoverPosterPath` (poster path derivation) — leaving the HTTP/FS I/O in two thin non-virtual job shells, and pin the new helpers with 9 golden-master tests.

**Architecture:** Same shape as the prior five slices. The two `DownVideoCover` overloads each mix pure decision logic with I/O; only the pure pocket moves to the static `SyncDecisionHelper`. The job keeps the `CloseNfo`/blank guards, `File.Exists`, `DownloadAsync`, and the inner-overload call. `BuildCoverPosterPath` lifts the `VideoType` abstract property to a `videoType` parameter (same pattern as `IsSyncLimitReached`/`BuildVideoFileName`). Both overloads are non-virtual with zero subclass overrides, so only `DouyinBasicSyncJob.cs` changes among job files.

**Tech Stack:** .NET 8 (build via `DOTNET_ROLL_FORWARD=LatestMajor` on local SDK 10), xUnit, existing `dy.net.Tests` project.

**Spec:** `docs/superpowers/specs/2026-05-21-extract-cover-decision-logic-design.md`

**Pre-conditions:**
- Branch `decompile/dy-sync-lib`, working tree clean, HEAD = `e3f8e06` (spec commit).
- Baseline test count: `dy.net.Tests` = **65 green**, filtered `SyncDecisionHelperTests` = **48 green**.
- `utils/SyncDecisionHelper.cs` is 192 lines; already holds 8 methods; `BuildVideoSaveFolderCandidates` closes on **line 190**; the class-closing `}` is **line 191**; insert the two new methods between them.
- `tests/dy.net.Tests/SyncDecisionHelperTests.cs` is 627 lines; single class organized by `// ---- <Method> ----` sections; the `BuildVideoSaveFolderCandidates` section is last, ending line 625; the class-closing `}` is **line 626**.
- `job/DouyinBasicSyncJob.cs`: `DownVideoCover(Aweme,...)` is at **lines 1174-1199**; `DownVideoCover(string,...)` is at **lines 1297-1320**. Both are non-virtual; `git grep` confirms zero subclass overrides. Call sites `:865`, `:931`, `:1119` and the inner call `:1198` stay untouched.
- `dy.net.csproj` has `<ImplicitUsings>enable</ImplicitUsings>` (net8.0); `System.IO` (`Path`) is globally imported. `DouyinCollectCate`, `Aweme`, `VideoTypeEnum` are already referenced by existing `SyncDecisionHelper` methods — no new `using` needed.

---

## File Structure

| File | Action | Responsibility |
|------|--------|----------------|
| `utils/SyncDecisionHelper.cs` | Modify (append before line 191 class close) | Add `PickCoverUrl(DouyinCollectCate, Aweme)` and `BuildCoverPosterPath(VideoTypeEnum, string)` static methods (verbatim move; `VideoType` → `videoType` for the second). |
| `job/DouyinBasicSyncJob.cs` | Modify (lines 1174-1199 and 1297-1320) | Replace both `DownVideoCover` overload bodies with delegate shells that retain the I/O. Signatures/visibility/XML docs unchanged. |
| `tests/dy.net.Tests/SyncDecisionHelperTests.cs` | Modify | Append `// ---- PickCoverUrl ----` (6 `[Fact]`) and `// ---- BuildCoverPosterPath ----` (3 `[Fact]`) sections before the final class close. |
| `tests/README.md` | Modify | Extend the `SyncDecisionHelper` row; update the `DouyinBasicSyncJob` "NOT covered" bullet. |
| `docs/superpowers/plans/2026-05-21-extract-cover-decision-logic.md` | This file | Tracked in source for review. |

---

## Task 1: Extract both helpers + add job-side delegate shells

**Files:**
- Modify: `utils/SyncDecisionHelper.cs` (append before class close on line 191)
- Modify: `job/DouyinBasicSyncJob.cs` (lines 1174-1199 and 1297-1320)
- No test changes — relies on the existing 65-test suite as the regression safety net.

- [ ] **Step 1.1: Verify baseline is green**

Run:
```bash
DOTNET_ROLL_FORWARD=LatestMajor dotnet test tests/dy.net.Tests/dy.net.Tests.csproj --nologo
```
Expected last line: `已通过! - 失败:     0，通过:    65，已跳过:     0，总计:    65`

If not 65 green, STOP — the working tree has unexpected state; investigate before proceeding.

- [ ] **Step 1.2: Append `PickCoverUrl` and `BuildCoverPosterPath` to `SyncDecisionHelper`**

In `utils/SyncDecisionHelper.cs`, locate the closing `}` of `BuildVideoSaveFolderCandidates` (line 190) and the class-closing `}` (line 191). Insert the following block **between them** — one blank line above `PickCoverUrl`, one blank line between the two methods, no blank line between `BuildCoverPosterPath` and the class-closing `}`:

```csharp

        /// <summary>
        /// 从 DouyinBasicSyncJob.DownVideoCover(Aweme,...) 抽出的纯封面 URL 选取逻辑（无 I/O）。
        /// 行为逐字保留：cate 非 null → MixInfo → Video（LastOrDefault）→ Music 三级兜底；
        /// cate == null → Video（FirstOrDefault），空白则回落 Images[0].DynamicVideo.Cover。
        /// 注意 cate 分支对 item.Video/Cover 无 ?. 空安全（与非-cate 分支不对称）——既有行为，
        /// 逐字保留；分支内的中文注释与代码实际顺序不符，亦逐字保留不修。
        /// 由特征化测试 SyncDecisionHelperTests 锁定当前行为。
        /// </summary>
        public static string PickCoverUrl(DouyinCollectCate cate, Aweme item)
        {
            // 定义封面URL变量
            string coverUrl;

            // 按照优先级获取封面URL
            if (cate is not null)
            {
                // cate不为空时：优先MixInfo封面 → 其次Music高清封面 → 最后Video封面
                coverUrl = item.MixInfo?.CoverUrl?.UrlList?.FirstOrDefault()
                           ?? item.Video.Cover.UrlList?.LastOrDefault()
                           ?? item.Music?.CoverHd?.UrlList?.FirstOrDefault();
            }
            else
            {
                // cate为空时：只取Video封面
                coverUrl = item.Video?.Cover?.UrlList?.FirstOrDefault();
                if (string.IsNullOrWhiteSpace(coverUrl))
                {
                    coverUrl = item.Images?.FirstOrDefault()?.DynamicVideo?.Cover?.UrlList?.FirstOrDefault();
                }
            }
            return coverUrl;
        }

        /// <summary>
        /// 从 DouyinBasicSyncJob.DownVideoCover(string,...) 抽出的纯海报路径派生逻辑（无 I/O）。
        /// 行为逐字保留：dy_mix/dy_series → 同目录 "poster.jpg"；其余 → "{无后缀原名}-poster.jpg"。
        /// 抽象属性 VideoType 提升为 videoType 入参。File.Exists / DownloadAsync 的 I/O 留在 job。
        /// 由特征化测试 SyncDecisionHelperTests 锁定当前行为。
        /// </summary>
        public static string BuildCoverPosterPath(VideoTypeEnum videoType, string savePath)
        {
            string directoryPath = Path.GetDirectoryName(savePath); // 获取文件所在目录，
            string newFileName = "poster.jpg";
            if (videoType != VideoTypeEnum.dy_mix && videoType != VideoTypeEnum.dy_series)
            {
                string fileNameWithoutExt = Path.GetFileNameWithoutExtension(savePath); // 获取无后缀的原文件名，
                newFileName = $"{fileNameWithoutExt}-poster.jpg"; // 拼接新文件名，
            }

            var coverSavePath = Path.Combine(directoryPath, newFileName);
            return coverSavePath;
        }
```

Notes on the move:
- `PickCoverUrl`: the `string coverUrl;` declaration and the entire `if/else` block are taken **verbatim** from `job/DouyinBasicSyncJob.cs:1177-1196`. Every expression, `??` chain, `FirstOrDefault`/`LastOrDefault`, and Chinese comment (`// 定义封面URL变量`, `// 按照优先级获取封面URL`, `// cate不为空时：...`, `// cate为空时：只取Video封面`) is preserved exactly — including the comment on the `cate is not null` branch that is factually wrong about the order (the code does MixInfo → Video → Music). Do NOT "fix" that comment. No token substitutions.
- `BuildCoverPosterPath`: lines `job/DouyinBasicSyncJob.cs:1303-1311` are taken verbatim — including the trailing Chinese comments (`// 获取文件所在目录，`, `// 获取无后缀的原文件名，`, `// 拼接新文件名，`) and the local variable `coverSavePath` (do NOT inline it into `return Path.Combine(...)`). The only token substitution is `VideoType` → `videoType`.
- Indentation: method declarations at 8 spaces, bodies at 12, nested blocks step in by 4.
- `using` directives: do NOT add any. `Path` is in implicit `System.IO`; `DouyinCollectCate`/`Aweme`/`VideoTypeEnum` are already used by existing methods in this file.

- [ ] **Step 1.3: Replace `DownVideoCover(Aweme,...)` body with a delegate shell**

In `job/DouyinBasicSyncJob.cs`, replace lines 1174-1199 — the entire `DownVideoCover(Aweme,...)` method (NOT any XML doc above it; this overload has no XML doc, it starts directly at the signature on line 1174):

```csharp
        protected async Task<string> DownVideoCover(Aweme item, string savePath, DouyinCookie cookie, DouyinCollectCate cate,AppConfig config)
        {
            if (config.CloseNfo) return string.Empty;
            // 定义封面URL变量
            string coverUrl;

            // 按照优先级获取封面URL
            if (cate is not null)
            {
                // cate不为空时：优先MixInfo封面 → 其次Music高清封面 → 最后Video封面
                coverUrl = item.MixInfo?.CoverUrl?.UrlList?.FirstOrDefault()
                           ?? item.Video.Cover.UrlList?.LastOrDefault()
                           ?? item.Music?.CoverHd?.UrlList?.FirstOrDefault();
            }
            else
            {
                // cate为空时：只取Video封面
                coverUrl = item.Video?.Cover?.UrlList?.FirstOrDefault();
                if (string.IsNullOrWhiteSpace(coverUrl))
                {
                    coverUrl = item.Images?.FirstOrDefault()?.DynamicVideo?.Cover?.UrlList?.FirstOrDefault();
                }
            }
            // 调用下载封面的方法
            return await DownVideoCover(coverUrl, savePath, cookie, config);
        }
```

with the delegate shell:

```csharp
        protected async Task<string> DownVideoCover(Aweme item, string savePath, DouyinCookie cookie, DouyinCollectCate cate,AppConfig config)
        {
            if (config.CloseNfo) return string.Empty;
            var coverUrl = SyncDecisionHelper.PickCoverUrl(cate, item);
            // 调用下载封面的方法
            return await DownVideoCover(coverUrl, savePath, cookie, config);
        }
```

Constraints:
- The signature is **unchanged**: `protected async Task<string>`, same 5 parameters in the same order, including the `cate,AppConfig` spacing (no space after the comma — keep it verbatim).
- The `if (config.CloseNfo) return string.Empty;` guard and the `// 调用下载封面的方法` comment + `return await DownVideoCover(coverUrl, savePath, cookie, config);` line stay verbatim.
- Do NOT touch the call sites `ProcessSingleVideo:865` and `ProcessDynamicVideo:931`.

- [ ] **Step 1.4: Replace `DownVideoCover(string,...)` body with a delegate shell**

In `job/DouyinBasicSyncJob.cs`, the `DownVideoCover(string,...)` overload has an XML doc block immediately above it (`/// <summary>` ... `/// <returns>`) — leave that XML doc untouched. Replace the method body — the original (lines 1297-1320):

```csharp
        private async Task<string> DownVideoCover(string coverUrl, string savePath, DouyinCookie cookie,AppConfig config)
        {
            if (config.CloseNfo) return string.Empty;
            if (string.IsNullOrWhiteSpace(coverUrl)) return string.Empty;
            if (string.IsNullOrWhiteSpace(savePath)) return string.Empty;

            string directoryPath = Path.GetDirectoryName(savePath); // 获取文件所在目录，
            string newFileName = "poster.jpg";
            if (VideoType != VideoTypeEnum.dy_mix && VideoType != VideoTypeEnum.dy_series)
            {
                string fileNameWithoutExt = Path.GetFileNameWithoutExtension(savePath); // 获取无后缀的原文件名，
                newFileName = $"{fileNameWithoutExt}-poster.jpg"; // 拼接新文件名，
            }

            var coverSavePath = Path.Combine(directoryPath, newFileName);


            // 如果封面文件不存在，则下载
            if (!File.Exists(coverSavePath))
            {
                await douyinHttpClientService.DownloadAsync(coverUrl, coverSavePath, cookie.Cookies);
            }
            return coverSavePath;
        }
```

with the delegate shell:

```csharp
        private async Task<string> DownVideoCover(string coverUrl, string savePath, DouyinCookie cookie,AppConfig config)
        {
            if (config.CloseNfo) return string.Empty;
            if (string.IsNullOrWhiteSpace(coverUrl)) return string.Empty;
            if (string.IsNullOrWhiteSpace(savePath)) return string.Empty;

            var coverSavePath = SyncDecisionHelper.BuildCoverPosterPath(VideoType, savePath);


            // 如果封面文件不存在，则下载
            if (!File.Exists(coverSavePath))
            {
                await douyinHttpClientService.DownloadAsync(coverUrl, coverSavePath, cookie.Cookies);
            }
            return coverSavePath;
        }
```

Constraints:
- The signature is **unchanged**: `private async Task<string>`, same 4 parameters, including the `cookie,AppConfig` spacing — verbatim.
- The three `if (...) return string.Empty;` guards, the `// 如果封面文件不存在，则下载` comment, the `File.Exists` / `DownloadAsync` block, and `return coverSavePath;` stay verbatim.
- ⚠️ Keep **both** blank lines between the `var coverSavePath = ...` assignment and the `// 如果封面文件不存在，则下载` comment — the original has two consecutive blank lines there; preserve them verbatim (this is a behavior-irrelevant whitespace detail, but keeping it verbatim minimizes diff noise).
- Do NOT touch the call site `ProcessImageSetAndMergeToVideo:1119`.

- [ ] **Step 1.5: Build the solution**

Run:
```bash
DOTNET_ROLL_FORWARD=LatestMajor dotnet build dy.net.csproj --nologo
```
Expected: `已成功生成` / `Build succeeded` with **0 errors**. Warning count must not increase versus baseline.

If the build fails with an unresolved `MixInfo` / `Music` / `ImageInfo` / `Path` / `DouyinCollectCate` symbol inside the helper, that means a `using` is missing — but **do not add one** without first verifying. These types are already reachable via the existing `using` block in `utils/SyncDecisionHelper.cs` (`dy.net.model.dto`, `dy.net.model.entity`, `dy.net.model.response`) plus implicit `System.IO`. If the build still fails, STOP and report.

- [ ] **Step 1.6: Run the full test suite**

Run:
```bash
DOTNET_ROLL_FORWARD=LatestMajor dotnet test tests/dy.net.Tests/dy.net.Tests.csproj --nologo
```
Expected: `通过:    65` — exactly the same count as Step 1.1. Zero failures.

The existing characterization tests do not test `PickCoverUrl`/`BuildCoverPosterPath` directly; their staying green is the regression signal that the extraction did not break compilation or unrelated paths. The new helpers' behavior is pinned in Task 2.

- [ ] **Step 1.7: Verify only the one job file changed**

Run:
```bash
git diff --stat
```
Expected: exactly 2 files modified — `utils/SyncDecisionHelper.cs` and `job/DouyinBasicSyncJob.cs`. No other job file may appear (no subclass file, since both overloads are non-virtual with zero overrides).

- [ ] **Step 1.8: Commit**

```bash
git add utils/SyncDecisionHelper.cs job/DouyinBasicSyncJob.cs
git -c user.name='Claude Code' -c user.email='mjgenab@gmail.com' commit -m "$(cat <<'EOF'
refactor(job): extract cover decision logic to SyncDecisionHelper

把两个 DownVideoCover 重载的纯逻辑逐字搬到 SyncDecisionHelper：
PickCoverUrl（封面 URL 三级/二级兜底选取）与 BuildCoverPosterPath
（海报落地路径派生，VideoType 抽象属性提升为 videoType 入参）。
job 内两个重载收薄为委托，CloseNfo/空白守卫、File.Exists、
DownloadAsync、内层重载调用等 I/O 全部留在 job。

两个 DownVideoCover 重载非 virtual、全仓零子类 override，故仅
DouyinBasicSyncJob.cs 一个 job 文件改动；3 个调用点不动。

行为零变更：cate 分支 MixInfo→Video(Last)→Music 与非-cate
Video(First)→Images 兜底、mix/series 的 poster.jpg 分支逐字保留；
cate 分支对 Video/Cover 无空安全的既有 quirk 与错误注释一并保留。

现有 65 个测试全绿；PickCoverUrl/BuildCoverPosterPath 的特征化
测试在下一个 commit 加上。

Spec: docs/superpowers/specs/2026-05-21-extract-cover-decision-logic-design.md

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

Verify the commit:
```bash
git log -1 --stat
```
Expected: 2 files changed; `utils/SyncDecisionHelper.cs` +~45 lines, `job/DouyinBasicSyncJob.cs` ~-30/+4 lines.

---

## Task 2: Characterization tests for `PickCoverUrl` and `BuildCoverPosterPath`

**Files:**
- Modify: `tests/dy.net.Tests/SyncDecisionHelperTests.cs` (append before final class-closing `}` on line 626 — after the existing `BuildVideoSaveFolderCandidates` section)

These are golden-master tests — pin the **current** behavior. If a test fails during this task, the bug is in your test setup, not in the helpers (verbatim from the job, shipped in Task 1).

- [ ] **Step 2.1: Add the two new sections + 9 `[Fact]` tests**

Open `tests/dy.net.Tests/SyncDecisionHelperTests.cs`. Find the **last** `}` on line 626 (it closes class `SyncDecisionHelperTests`). The line immediately above it (625) is the close of the last `[Fact]` in the `BuildVideoSaveFolderCandidates` section. Insert the following block **before** the class-closing `}` (preserve one blank line between the previous Fact and the new section header):

```csharp

        // ---- PickCoverUrl ----

        // pin: current behavior, not aspirational

        private static ImageInfo CoverImg(params string[] urls)
            => new ImageInfo { UrlList = urls.ToList() };

        [Fact]
        public void PickCoverUrl_Cate_MixInfoCover_TakesMixInfoFirst()
        {
            var item = new Aweme
            {
                MixInfo = new MixInfo { CoverUrl = CoverImg("m1", "m2") },
                Video = new Video { Cover = CoverImg("v1") },
            };
            var url = SyncDecisionHelper.PickCoverUrl(new DouyinCollectCate(), item);
            Assert.Equal("m1", url);
        }

        [Fact]
        public void PickCoverUrl_Cate_NoMixInfo_TakesVideoCoverLast()
        {
            var item = new Aweme
            {
                MixInfo = null,
                Video = new Video { Cover = CoverImg("v1", "v2") },
            };
            var url = SyncDecisionHelper.PickCoverUrl(new DouyinCollectCate(), item);
            Assert.Equal("v2", url);
        }

        [Fact]
        public void PickCoverUrl_Cate_MixNullAndVideoCoverUrlsNull_TakesMusicCoverHd()
        {
            var item = new Aweme
            {
                MixInfo = null,
                Video = new Video { Cover = new ImageInfo { UrlList = null } },
                Music = new Music { CoverHd = CoverImg("mu1") },
            };
            var url = SyncDecisionHelper.PickCoverUrl(new DouyinCollectCate(), item);
            Assert.Equal("mu1", url);
        }

        [Fact]
        public void PickCoverUrl_NoCate_TakesVideoCoverFirst()
        {
            var item = new Aweme
            {
                Video = new Video { Cover = CoverImg("v1", "v2") },
            };
            var url = SyncDecisionHelper.PickCoverUrl(cate: null, item);
            Assert.Equal("v1", url);
        }

        [Fact]
        public void PickCoverUrl_NoCate_BlankVideoCover_FallsBackToImages()
        {
            var item = new Aweme
            {
                Video = new Video { Cover = CoverImg("") },
                Images = new List<ImageItemInfo>
                {
                    new ImageItemInfo { DynamicVideo = new Video { Cover = CoverImg("img1") } },
                },
            };
            var url = SyncDecisionHelper.PickCoverUrl(cate: null, item);
            Assert.Equal("img1", url);
        }

        [Fact]
        public void PickCoverUrl_NoCate_AllNull_ReturnsNull()
        {
            var item = new Aweme { Video = null, Images = null };
            var url = SyncDecisionHelper.PickCoverUrl(cate: null, item);
            Assert.Null(url);
        }

        // ---- BuildCoverPosterPath ----

        // pin: current behavior, not aspirational

        [Fact]
        public void BuildCoverPosterPath_Mix_UsesPlainPosterJpg()
        {
            var path = SyncDecisionHelper.BuildCoverPosterPath(VideoTypeEnum.dy_mix, "/data/v/123.mp4");
            var expected = Path.Combine(Path.GetDirectoryName("/data/v/123.mp4"), "poster.jpg");
            Assert.Equal(expected, path);
        }

        [Fact]
        public void BuildCoverPosterPath_Series_UsesPlainPosterJpg()
        {
            var path = SyncDecisionHelper.BuildCoverPosterPath(VideoTypeEnum.dy_series, "/data/v/123.mp4");
            var expected = Path.Combine(Path.GetDirectoryName("/data/v/123.mp4"), "poster.jpg");
            Assert.Equal(expected, path);
        }

        [Fact]
        public void BuildCoverPosterPath_OtherType_UsesFileNamePrefixedPoster()
        {
            var path = SyncDecisionHelper.BuildCoverPosterPath(VideoTypeEnum.dy_collects, "/data/v/123.mp4");
            var dir = Path.GetDirectoryName("/data/v/123.mp4");
            var nameNoExt = Path.GetFileNameWithoutExtension("/data/v/123.mp4");
            var expected = Path.Combine(dir, $"{nameNoExt}-poster.jpg");
            Assert.Equal(expected, path);
        }
```

Notes:
- The section-local helper `CoverImg(params string[] urls)` builds an `ImageInfo` with a populated `UrlList`. For the one case needing a `null` `UrlList` (test #3), `new ImageInfo { UrlList = null }` is written inline. The name `CoverImg` is deliberately distinct from every existing builder in the file (`StdAweme`/`Ck`/`Br`/`AwemeWith*`/`Cate`/`FolderCookie`/`FolderAweme`) to avoid a duplicate-member compile error.
- Tests #1–#3 (cate branch) pass `new DouyinCollectCate()` — any non-null `cate` selects the cate branch; tests #4–#6 pass `cate: null` (named argument) for the non-cate branch.
- Test #1: MixInfo wins via `??` short-circuit; `Video.Cover` is still given a value so the test does not depend on `??` short-circuit subtlety.
- Test #2 pins that the cate branch uses `LastOrDefault()` on the Video cover (expects `"v2"`); test #4 pins that the non-cate branch uses `FirstOrDefault()` (expects `"v1"`).
- Test #3: `Video.Cover` is non-null but its `UrlList` is null, so `item.Video.Cover.UrlList?.LastOrDefault()` is `null` and the cascade falls through to `Music.CoverHd`. The cate branch does NOT null-guard `item.Video`/`item.Video.Cover`, so `Video.Cover` must be a real object — that is why it is set.
- Test #5: `CoverImg("")` yields `UrlList = [""]`; `FirstOrDefault()` returns `""`, which is whitespace, so the cascade falls back to `Images`.
- `BuildCoverPosterPath` expected paths are computed live with `Path.Combine` / `Path.GetDirectoryName` / `Path.GetFileNameWithoutExtension` — not hardcoded — so the test does not bake in a platform path separator (same approach as the `BuildVideoSaveFolderCandidates` section).
- `MixInfo`, `Music`, `Video`, `ImageInfo`, `ImageItemInfo`, `Aweme` are all in `dy.net.model.response`; `DouyinCollectCate` in `dy.net.model.entity`; `VideoTypeEnum` in `dy.net.model.dto`; `List<>` from implicit `System.Collections.Generic`. All four namespaces are already imported at the top of the file (lines 1-4) plus implicit usings. No new `using` needed.

- [ ] **Step 2.2: Run filtered tests**

Run:
```bash
DOTNET_ROLL_FORWARD=LatestMajor dotnet test tests/dy.net.Tests/dy.net.Tests.csproj --nologo --filter "FullyQualifiedName~SyncDecisionHelperTests"
```
Expected: `通过:    57` (48 prior + 9 new).

If any new test fails: STOP. Either a test-setup model-shape assumption is wrong (re-check the property names against `model/response/DouyinVideoInfoResponse.cs`: `MixInfo.CoverUrl`, `Music.CoverHd`, `Video.Cover` are all `ImageInfo`; `ImageInfo.UrlList` and `ImageItemInfo.UrlList` are `List<string>`; `ImageItemInfo.DynamicVideo` is `Video`), or a helper is not byte-for-byte identical with the original. Read the failure; do **not** weaken the assertion.

- [ ] **Step 2.3: Run the full suite**

Run:
```bash
DOTNET_ROLL_FORWARD=LatestMajor dotnet test tests/dy.net.Tests/dy.net.Tests.csproj --nologo
```
Expected: `通过:    74` (65 prior + 9 new), 0 failed, 0 skipped.

- [ ] **Step 2.4: Commit**

```bash
git add tests/dy.net.Tests/SyncDecisionHelperTests.cs
git -c user.name='Claude Code' -c user.email='mjgenab@gmail.com' commit -m "$(cat <<'EOF'
test: golden-master tests pinning PickCoverUrl + BuildCoverPosterPath

PickCoverUrl 6 个 [Fact]：cate 分支 MixInfo / Video(LastOrDefault) /
Music 三级兜底各一；非-cate 分支 Video(FirstOrDefault) / Images 回落 /
全空→null。BuildCoverPosterPath 3 个 [Fact]：dy_mix·dy_series→poster.jpg /
其余→{无后缀名}-poster.jpg，期望路径用 Path API 现场计算。

不写 NRE 路径测试——cate 分支对 Video/Cover 无空安全（既有 quirk）。

dy.net.Tests: 65 → 74 全绿。
SyncDecisionHelperTests filter: 48 → 57 全绿。

Spec: docs/superpowers/specs/2026-05-21-extract-cover-decision-logic-design.md

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

Verify:
```bash
git log -1 --stat
```
Expected: 1 file changed; `tests/dy.net.Tests/SyncDecisionHelperTests.cs` +~115 lines.

---

## Task 3: Update `tests/README.md` + push

**Files:**
- Modify: `tests/README.md` — the `SyncDecisionHelper` row in the `## What is pinned` table and the `DouyinBasicSyncJob` bullet under `## What is intentionally NOT covered`.

- [ ] **Step 3.1: Extend the `SyncDecisionHelper` row**

In `tests/README.md`, the `SyncDecisionHelper` table row currently ends (immediately before the closing pipe) with the `BuildVideoSaveFolderCandidates (...)` clause added in the prior slice.

Append `, \`PickCoverUrl\` (cate 三级兜底 MixInfo→Video(Last)→Music / 非-cate Video(First)→Images / 全空→null), \`BuildCoverPosterPath\` (dy_mix·dy_series→poster.jpg / 其余→{名}-poster.jpg)` to the end of the row, immediately before the closing pipe. The full updated line must read EXACTLY:

```markdown
| `SyncDecisionHelper` | `SyncDecisionHelperTests` | `GetNextCursor` (Cursor→MaxCursor→`"0"`, null-safe), `IsAwemeValid` (3-level null guard), `GetVideoTags` (per-level pick, missing→null), `IsSyncLimitReached` (cate 30-cap vs `BatchCount` cap, `OnlySyncNew` passthrough, `dy_follows` `!FullSync`, mix/series short-circuit), `BuildVideoEntity` (纯字段映射 / cate 标题覆盖 / `OnlyImgOrOnlyMp3` / `DyUserId` 分支 / `AuthorAvatarUrl` 回落 / `FileSize` 零回落), `PickBestVideoBitRate` (encoder=265 优先 H.265 + 回退 H.264 / 默认或 ≠265 仅 H.264 / 空或 null `UrlList` 跳过), `BuildVideoFileName` (custom_collect Format vs mp4 兜底 / mix-series 数字 episode 的 S01E{D2} / 默认 AwemeId.mp4 / cate 非 custom_collect 走默认), `BuildVideoSaveFolderCandidates` (primary = SavePath/sanitized-subFolder / collisionResolved 追加 _{AwemeId} / 空 Desc 走 AwemeId 兜底 / 非法字符经 Sanitize), `PickCoverUrl` (cate 三级兜底 MixInfo→Video(Last)→Music / 非-cate Video(First)→Images / 全空→null), `BuildCoverPosterPath` (dy_mix·dy_series→poster.jpg / 其余→{名}-poster.jpg) |
```

If the row's existing prefix differs in any way other than the appended clauses, keep the existing prefix verbatim and only append the new clauses before the closing pipe.

- [ ] **Step 3.2: Update the `DouyinBasicSyncJob` NOT-covered bullet**

In `tests/README.md`, the `DouyinBasicSyncJob orchestration` bullet under `## What is intentionally NOT covered (and why)` currently reads EXACTLY:

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

Replace that entire bullet with EXACTLY:

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
  6 个子类 `CreateSaveFolder` override 仍是子类业务实现，未覆盖),
  `PickCoverUrl`/`BuildCoverPosterPath` (两个 `DownVideoCover` 重载的纯逻辑
  ——封面 URL 选取 + 海报路径派生——已抽出并 pinned；其 `CloseNfo`/空白守卫、
  `File.Exists`/`DownloadAsync` I/O 编排仍在 job 薄壳内、未覆盖；cate 分支
  对 `Video`/`Cover` 无空安全的 NRE 路径保留不测) — all pinned (see table
  above). Still uncovered:
  `ProcessSingleVideo`/`ProcessDynamicVideo`/`ProcessImageSetAndMergeToVideo`
  orchestration bodies, `AutoDistinct`, `SaveVideos`, `DownAuthorAvatar`,
  `CleanupFailedVideos`, `HandleSyncCompletion` — all retain HTTP / FS / DB
  coupling and will be characterized as further seams are extracted in
  follow-up plans.
```

Diff intent: (1) add the `PickCoverUrl`/`BuildCoverPosterPath` clause to the "extracted so far" list; (2) remove `DownVideoCover,` from the "Still uncovered" list (its pure logic is now pinned; the residual I/O is noted in the new clause). `DownAuthorAvatar` stays in "Still uncovered". Everything else stays identical.

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
Expected: `通过:    74` (unchanged from Task 2; docs edit cannot affect tests).

- [ ] **Step 3.5: Commit**

```bash
git add tests/README.md docs/superpowers/plans/2026-05-21-extract-cover-decision-logic.md
git -c user.name='Claude Code' -c user.email='mjgenab@gmail.com' commit -m "$(cat <<'EOF'
docs: pin PickCoverUrl + BuildCoverPosterPath coverage in tests/README

把 PickCoverUrl / BuildCoverPosterPath 加入 SyncDecisionHelper
表格行；DouyinBasicSyncJob 的 NOT-covered 清单移除 DownVideoCover
（两重载纯逻辑已 pinned），并标注 CloseNfo/空白守卫、File.Exists/
DownloadAsync I/O 编排仍在 job 薄壳内未覆盖、NRE 路径保留不测。

落地本刀实现计划：
docs/superpowers/plans/2026-05-21-extract-cover-decision-logic.md

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

Verify:
```bash
git log --oneline -4
```
Expected (top → bottom):
1. `docs: pin PickCoverUrl + BuildCoverPosterPath coverage in tests/README`
2. `test: golden-master tests pinning PickCoverUrl + BuildCoverPosterPath`
3. `refactor(job): extract cover decision logic to SyncDecisionHelper`
4. `docs: spec for extracting cover decision logic`

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
- `dotnet test` reports **74 passed / 0 failed / 0 skipped**.
- `git status -sb` shows clean working tree and synced-with-origin.
- `utils/SyncDecisionHelper.cs` contains `PickCoverUrl` and `BuildCoverPosterPath` as its last two methods (after `BuildVideoSaveFolderCandidates`).
- `job/DouyinBasicSyncJob.cs`: both `DownVideoCover` overload bodies are delegate shells calling `SyncDecisionHelper.PickCoverUrl` / `SyncDecisionHelper.BuildCoverPosterPath`, with the I/O (`CloseNfo`/blank guards, inner call, `File.Exists`, `DownloadAsync`) retained. Both signatures and the second overload's XML doc unchanged.
- `tests/dy.net.Tests/SyncDecisionHelperTests.cs` contains the `// ---- PickCoverUrl ----` (6 `[Fact]`) and `// ---- BuildCoverPosterPath ----` (3 `[Fact]`) sections.
- `tests/README.md` row for `SyncDecisionHelper` mentions `PickCoverUrl` and `BuildCoverPosterPath`; the `DouyinBasicSyncJob` "NOT covered" bullet no longer lists `DownVideoCover` (but still lists `DownAuthorAvatar`).
- `git diff e3f8e06..HEAD --stat` shows changes confined to `utils/SyncDecisionHelper.cs`, `job/DouyinBasicSyncJob.cs`, `tests/dy.net.Tests/SyncDecisionHelperTests.cs`, `tests/README.md`, and this plan doc — no subclass job file touched.
- No merge, no PR.
