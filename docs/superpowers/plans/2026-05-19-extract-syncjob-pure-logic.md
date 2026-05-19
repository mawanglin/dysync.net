# Extract DouyinBasicSyncJob Pure Logic Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Move four side-effect-free decision methods out of the 1520-line `DouyinBasicSyncJob` god-class into a testable static helper, and pin their current behavior with golden-master tests.

**Architecture:** New `static class SyncDecisionHelper` (utils/, namespace `dy.net.utils`) holds the four methods verbatim; `DouyinBasicSyncJob` keeps the original method signatures but their bodies become one-line delegations to the helper. Zero observable behavior change for the job and its 6 subclasses; the extracted logic becomes independently unit-testable. A new xUnit characterization test class locks the behavior.

**Tech Stack:** C# / .NET 8 (build with `DOTNET_ROLL_FORWARD=LatestMajor`, local SDK is 10), xUnit 2.9.2, existing `tests/dy.net.Tests` project.

---

## Spec

`docs/superpowers/specs/2026-05-19-extract-syncjob-pure-logic-design.md`

## Reference: exact current source (do not paraphrase — copy)

These are the four method bodies as they currently exist in `job/DouyinBasicSyncJob.cs`:

```csharp
// line 190
private static string GetNextCursor(DouyinVideoInfoResponse data)
{
    return data?.Cursor ?? (data?.MaxCursor ?? "0");
}

// line 1327
private static bool IsAwemeValid(Aweme item) => item != null && item.Video != null && item.Video.BitRate != null;

// line 1335
protected (string tag1, string tag2, string tag3) GetVideoTags(Aweme item)
{
    var tags = item.VideoTags;
    return (
        tags?.FirstOrDefault(x => x.Level == 1)?.TagName,
        tags?.FirstOrDefault(x => x.Level == 2)?.TagName,
        tags?.FirstOrDefault(x => x.Level == 3)?.TagName
    );
}

// line 483 (active logic only; the commented-out _syncStatusCheckMap block below it is dead and is NOT carried over — comments are not behavior)
private bool IsSyncLimitReached(DouyinCookie cookie, AppConfig config, int syncCount, DouyinCollectCate cate, DouyinFollowed followed)
{
    if (cate != null && cate.CateType != VideoTypeEnum.dy_custom_collect)
    {
        if (syncCount >= 30)
        {
            Log.Debug($"[{cookie.UserName}][{VideoType.GetDesc()}]:本次同步数量{syncCount}，等下次任务继续同步");
            return true;
        }
    }
    else
    {
        if (syncCount >= config.BatchCount)
        {
            Log.Debug($"[{cookie.UserName}][{VideoType.GetDesc()}]:本次同步数量{syncCount}，已达配置上限{config.BatchCount}，等下次任务继续同步");
            return true;
        }
    }
    if (VideoType == VideoTypeEnum.dy_collects || VideoType == VideoTypeEnum.dy_favorite)
        return config.OnlySyncNew;

    return VideoType == VideoTypeEnum.dy_follows && !followed.FullSync;
}
```

## Reference: exact type shapes for test fixtures

| Type | Namespace | Members used |
|------|-----------|--------------|
| `DouyinVideoInfoResponse` | `dy.net.model.response` | `string Cursor`, `string MaxCursor` |
| `Aweme` | `dy.net.model.response` | `Video Video`, `List<VideoTagItem> VideoTags` |
| `Video` | `dy.net.model.response` | `List<VideoBitRate> BitRate` |
| `VideoTagItem` | `dy.net.model.response` | `int Level`, `string TagName` |
| `DouyinCookie` | `dy.net.model.entity` | `string UserName` |
| `AppConfig` | `dy.net.model.entity` | `int BatchCount` (default 18), `bool OnlySyncNew` (default true) |
| `DouyinCollectCate` | `dy.net.model.entity` | `VideoTypeEnum CateType` |
| `DouyinFollowed` | `dy.net.model.entity` | `bool FullSync` |
| `VideoTypeEnum` | `dy.net.model.dto` | `dy_favorite=1, dy_collects=2, dy_follows=3, ImageVideo=4, dy_custom_collect=5, dy_mix=6, dy_series=7` |
| `GetDesc(this VideoTypeEnum)` | `dy.net.utils` (`DouyinRequestParamManager`) | same namespace as helper — no extra using needed |

---

### Task 1: Create `SyncDecisionHelper` and delegate from the job

**Files:**
- Create: `utils/SyncDecisionHelper.cs`
- Modify: `job/DouyinBasicSyncJob.cs` (bodies at lines 190, 483, 1327, 1335)

- [ ] **Step 1: Confirm no subclass overrides these four methods**

Run:
```bash
cd /mnt/EA7802167801E265/WorkSpace/Personal/dysync.net/mawanglin.dysync.net
grep -rn "GetNextCursor\|IsAwemeValid\|GetVideoTags\|IsSyncLimitReached" job/*.cs | grep -v "DouyinBasicSyncJob.cs"
```
Expected: **no output** (the 6 subclass files do not reference or override these). If any output appears, STOP and report — delegation assumptions break.

- [ ] **Step 2: Create the helper**

Create `utils/SyncDecisionHelper.cs` with exactly:

```csharp
using dy.net.model.dto;
using dy.net.model.entity;
using dy.net.model.response;
using Serilog;

namespace dy.net.utils
{
    /// <summary>
    /// 从 DouyinBasicSyncJob 抽出的无 I/O 纯决策逻辑。
    /// 行为逐字保留，仅把原抽象属性 VideoType 提升为入参。
    /// 由特征化测试 SyncDecisionHelperTests 锁定当前行为。
    /// </summary>
    public static class SyncDecisionHelper
    {
        public static string GetNextCursor(DouyinVideoInfoResponse data)
        {
            return data?.Cursor ?? (data?.MaxCursor ?? "0");
        }

        public static bool IsAwemeValid(Aweme item)
            => item != null && item.Video != null && item.Video.BitRate != null;

        public static (string tag1, string tag2, string tag3) GetVideoTags(Aweme item)
        {
            var tags = item.VideoTags;
            return (
                tags?.FirstOrDefault(x => x.Level == 1)?.TagName,
                tags?.FirstOrDefault(x => x.Level == 2)?.TagName,
                tags?.FirstOrDefault(x => x.Level == 3)?.TagName
            );
        }

        public static bool IsSyncLimitReached(VideoTypeEnum videoType, DouyinCookie cookie,
            AppConfig config, int syncCount, DouyinCollectCate cate, DouyinFollowed followed)
        {
            if (cate != null && cate.CateType != VideoTypeEnum.dy_custom_collect)
            {
                if (syncCount >= 30)
                {
                    Log.Debug($"[{cookie.UserName}][{videoType.GetDesc()}]:本次同步数量{syncCount}，等下次任务继续同步");
                    return true;
                }
            }
            else
            {
                if (syncCount >= config.BatchCount)
                {
                    Log.Debug($"[{cookie.UserName}][{videoType.GetDesc()}]:本次同步数量{syncCount}，已达配置上限{config.BatchCount}，等下次任务继续同步");
                    return true;
                }
            }
            if (videoType == VideoTypeEnum.dy_collects || videoType == VideoTypeEnum.dy_favorite)
                return config.OnlySyncNew;

            return videoType == VideoTypeEnum.dy_follows && !followed.FullSync;
        }
    }
}
```

Note: `FirstOrDefault` resolves via the project's implicit `System.Linq` (the web csproj uses ImplicitUsings; if a build error `CS1061 ... FirstOrDefault` appears, add `using System.Linq;` to the helper).

- [ ] **Step 3: Replace the four bodies in `DouyinBasicSyncJob.cs` with delegations**

Edit `job/DouyinBasicSyncJob.cs`. Replace the line-190 method:

```csharp
private static string GetNextCursor(DouyinVideoInfoResponse data)
{
    return data?.Cursor ?? (data?.MaxCursor ?? "0");
}
```
with:
```csharp
private static string GetNextCursor(DouyinVideoInfoResponse data)
    => SyncDecisionHelper.GetNextCursor(data);
```

Replace the line-1327 method:
```csharp
private static bool IsAwemeValid(Aweme item) => item != null && item.Video != null && item.Video.BitRate != null;
```
with:
```csharp
private static bool IsAwemeValid(Aweme item) => SyncDecisionHelper.IsAwemeValid(item);
```

Replace the line-1335 method (entire body, signature unchanged):
```csharp
protected (string tag1, string tag2, string tag3) GetVideoTags(Aweme item)
{
    var tags = item.VideoTags;
    return (
        tags?.FirstOrDefault(x => x.Level == 1)?.TagName,
        tags?.FirstOrDefault(x => x.Level == 2)?.TagName,
        tags?.FirstOrDefault(x => x.Level == 3)?.TagName
    );
}
```
with:
```csharp
protected (string tag1, string tag2, string tag3) GetVideoTags(Aweme item)
    => SyncDecisionHelper.GetVideoTags(item);
```

Replace the line-483 method body (signature unchanged; pass the abstract `VideoType` property as the first argument). Replace from `private bool IsSyncLimitReached(...)` through its closing `}` (delete the active body **and** the trailing commented-out `_syncStatusCheckMap` block that lives inside the method) with:
```csharp
private bool IsSyncLimitReached(DouyinCookie cookie, AppConfig config, int syncCount, DouyinCollectCate cate, DouyinFollowed followed)
    => SyncDecisionHelper.IsSyncLimitReached(VideoType, cookie, config, syncCount, cate, followed);
```

`dy.net.utils` is already imported at the top of `DouyinBasicSyncJob.cs` (line 6: `using dy.net.utils;`) so `SyncDecisionHelper` resolves with no new using.

- [ ] **Step 4: Build the web project — expect 0 errors**

Run:
```bash
cd /mnt/EA7802167801E265/WorkSpace/Personal/dysync.net/mawanglin.dysync.net
DOTNET_ROLL_FORWARD=LatestMajor dotnet build dy.net.csproj -v q 2>&1 | tail -15
```
Expected: `Build succeeded` with `0 Error(s)`. If `CS1061 ... 'List<VideoTagItem>' does not contain a definition for 'FirstOrDefault'` appears, add `using System.Linq;` to `utils/SyncDecisionHelper.cs` and rebuild.

- [ ] **Step 5: Run the existing full test suite — expect still green (regression guard)**

Run:
```bash
cd /mnt/EA7802167801E265/WorkSpace/Personal/dysync.net/mawanglin.dysync.net
DOTNET_ROLL_FORWARD=LatestMajor dotnet test tests/dy.net.Tests/dy.net.Tests.csproj 2>&1 | tail -8
```
Expected: `失败: 0` / `通过: 17` (the pre-existing 17 characterization tests still pass — proves the extraction did not change `GetStatics`/`GetChartData` or any pinned behavior).

- [ ] **Step 6: Commit**

```bash
cd /mnt/EA7802167801E265/WorkSpace/Personal/dysync.net/mawanglin.dysync.net
git add utils/SyncDecisionHelper.cs job/DouyinBasicSyncJob.cs
git -c user.name='Claude Code' -c user.email='mjgenab@gmail.com' commit -m "refactor(job): extract DouyinBasicSyncJob pure decision logic to SyncDecisionHelper

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>"
```

---

### Task 2: Characterization tests for `SyncDecisionHelper`

**Files:**
- Create: `tests/dy.net.Tests/SyncDecisionHelperTests.cs`

- [ ] **Step 1: Write the characterization test class**

Create `tests/dy.net.Tests/SyncDecisionHelperTests.cs` with exactly:

```csharp
using dy.net.model.dto;
using dy.net.model.entity;
using dy.net.model.response;
using dy.net.utils;

namespace dy.net.Tests
{
    // Golden-master / characterization: pins SyncDecisionHelper's CURRENT behavior
    // (logic moved verbatim out of DouyinBasicSyncJob). A failure here means the
    // extraction changed behavior — fix the helper, never weaken the assertion.
    public class SyncDecisionHelperTests
    {
        // ---- GetNextCursor ----

        [Fact]
        public void GetNextCursor_prefers_Cursor()
            => Assert.Equal("c1", SyncDecisionHelper.GetNextCursor(
                new DouyinVideoInfoResponse { Cursor = "c1", MaxCursor = "m1" }));

        [Fact]
        public void GetNextCursor_falls_back_to_MaxCursor_when_Cursor_null()
            => Assert.Equal("m1", SyncDecisionHelper.GetNextCursor(
                new DouyinVideoInfoResponse { Cursor = null, MaxCursor = "m1" }));

        [Fact]
        public void GetNextCursor_returns_zero_when_both_null()
            => Assert.Equal("0", SyncDecisionHelper.GetNextCursor(
                new DouyinVideoInfoResponse { Cursor = null, MaxCursor = null }));

        [Fact]
        public void GetNextCursor_returns_zero_when_data_null()
            => Assert.Equal("0", SyncDecisionHelper.GetNextCursor(null));

        // ---- IsAwemeValid ----

        [Fact]
        public void IsAwemeValid_false_when_item_null()
            => Assert.False(SyncDecisionHelper.IsAwemeValid(null));

        [Fact]
        public void IsAwemeValid_false_when_video_null()
            => Assert.False(SyncDecisionHelper.IsAwemeValid(new Aweme { Video = null }));

        [Fact]
        public void IsAwemeValid_false_when_bitrate_null()
            => Assert.False(SyncDecisionHelper.IsAwemeValid(
                new Aweme { Video = new Video { BitRate = null } }));

        [Fact]
        public void IsAwemeValid_true_when_all_present()
            => Assert.True(SyncDecisionHelper.IsAwemeValid(
                new Aweme { Video = new Video { BitRate = new List<VideoBitRate>() } }));

        // ---- GetVideoTags ----

        [Fact]
        public void GetVideoTags_picks_level_1_2_3()
        {
            var item = new Aweme
            {
                VideoTags = new List<VideoTagItem>
                {
                    new VideoTagItem { Level = 1, TagName = "一级" },
                    new VideoTagItem { Level = 2, TagName = "二级" },
                    new VideoTagItem { Level = 3, TagName = "三级" },
                }
            };
            Assert.Equal(("一级", "二级", "三级"), SyncDecisionHelper.GetVideoTags(item));
        }

        [Fact]
        public void GetVideoTags_missing_level_yields_null_for_that_level()
        {
            var item = new Aweme
            {
                VideoTags = new List<VideoTagItem>
                {
                    new VideoTagItem { Level = 1, TagName = "一级" },
                    new VideoTagItem { Level = 3, TagName = "三级" },
                }
            };
            Assert.Equal(("一级", (string)null, "三级"), SyncDecisionHelper.GetVideoTags(item));
        }

        [Fact]
        public void GetVideoTags_null_list_yields_all_null()
            => Assert.Equal(((string)null, (string)null, (string)null),
                SyncDecisionHelper.GetVideoTags(new Aweme { VideoTags = null }));

        // ---- IsSyncLimitReached ----

        private static DouyinCookie Cookie() => new DouyinCookie { UserName = "u" };

        [Fact]
        public void IsSyncLimitReached_noncustom_cate_over_30_returns_true()
        {
            var r = SyncDecisionHelper.IsSyncLimitReached(
                VideoTypeEnum.dy_mix, Cookie(), new AppConfig(), 30,
                new DouyinCollectCate { CateType = VideoTypeEnum.dy_mix }, null);
            Assert.True(r);
        }

        [Fact]
        public void IsSyncLimitReached_null_cate_over_batchcount_returns_true()
        {
            var r = SyncDecisionHelper.IsSyncLimitReached(
                VideoTypeEnum.dy_favorite, Cookie(),
                new AppConfig { BatchCount = 18 }, 18, null, null);
            Assert.True(r);
        }

        [Fact]
        public void IsSyncLimitReached_custom_cate_over_batchcount_returns_true()
        {
            var r = SyncDecisionHelper.IsSyncLimitReached(
                VideoTypeEnum.dy_custom_collect, Cookie(),
                new AppConfig { BatchCount = 5 }, 5,
                new DouyinCollectCate { CateType = VideoTypeEnum.dy_custom_collect }, null);
            Assert.True(r);
        }

        [Fact]
        public void IsSyncLimitReached_under_limit_collects_returns_OnlySyncNew_true()
        {
            var r = SyncDecisionHelper.IsSyncLimitReached(
                VideoTypeEnum.dy_collects, Cookie(),
                new AppConfig { BatchCount = 18, OnlySyncNew = true }, 0, null, null);
            Assert.True(r);
        }

        [Fact]
        public void IsSyncLimitReached_under_limit_favorite_returns_OnlySyncNew_false()
        {
            var r = SyncDecisionHelper.IsSyncLimitReached(
                VideoTypeEnum.dy_favorite, Cookie(),
                new AppConfig { BatchCount = 18, OnlySyncNew = false }, 0, null, null);
            Assert.False(r);
        }

        [Fact]
        public void IsSyncLimitReached_under_limit_follows_returns_not_FullSync()
        {
            var notFull = SyncDecisionHelper.IsSyncLimitReached(
                VideoTypeEnum.dy_follows, Cookie(), new AppConfig { BatchCount = 18 }, 0,
                null, new DouyinFollowed { FullSync = false });
            Assert.True(notFull);

            var full = SyncDecisionHelper.IsSyncLimitReached(
                VideoTypeEnum.dy_follows, Cookie(), new AppConfig { BatchCount = 18 }, 0,
                null, new DouyinFollowed { FullSync = true });
            Assert.False(full);
        }

        [Fact]
        public void IsSyncLimitReached_under_limit_mix_short_circuits_to_false()
        {
            // videoType != dy_collects/dy_favorite and != dy_follows:
            // expression `videoType == dy_follows && !followed.FullSync` short-circuits
            // on the first term, returns false, never dereferences followed (null safe).
            var r = SyncDecisionHelper.IsSyncLimitReached(
                VideoTypeEnum.dy_mix, Cookie(), new AppConfig { BatchCount = 18 }, 0,
                null, null);
            Assert.False(r);
        }
    }
}
```

- [ ] **Step 2: Run the new test class — expect all pass**

Run:
```bash
cd /mnt/EA7802167801E265/WorkSpace/Personal/dysync.net/mawanglin.dysync.net
DOTNET_ROLL_FORWARD=LatestMajor dotnet test tests/dy.net.Tests/dy.net.Tests.csproj --filter SyncDecisionHelperTests 2>&1 | tail -8
```
Expected: `失败: 0`, all `SyncDecisionHelperTests` pass. If any FAIL: the assertion encodes the behavior derived from the verbatim source — a failure means the helper transcription diverged from the original. Fix `utils/SyncDecisionHelper.cs` to match the Reference source exactly; do NOT change the assertion.

- [ ] **Step 3: Run the full suite — expect 17 + new all green**

Run:
```bash
cd /mnt/EA7802167801E265/WorkSpace/Personal/dysync.net/mawanglin.dysync.net
DOTNET_ROLL_FORWARD=LatestMajor dotnet test tests/dy.net.Tests/dy.net.Tests.csproj 2>&1 | tail -8
```
Expected: `失败: 0`, `通过: 35` (17 existing + 18 new test cases — exact count may differ if xUnit counts the multi-assert `follows` test as one; the requirement is **failed: 0** and the new class fully green).

- [ ] **Step 4: Commit**

```bash
cd /mnt/EA7802167801E265/WorkSpace/Personal/dysync.net/mawanglin.dysync.net
git add tests/dy.net.Tests/SyncDecisionHelperTests.cs
git -c user.name='Claude Code' -c user.email='mjgenab@gmail.com' commit -m "test: golden-master tests pinning SyncDecisionHelper behavior

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>"
```

---

### Task 3: Update test docs and push

**Files:**
- Modify: `tests/README.md`

- [ ] **Step 1: Add the `SyncDecisionHelper` row to the pinned-coverage table**

In `tests/README.md`, in the "What is pinned" table, add this row immediately after the `Md5Util.Md5` row:

```markdown
| `SyncDecisionHelper` | `SyncDecisionHelperTests` | `GetNextCursor` (Cursor→MaxCursor→`"0"`, null-safe), `IsAwemeValid` (3-level null guard), `GetVideoTags` (per-level pick, missing→null), `IsSyncLimitReached` (cate 30-cap vs `BatchCount` cap, `OnlySyncNew` passthrough, `dy_follows` `!FullSync`, mix/series short-circuit) |
```

- [ ] **Step 2: Update the "NOT covered" `DouyinBasicSyncJob` bullet**

In `tests/README.md`, replace the existing `DouyinBasicSyncJob` bullet under "What is intentionally NOT covered (and why)":

Old:
```markdown
- **`DouyinBasicSyncJob` orchestration** — HTTP + filesystem + DB coupled with
  no seams. Characterizing it requires extracting interfaces first; doing that
  *is* the refactor. Pin the extracted pure pieces as they come out.
```
New:
```markdown
- **`DouyinBasicSyncJob` orchestration** — HTTP + filesystem + DB coupled with
  no seams. Its side-effect-free decision logic has been extracted to
  `SyncDecisionHelper` and is now pinned (see table above); the remaining
  orchestration/HTTP/FS/DB body stays uncovered until further seams are
  extracted in follow-up plans.
```

- [ ] **Step 3: Verify suite still green (no code change, sanity only)**

Run:
```bash
cd /mnt/EA7802167801E265/WorkSpace/Personal/dysync.net/mawanglin.dysync.net
DOTNET_ROLL_FORWARD=LatestMajor dotnet test tests/dy.net.Tests/dy.net.Tests.csproj 2>&1 | tail -5
```
Expected: `失败: 0`.

- [ ] **Step 4: Commit and push (push only — no merge, no PR)**

```bash
cd /mnt/EA7802167801E265/WorkSpace/Personal/dysync.net/mawanglin.dysync.net
git add tests/README.md
git -c user.name='Claude Code' -c user.email='mjgenab@gmail.com' commit -m "docs: pin SyncDecisionHelper coverage; note god-class pure-logic extracted

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>"
git push origin decompile/dy-sync-lib
git log --oneline -4
```
Expected: push succeeds; do **not** merge to master, do **not** open a PR (standing constraint).

- [ ] **Step 5: Update project memory**

Append to `/home/virson/.claude/projects/-mnt-EA7802167801E265-WorkSpace-Personal-dysync-net-mawanglin-dysync-net/memory/project-dysync-security-hardening.md`: note that the first slice of the DouyinBasicSyncJob god-class split landed — pure decision logic (`GetNextCursor`/`IsAwemeValid`/`GetVideoTags`/`IsSyncLimitReached`) extracted to `utils/SyncDecisionHelper.cs`, pinned by `SyncDecisionHelperTests`, pushed; `CreateVideoEntity`/`AutoDistinct`/orchestration still pending as separate follow-up plans.

---

## Self-Review

**Spec coverage:** Spec §"范围内的四个方法" → Task 1 Step 2/3. Spec §"架构 1/2" (helper + thin delegation, signatures/subclasses untouched) → Task 1 Step 2/3 + Step 1 override-check. Spec §"架构 3" (characterization tests, no TestDb, branch matrix) → Task 2 Step 1 (all four method groups + IsSyncLimitReached branch matrix incl. mix/series short-circuit). Spec §"架构 4" (README rows) → Task 3 Step 1/2. Spec §"测试策略与正确性" (build 0 errors, suite green, fix helper not assertion) → Task 1 Step 4/5, Task 2 Step 2/3. Spec §"验证与收尾" (roll-forward, explicit add, Claude Code identity, push-only no merge/PR, memory) → Task 1 Step 6, Task 2 Step 4, Task 3 Step 4/5. No gaps.

**Placeholder scan:** No TBD/TODO/"handle edge cases"/"similar to". All code blocks complete and copy-ready.

**Type consistency:** Helper method names `GetNextCursor`/`IsAwemeValid`/`GetVideoTags`/`IsSyncLimitReached` and signature `IsSyncLimitReached(VideoTypeEnum, DouyinCookie, AppConfig, int, DouyinCollectCate, DouyinFollowed)` are identical across Task 1 (definition), Task 1 (delegation call sites), and Task 2 (test call sites). Property names (`Cursor`, `MaxCursor`, `Video`, `BitRate`, `VideoTags`, `Level`, `TagName`, `UserName`, `BatchCount`, `OnlySyncNew`, `CateType`, `FullSync`) match the verified type-shapes table. Consistent.
