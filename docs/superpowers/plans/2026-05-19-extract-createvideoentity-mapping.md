# Extract CreateVideoEntity Pure Mapping Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Extract the pure `DouyinVideo` field projection inside `DouyinBasicSyncJob.CreateVideoEntity` into `SyncDecisionHelper.BuildVideoEntity`, lifting the two non-deterministic fields (`Id`, `SyncTime`) to parameters; the NFO file-write side effect and the `DynamicVideos`/NFO `if/else` stay verbatim in the job.

**Architecture:** Behavior-preserving structural move. The `new DouyinVideo { … }` initializer plus the cate-name title override move verbatim into a new `public static` method on the existing `SyncDecisionHelper`; the abstract `VideoType` becomes a `videoType` param; `IdGener.GetLong().ToString()` and `DateTime.Now` become `id`/`syncTime` params evaluated at the job call site. The job delegates and keeps the `DynamicVideos`/`NfoFileGenerator` `if/else` block unchanged. Pinned by golden-master characterization tests.

**Tech Stack:** C# / .NET 8 (`DOTNET_ROLL_FORWARD=LatestMajor`, local SDK 10), xUnit 2.9.2.

---

## Spec

`docs/superpowers/specs/2026-05-19-extract-createvideoentity-mapping-design.md`

## Verbatim original source (job/DouyinBasicSyncJob.cs:1385-1440)

```csharp
        private async Task<DouyinVideo> CreateVideoEntity(AppConfig config,
            DouyinCookie cookie, Aweme item, VideoBitRate bitRate, string savePath, string coverSavePath, string avatorPath, List<DouyinMergeVideoDto> dynamicVideos = null, DouyinCollectCate cate = null)
        {
            // 获取视频标签
            var (tag1, tag2, tag3) = GetVideoTags(item);
            var video = new DouyinVideo
            {
                ViedoType = VideoType,
                AwemeId = item.AwemeId,
                Author = item.Author?.Nickname,
                AuthorId = item.Author?.Uid,
                AuthorAvatar = avatorPath,
                AuthorAvatarUrl = item.Author.AvatarLarger?.UrlList?.FirstOrDefault() ?? item.Author.AvatarThumb?.UrlList?.FirstOrDefault(),
                CreateTime = DateTimeUtil.Convert10BitTimestamp(item.CreateTime),
                VideoTitle = string.IsNullOrWhiteSpace(item.Desc) ? $"{item.Author?.Nickname}-{item.CreateTime}" : item.Desc,
                //VideoTitleSimplify = VideoType == VideoTypeEnum.dy_follows? GetVideoSimplifyTitle(item):string.Empty,
                Id = IdGener.GetLong().ToString(),
                Resolution = $"{bitRate.PlayAddr.Width}×{bitRate.PlayAddr.Height}",
                FileSize = bitRate.PlayAddr.DataSize ?? 0,
                FileHash = bitRate.PlayAddr.FileHash,
                Tag1 = tag1,
                Tag2 = tag2,
                Tag3 = tag3,
                VideoUrl = bitRate.PlayAddr.UrlList?.FirstOrDefault(),
                VideoCoverUrl = item.Video.Cover.UrlList?.FirstOrDefault(),
                VideoSavePath = savePath,
                VideoCoverSavePath = coverSavePath,
                SyncTime = DateTime.Now,
                DyUserId = item.AuthorUserId == 0 ? item.Author?.Uid : item.AuthorUserId.ToString(),
                CookieId = cookie.Id,
                OnlyImgOrOnlyMp3 = string.IsNullOrWhiteSpace(savePath) && !config.DownImageVideo && (config.DownImage || config.DownMp3),
                CateId = cate?.Id,
                CateXId = cate?.XId,
            };
            if (cate != null && cate.CateType != VideoTypeEnum.dy_custom_collect)
            {
                video.VideoTitle = (string.IsNullOrWhiteSpace(item.Desc) ? cate.Name : $"[{cate.Name}]" + "_" + item.Desc) + "_" + item.MixInfo.Statis.CurrentEpisode;
            }
            if (dynamicVideos != null && dynamicVideos.Count > 0)
            {
                video.DynamicVideos = JsonConvert.SerializeObject(dynamicVideos);
            }
            else
            {
                if (cate != null && (VideoType == VideoTypeEnum.dy_mix || VideoType == VideoTypeEnum.dy_series))
                {
                    NfoFileGenerator.GenerateVideoNfoFile(config.CloseNfo, video, cate.Name);
                }
                else
                {
                    NfoFileGenerator.GenerateVideoNfoFile(config.CloseNfo, video);
                }
            }

            return video;
        }
```

## Type shapes (for test construction)

| Type | Members used |
|------|--------------|
| `DouyinVideo` (entity) | `Id` string, `DyUserId` string, `CookieId` string, `AwemeId` string, `SyncTime` DateTime, `CreateTime` DateTime, `VideoTitle` string, `Tag1/2/3` string, `VideoUrl` string, `VideoSavePath` string, `VideoCoverUrl` string, `VideoCoverSavePath` string, `Author` string, `AuthorId` string, `AuthorAvatar` string, `AuthorAvatarUrl` string, `FileHash` string, `FileSize` long, `Resolution` string, `ViedoType` VideoTypeEnum, `OnlyImgOrOnlyMp3` bool, `CateId` string, `CateXId` string |
| `Aweme` (response) | `Author` Author, `AuthorUserId` long, `AwemeId` string, `CreateTime` long, `Desc` string, `MixInfo` MixInfo, `Video` Video, `VideoTags` List\<VideoTagItem\> |
| `Author` | `AvatarLarger` ImageInfo, `AvatarThumb` ImageInfo, `Nickname` string, `Uid` string |
| `ImageInfo` | `UrlList` List\<string\> |
| `Video` | `Cover` ImageInfo |
| `VideoBitRate` | `PlayAddr` PlayAddr |
| `PlayAddr` | `Width` int, `Height` int, `DataSize` long?, `FileHash` string, `UrlList` List\<string\> |
| `MixInfo` | `Statis` MixStatis |
| `MixStatis` | `CurrentEpisode` int |
| `VideoTagItem` | `Level` int, `TagName` string |
| `DouyinCollectCate` (entity) | `Id` string, `Name` string, `XId` string, `CateType` VideoTypeEnum |
| `DouyinCookie` (entity) | `Id` string, `UserName` string |
| `AppConfig` | `DownImageVideo` bool, `DownImage` bool, `DownMp3` bool, `CloseNfo` bool |
| `DateTimeUtil.Convert10BitTimestamp(long)` | static, in `dy.net.utils`, returns local `DateTime` |

Notes carried from spec (pin as-is, do NOT "fix"):
- `item.Author.` (1397) and `item.Video.Cover.` (1409) are NOT null-conditional — tests supply non-null `Author`/`Video.Cover` (don't probe these unreachable NRE paths).
- `VideoTitle` fallback `$"{item.Author?.Nickname}-{item.CreateTime}"` uses the **raw long** `item.CreateTime`, not the converted `DateTime`.
- cate-noncustom branch dereferences `item.MixInfo.Statis.CurrentEpisode` — that test supplies them non-null.

---

### Task 1: Add `BuildVideoEntity` to SyncDecisionHelper and delegate from the job

**Files:**
- Modify: `utils/SyncDecisionHelper.cs` (add one method; add `using` for `DateTimeUtil`'s namespace is unnecessary — `DateTimeUtil` is already in `dy.net.utils`, same namespace as `SyncDecisionHelper`)
- Modify: `job/DouyinBasicSyncJob.cs:1385-1440` (replace method body with delegation)

- [ ] **Step 1: Add `BuildVideoEntity` to `SyncDecisionHelper`**

In `utils/SyncDecisionHelper.cs`, insert this method inside the class, immediately after the closing `}` of `IsSyncLimitReached` and before the class's closing `}` (one blank line before it, matching existing spacing):

```csharp
        /// <summary>
        /// 从 DouyinBasicSyncJob.CreateVideoEntity 抽出的纯字段映射。
        /// 行为逐字保留：VideoType→videoType；IdGener.GetLong()→id；DateTime.Now→syncTime。
        /// 不含 DynamicVideos 赋值与 NfoFileGenerator 文件写入（那段 if/else 留在 job）。
        /// 由特征化测试 SyncDecisionHelperTests 锁定当前行为。
        /// </summary>
        public static DouyinVideo BuildVideoEntity(VideoTypeEnum videoType, AppConfig config,
            DouyinCookie cookie, Aweme item, VideoBitRate bitRate, string savePath,
            string coverSavePath, string avatorPath, string id, DateTime syncTime,
            DouyinCollectCate cate)
        {
            // 获取视频标签
            var (tag1, tag2, tag3) = GetVideoTags(item);
            var video = new DouyinVideo
            {
                ViedoType = videoType,
                AwemeId = item.AwemeId,
                Author = item.Author?.Nickname,
                AuthorId = item.Author?.Uid,
                AuthorAvatar = avatorPath,
                AuthorAvatarUrl = item.Author.AvatarLarger?.UrlList?.FirstOrDefault() ?? item.Author.AvatarThumb?.UrlList?.FirstOrDefault(),
                CreateTime = DateTimeUtil.Convert10BitTimestamp(item.CreateTime),
                VideoTitle = string.IsNullOrWhiteSpace(item.Desc) ? $"{item.Author?.Nickname}-{item.CreateTime}" : item.Desc,
                //VideoTitleSimplify = VideoType == VideoTypeEnum.dy_follows? GetVideoSimplifyTitle(item):string.Empty,
                Id = id,
                Resolution = $"{bitRate.PlayAddr.Width}×{bitRate.PlayAddr.Height}",
                FileSize = bitRate.PlayAddr.DataSize ?? 0,
                FileHash = bitRate.PlayAddr.FileHash,
                Tag1 = tag1,
                Tag2 = tag2,
                Tag3 = tag3,
                VideoUrl = bitRate.PlayAddr.UrlList?.FirstOrDefault(),
                VideoCoverUrl = item.Video.Cover.UrlList?.FirstOrDefault(),
                VideoSavePath = savePath,
                VideoCoverSavePath = coverSavePath,
                SyncTime = syncTime,
                DyUserId = item.AuthorUserId == 0 ? item.Author?.Uid : item.AuthorUserId.ToString(),
                CookieId = cookie.Id,
                OnlyImgOrOnlyMp3 = string.IsNullOrWhiteSpace(savePath) && !config.DownImageVideo && (config.DownImage || config.DownMp3),
                CateId = cate?.Id,
                CateXId = cate?.XId,
            };
            if (cate != null && cate.CateType != VideoTypeEnum.dy_custom_collect)
            {
                video.VideoTitle = (string.IsNullOrWhiteSpace(item.Desc) ? cate.Name : $"[{cate.Name}]" + "_" + item.Desc) + "_" + item.MixInfo.Statis.CurrentEpisode;
            }
            return video;
        }
```

The `using` block at the top of `SyncDecisionHelper.cs` already imports `dy.net.model.dto`, `dy.net.model.entity`, `dy.net.model.response`, `Serilog`. `DouyinVideo` is in `dy.net.model.entity` (covered); `Aweme`/`VideoBitRate` in `dy.net.model.response` (covered); `AppConfig`/`DouyinCookie`/`DouyinCollectCate` resolve via the same imports already used by `IsSyncLimitReached`; `DateTimeUtil` is in `dy.net.utils` (same namespace, no using needed). Do NOT add any new `using`.

- [ ] **Step 2: Replace the job method body with a delegation**

In `job/DouyinBasicSyncJob.cs`, replace the entire body of `CreateVideoEntity` (the original block shown above, lines 1385-1440) with exactly:

```csharp
        private async Task<DouyinVideo> CreateVideoEntity(AppConfig config,
            DouyinCookie cookie, Aweme item, VideoBitRate bitRate, string savePath, string coverSavePath, string avatorPath, List<DouyinMergeVideoDto> dynamicVideos = null, DouyinCollectCate cate = null)
        {
            var video = SyncDecisionHelper.BuildVideoEntity(VideoType, config, cookie, item, bitRate,
                savePath, coverSavePath, avatorPath, IdGener.GetLong().ToString(), DateTime.Now, cate);
            if (dynamicVideos != null && dynamicVideos.Count > 0)
            {
                video.DynamicVideos = JsonConvert.SerializeObject(dynamicVideos);
            }
            else
            {
                if (cate != null && (VideoType == VideoTypeEnum.dy_mix || VideoType == VideoTypeEnum.dy_series))
                {
                    NfoFileGenerator.GenerateVideoNfoFile(config.CloseNfo, video, cate.Name);
                }
                else
                {
                    NfoFileGenerator.GenerateVideoNfoFile(config.CloseNfo, video);
                }
            }

            return video;
        }
```

Signature, visibility (`private`), `async Task<DouyinVideo>`, the three call sites (lines ~899/994/1171), and all 6 subclasses are unchanged. `using dy.net.utils;` is already present at the top of `DouyinBasicSyncJob.cs` (added in the prior slice), so `SyncDecisionHelper` resolves without a new using. The pre-existing CS1998 ("async method lacks await") is unchanged behavior — do not suppress or change it.

- [ ] **Step 3: Build to verify zero errors**

Run: `DOTNET_ROLL_FORWARD=LatestMajor dotnet build dy.net.csproj -nologo`
Expected: `Build succeeded.` with `0 Error(s)`. Warning count unchanged from before (CS1998 on `CreateVideoEntity` still present — that is pre-existing and acceptable).

- [ ] **Step 4: Run the existing test suite to verify nothing regressed**

Run: `DOTNET_ROLL_FORWARD=LatestMajor dotnet test tests/dy.net.Tests/dy.net.Tests.csproj -nologo`
Expected: PASS, `Passed!  - Failed: 0`, 37 tests (unchanged — no new tests yet; this proves the extraction did not break the existing pinned behavior).

- [ ] **Step 5: Commit**

```bash
cd "/mnt/EA7802167801E265/WorkSpace/Personal/dysync.net/mawanglin.dysync.net"
git add utils/SyncDecisionHelper.cs job/DouyinBasicSyncJob.cs
git -c user.name='Claude Code' -c user.email='mjgenab@gmail.com' commit -m "$(cat <<'EOF'
refactor(job): extract CreateVideoEntity pure mapping to SyncDecisionHelper.BuildVideoEntity

Verbatim move of the DouyinVideo field projection + cate-name title
override; VideoType lifted to a param, Id/SyncTime lifted to id/syncTime
params evaluated at the job call site. The DynamicVideos/NfoFileGenerator
if/else stays in the job unchanged. Signatures/visibility/call sites and
all 6 subclasses untouched. Build 0 errors, suite 37 green (behavior
unchanged; characterization tests added next task).

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
EOF
)"
```

---

### Task 2: Pin `BuildVideoEntity` with golden-master characterization tests

**Files:**
- Modify: `tests/dy.net.Tests/SyncDecisionHelperTests.cs` (append fixtures + Facts inside the existing `SyncDecisionHelperTests` class, before the class's closing `}`)

The file currently ends with the `IsSyncLimitReached_under_limit_mix_short_circuits_to_false` Fact, then `}` (class) then `}` (namespace). Insert the block below immediately **after** that last Fact's closing `}` and **before** the class's closing `}`.

- [ ] **Step 1: Add fixtures + characterization Facts**

```csharp

        // ---- BuildVideoEntity (golden-master: pins CreateVideoEntity's pure mapping) ----

        private static readonly DateTime FixedSync = new DateTime(2026, 5, 19, 12, 0, 0, DateTimeKind.Local);
        private const string FixedId = "ID-1";

        private static DouyinCookie Ck() => new DouyinCookie { Id = "ck1", UserName = "u" };

        private static Aweme StdAweme() => new Aweme
        {
            AwemeId = "aw1",
            AuthorUserId = 0,
            CreateTime = 1700000000,
            Desc = "hello",
            Author = new Author
            {
                Nickname = "nick",
                Uid = "uid9",
                AvatarLarger = new ImageInfo { UrlList = new List<string> { "large.jpg" } },
                AvatarThumb = new ImageInfo { UrlList = new List<string> { "thumb.jpg" } },
            },
            Video = new Video { Cover = new ImageInfo { UrlList = new List<string> { "cover.jpg" } } },
            VideoTags = new List<VideoTagItem>
            {
                new VideoTagItem { Level = 1, TagName = "t1" },
                new VideoTagItem { Level = 2, TagName = "t2" },
                new VideoTagItem { Level = 3, TagName = "t3" },
            },
            MixInfo = new MixInfo { Statis = new MixStatis { CurrentEpisode = 7 } },
        };

        private static VideoBitRate StdBitRate() => new VideoBitRate
        {
            PlayAddr = new PlayAddr
            {
                Width = 1920,
                Height = 1080,
                DataSize = 12345,
                FileHash = "fh",
                UrlList = new List<string> { "v1.mp4", "v2.mp4" },
            }
        };

        [Fact]
        public void BuildVideoEntity_maps_core_fields()
        {
            var v = SyncDecisionHelper.BuildVideoEntity(
                VideoTypeEnum.dy_collects, new AppConfig(), Ck(), StdAweme(), StdBitRate(),
                "save.mp4", "cover-save.jpg", "av-path", FixedId, FixedSync, null);

            Assert.Equal(VideoTypeEnum.dy_collects, v.ViedoType);
            Assert.Equal("aw1", v.AwemeId);
            Assert.Equal("nick", v.Author);
            Assert.Equal("uid9", v.AuthorId);
            Assert.Equal("av-path", v.AuthorAvatar);
            Assert.Equal("large.jpg", v.AuthorAvatarUrl);
            Assert.Equal(DateTimeUtil.Convert10BitTimestamp(1700000000), v.CreateTime);
            Assert.Equal("hello", v.VideoTitle);
            Assert.Equal("ID-1", v.Id);
            Assert.Equal("1920×1080", v.Resolution);
            Assert.Equal(12345L, v.FileSize);
            Assert.Equal("fh", v.FileHash);
            Assert.Equal("t1", v.Tag1);
            Assert.Equal("t2", v.Tag2);
            Assert.Equal("t3", v.Tag3);
            Assert.Equal("v1.mp4", v.VideoUrl);
            Assert.Equal("cover.jpg", v.VideoCoverUrl);
            Assert.Equal("save.mp4", v.VideoSavePath);
            Assert.Equal("cover-save.jpg", v.VideoCoverSavePath);
            Assert.Equal(FixedSync, v.SyncTime);
            Assert.Equal("uid9", v.DyUserId);
            Assert.Equal("ck1", v.CookieId);
            Assert.False(v.OnlyImgOrOnlyMp3);
            Assert.Null(v.CateId);
            Assert.Null(v.CateXId);
        }

        [Fact]
        public void BuildVideoEntity_title_falls_back_to_nickname_dash_createtime_when_desc_blank()
        {
            var a = StdAweme();
            a.Desc = "   ";
            var v = SyncDecisionHelper.BuildVideoEntity(
                VideoTypeEnum.dy_collects, new AppConfig(), Ck(), a, StdBitRate(),
                "save.mp4", "cs.jpg", "ap", FixedId, FixedSync, null);
            Assert.Equal("nick-1700000000", v.VideoTitle);
        }

        [Fact]
        public void BuildVideoEntity_cate_noncustom_overrides_title_with_desc()
        {
            var cate = new DouyinCollectCate
            {
                Id = "cid",
                XId = "cxid",
                Name = "MyCate",
                CateType = VideoTypeEnum.dy_mix,
            };
            var v = SyncDecisionHelper.BuildVideoEntity(
                VideoTypeEnum.dy_mix, new AppConfig(), Ck(), StdAweme(), StdBitRate(),
                "save.mp4", "cs.jpg", "ap", FixedId, FixedSync, cate);
            Assert.Equal("[MyCate]_hello_7", v.VideoTitle);
            Assert.Equal("cid", v.CateId);
            Assert.Equal("cxid", v.CateXId);
        }

        [Fact]
        public void BuildVideoEntity_cate_noncustom_overrides_title_when_desc_blank()
        {
            var a = StdAweme();
            a.Desc = null;
            var cate = new DouyinCollectCate
            {
                Id = "cid",
                XId = "cxid",
                Name = "MyCate",
                CateType = VideoTypeEnum.dy_series,
            };
            var v = SyncDecisionHelper.BuildVideoEntity(
                VideoTypeEnum.dy_series, new AppConfig(), Ck(), a, StdBitRate(),
                "save.mp4", "cs.jpg", "ap", FixedId, FixedSync, cate);
            Assert.Equal("MyCate_7", v.VideoTitle);
        }

        [Fact]
        public void BuildVideoEntity_OnlyImgOrOnlyMp3_true_when_savepath_blank_and_downmp3()
        {
            var cfg = new AppConfig { DownImageVideo = false, DownImage = false, DownMp3 = true };
            var v = SyncDecisionHelper.BuildVideoEntity(
                VideoTypeEnum.dy_collects, cfg, Ck(), StdAweme(), StdBitRate(),
                "", "cs.jpg", "ap", FixedId, FixedSync, null);
            Assert.True(v.OnlyImgOrOnlyMp3);
        }

        [Fact]
        public void BuildVideoEntity_OnlyImgOrOnlyMp3_false_when_downimagevideo_true()
        {
            var cfg = new AppConfig { DownImageVideo = true, DownImage = true, DownMp3 = true };
            var v = SyncDecisionHelper.BuildVideoEntity(
                VideoTypeEnum.dy_collects, cfg, Ck(), StdAweme(), StdBitRate(),
                "", "cs.jpg", "ap", FixedId, FixedSync, null);
            Assert.False(v.OnlyImgOrOnlyMp3);
        }

        [Fact]
        public void BuildVideoEntity_DyUserId_uses_AuthorUserId_when_nonzero()
        {
            var a = StdAweme();
            a.AuthorUserId = 42;
            var v = SyncDecisionHelper.BuildVideoEntity(
                VideoTypeEnum.dy_collects, new AppConfig(), Ck(), a, StdBitRate(),
                "save.mp4", "cs.jpg", "ap", FixedId, FixedSync, null);
            Assert.Equal("42", v.DyUserId);
        }

        [Fact]
        public void BuildVideoEntity_AuthorAvatarUrl_falls_back_to_thumb_when_larger_null()
        {
            var a = StdAweme();
            a.Author.AvatarLarger = null;
            var v = SyncDecisionHelper.BuildVideoEntity(
                VideoTypeEnum.dy_collects, new AppConfig(), Ck(), a, StdBitRate(),
                "save.mp4", "cs.jpg", "ap", FixedId, FixedSync, null);
            Assert.Equal("thumb.jpg", v.AuthorAvatarUrl);
        }

        [Fact]
        public void BuildVideoEntity_FileSize_zero_when_datasize_null()
        {
            var br = StdBitRate();
            br.PlayAddr.DataSize = null;
            var v = SyncDecisionHelper.BuildVideoEntity(
                VideoTypeEnum.dy_collects, new AppConfig(), Ck(), StdAweme(), br,
                "save.mp4", "cs.jpg", "ap", FixedId, FixedSync, null);
            Assert.Equal(0L, v.FileSize);
        }
```

These fixtures use `List<…>` and `DateTime` — the test project has `ImplicitUsings` enabled and `<Using Include="Xunit" />`, and the file already has `using dy.net.model.dto; using dy.net.model.entity; using dy.net.model.response; using dy.net.utils;`. `DateTimeUtil` resolves via `using dy.net.utils;`. Do NOT add new usings.

- [ ] **Step 2: Run the new tests to verify they pass (golden values pinned)**

Run: `DOTNET_ROLL_FORWARD=LatestMajor dotnet test tests/dy.net.Tests/dy.net.Tests.csproj -nologo`
Expected: PASS, `Passed!  - Failed: 0`, 46 tests (37 prior + 9 new `BuildVideoEntity_*`).

If any new assertion fails on first run, the asserted constant is wrong, NOT the helper — read the actual produced value, confirm it matches the verbatim original logic by inspection, then pin that observed value (golden-master rule, `tests/README.md`). Never weaken an assertion to make it pass.

- [ ] **Step 3: Commit**

```bash
cd "/mnt/EA7802167801E265/WorkSpace/Personal/dysync.net/mawanglin.dysync.net"
git add tests/dy.net.Tests/SyncDecisionHelperTests.cs
git -c user.name='Claude Code' -c user.email='mjgenab@gmail.com' commit -m "$(cat <<'EOF'
test: golden-master tests pinning SyncDecisionHelper.BuildVideoEntity

9 characterization Facts pin the extracted CreateVideoEntity pure
mapping: core field projection, VideoTitle desc-blank fallback,
cate-noncustom title override (with/without desc), OnlyImgOrOnlyMp3
both ways, DyUserId AuthorUserId branch, AuthorAvatarUrl thumb
fallback, FileSize null DataSize. Suite 46 green.

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
EOF
)"
```

---

### Task 3: Update tests/README.md pinned-coverage docs

**Files:**
- Modify: `tests/README.md`

- [ ] **Step 1: Extend the `SyncDecisionHelper` pinned row and the NOT-covered note**

In `tests/README.md`, find the `| \`SyncDecisionHelper\` | \`SyncDecisionHelperTests\` | … |` row and append to its coverage cell (after the existing `IsSyncLimitReached` clause), within the same cell:
`, \`BuildVideoEntity\` (DouyinVideo pure projection, VideoTitle desc-blank fallback, cate-noncustom title override, OnlyImgOrOnlyMp3, DyUserId AuthorUserId branch, AuthorAvatarUrl thumb fallback, FileSize null DataSize)`

Then find the "What is intentionally NOT covered" `DouyinBasicSyncJob` bullet (the one updated by the prior slice noting decision logic extracted). Replace its body so it reads (keep the existing list-item marker/style):
`DouyinBasicSyncJob: decision logic (SyncDecisionHelper, pinned) AND CreateVideoEntity's pure mapping (SyncDecisionHelper.BuildVideoEntity, pinned) are extracted; the DynamicVideos/NfoFileGenerator file-write if/else still lives in the job and its side effect is intentionally NOT covered. Orchestration / HTTP fetch / filesystem / DB and AutoDistinct remain uncovered until further seams are extracted.`

(If the exact wording of the prior bullet differs, preserve its surrounding sentence shape and only swap in the substance above — the goal is: pure mapping now pinned, NFO file-write still uncovered, orchestration/HTTP/FS/DB/AutoDistinct still uncovered.)

- [ ] **Step 2: Verify the doc reads consistently**

Run: `grep -n "BuildVideoEntity\|CreateVideoEntity\|SyncDecisionHelper" tests/README.md`
Expected: the pinned row mentions `BuildVideoEntity`; the NOT-covered note mentions `CreateVideoEntity` pure mapping pinned + NFO file-write uncovered.

- [ ] **Step 3: Commit**

```bash
cd "/mnt/EA7802167801E265/WorkSpace/Personal/dysync.net/mawanglin.dysync.net"
git add tests/README.md
git -c user.name='Claude Code' -c user.email='mjgenab@gmail.com' commit -m "$(cat <<'EOF'
docs: pin BuildVideoEntity coverage; note NFO file-write still uncovered

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
EOF
)"
```

---

### Task 4: Push and update project memory

- [ ] **Step 1: Push the branch**

```bash
cd "/mnt/EA7802167801E265/WorkSpace/Personal/dysync.net/mawanglin.dysync.net"
git push origin decompile/dy-sync-lib
```
Do NOT merge, do NOT open a PR (standing constraint).

- [ ] **Step 2: Update project memory**

Update `/home/virson/.claude/projects/-mnt-EA7802167801E265-WorkSpace-Personal-dysync-net-mawanglin-dysync-net/memory/project-dysync-security-hardening.md`: extend the DouyinBasicSyncJob split paragraph noting the SECOND slice landed (CreateVideoEntity pure mapping → `SyncDecisionHelper.BuildVideoEntity`, spec/plan paths, new branch head, suite 46 green, NFO file-write side effect still in job, remaining = DynamicVideos/NFO side effect + AutoDistinct + orchestration/HTTP/FS/DB + sync-over-async + frontend interceptor). Keep it one updated paragraph, no duplicate memory file.

---

## Self-Review

**1. Spec coverage:**
- Spec §架构.1 (new `BuildVideoEntity`, verbatim move, VideoType→param, Id/SyncTime→params, no DynamicVideos/NFO) → Task 1 Step 1. ✓
- Spec §架构.1 note (`GetVideoTags` resolves to same-class static) → satisfied: `BuildVideoEntity` is in `SyncDecisionHelper`, unqualified `GetVideoTags(item)` binds to `SyncDecisionHelper.GetVideoTags`. ✓
- Spec §架构.2 (job thin-delegate, if/else verbatim, signatures/callsites/subclasses unchanged) → Task 1 Step 2. ✓
- Spec §架构.3 (characterization tests, no TestDb, fixed id/syncTime, full matrix: core map, VideoTitle fallback, cate-noncustom both desc cases, OnlyImgOrOnlyMp3  both, DyUserId branch, AuthorAvatarUrl fallback, FileSize null) → Task 2, 9 Facts cover every listed bullet incl. FileSize-zero (spec's "FileSize 回落"). ✓
- Spec §架构.4 (tests/README pinned row + NOT-covered note) → Task 3. ✓
- Spec §验证与收尾 (roll-forward build/test, explicit `git add`, commit identity, push no-merge/no-PR, update memory) → Task 1-4 commands + Task 4. ✓

**2. Placeholder scan:** No TBD/TODO; every code step has full copy-ready code; every command has expected output. Task 3 Step 1 gives exact append text with a fallback instruction only for whitespace/wording tolerance (substance is fully specified). ✓

**3. Type consistency:** `BuildVideoEntity` signature `(VideoTypeEnum videoType, AppConfig config, DouyinCookie cookie, Aweme item, VideoBitRate bitRate, string savePath, string coverSavePath, string avatorPath, string id, DateTime syncTime, DouyinCollectCate cate)` is identical in Task 1 Step 1 (definition), Task 1 Step 2 (job call: `VideoType, config, cookie, item, bitRate, savePath, coverSavePath, avatorPath, IdGener.GetLong().ToString(), DateTime.Now, cate`), and Task 2 (all 9 test calls, same positional order). Member names asserted in tests (`ViedoType`, `FileSize` long, `DataSize` long?, `CateId/CateXId`, `MixInfo.Statis.CurrentEpisode`, `VideoTagItem.Level/TagName`) match the type-shapes table verified against source. ✓
