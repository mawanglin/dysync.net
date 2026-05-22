# Extract AutoDistinct Priority Decision Logic — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Extract the four-level nested priority 判定 from `DouyinBasicSyncJob.AutoDistinct` into a pure, independently-testable `SyncDecisionHelper.ResolveDuplicateVideoAction`, leaving all file/DB I/O in a thin job shell.

**Architecture:** Behavior-preserving "thin shell" extraction (seventh god-class slice). A new `DuplicateVideoAction` enum names the two decision outcomes; the helper takes plain objects (no I/O, no external deps); `AutoDistinct` keeps its signature and all guards/I/O. The two asymmetric `DeleteOldViedo` try/catch sites are normalized to one guarded delete — the single deliberate behavior deviation, recorded in spec + commit message. Golden-master characterization tests pin the helper before/independent of any future change.

**Tech Stack:** .NET 8 (`net8.0`; local SDK 10 → all `dotnet` commands prefixed `DOTNET_ROLL_FORWARD=LatestMajor`), xUnit (`tests/dy.net.Tests`), C#.

**Spec:** `docs/superpowers/specs/2026-05-22-extract-autodistinct-priority-decision-design.md`

---

## File Structure

- **Create:** `model/dto/DuplicateVideoAction.cs` — the decision enum (one responsibility: name the two去重 outcomes). Lives in `model/dto/` beside `VideoTypeEnum.cs` per project convention.
- **Modify:** `utils/SyncDecisionHelper.cs` — append one pure method `ResolveDuplicateVideoAction`; existing 10 methods untouched.
- **Modify:** `job/DouyinBasicSyncJob.cs` — thin `AutoDistinct` (lines 662-778); BRANCH 2 + all guards verbatim, BRANCH 1 delegates to the helper.
- **Modify:** `tests/dy.net.Tests/SyncDecisionHelperTests.cs` — append one `// ---- ResolveDuplicateVideoAction ----` section (8 `[Fact]` + one section-local helper).
- **Modify:** `tests/README.md` — record the new pinned coverage.

`AutoDistinct` is `private` (cannot be overridden) → job-side change is confined to `DouyinBasicSyncJob.cs`.

---

## Task 1: Extract `ResolveDuplicateVideoAction` + thin `AutoDistinct`

**Files:**
- Create: `model/dto/DuplicateVideoAction.cs`
- Modify: `utils/SyncDecisionHelper.cs` (append before the class-closing `}` — after `BuildCoverPosterPath`)
- Modify: `job/DouyinBasicSyncJob.cs:662-778` (the `AutoDistinct` method)

- [ ] **Step 1: Create the `DuplicateVideoAction` enum**

Create `model/dto/DuplicateVideoAction.cs` with exactly:

```csharp
namespace dy.net.model.dto
{
    /// <summary>
    /// 去重决策结果：当 DB 已存在同 AwemeId 视频、且其本地文件也存在时，
    /// 按视频类型优先级判定本次同步应如何处理。
    /// 由 SyncDecisionHelper.ResolveDuplicateVideoAction 产出。
    /// </summary>
    public enum DuplicateVideoAction
    {
        /// <summary>跳过下载——已存在同等或更高优先级的视频。</summary>
        SkipDownload,

        /// <summary>删除旧文件后继续下载——当前类型优先级更高。</summary>
        ReplaceExisting
    }
}
```

- [ ] **Step 2: Append `ResolveDuplicateVideoAction` to `SyncDecisionHelper`**

In `utils/SyncDecisionHelper.cs`, insert this method immediately after `BuildCoverPosterPath`'s closing `}` and before the class-closing `}` (the file ends `        }\n    }\n}`):

```csharp

        /// <summary>
        /// 从 DouyinBasicSyncJob.AutoDistinct 抽出的纯优先级去重判定（无 I/O）。
        /// 行为逐字保留：priorityLevels 为空 → 默认最高优先级 {Id=1,Sort=1}（即 dy_favorite）；
        /// 否则取 Sort 最小者为最高优先级。判定见方法体四层嵌套 if/else。
        /// 抽象属性 VideoType 提升为 currentType 入参。
        /// 注意 priorityLevels 为 null 时 .Any() 会抛 NRE（与原 priLevs.Any() 逐字一致）——
        /// 既有行为，不加守卫。JsonConvert 反序列化、DeleteOldViedo/DeleteById 的 I/O 留在 job。
        /// 由特征化测试 SyncDecisionHelperTests 锁定当前行为。
        /// </summary>
        public static DuplicateVideoAction ResolveDuplicateVideoAction(
            VideoTypeEnum currentType,
            VideoTypeEnum exitVideoType,
            List<PriorityLevelDto> priorityLevels)
        {
            // 4. 处理优先级：获取「最高优先级」（Sort 越小优先级越高）
            PriorityLevelDto maxPriority = null;
            if (priorityLevels.Any())
            {
                // 前端已配置优先级：取 Sort 最小的（1最高）
                maxPriority = priorityLevels.OrderBy(x => x.Sort).FirstOrDefault();
            }
            else
            {
                // 前端未配置：使用默认优先级（喜欢 > 收藏 > 关注）
                maxPriority = new PriorityLevelDto { Id = 1, Sort = 1, Name = "喜欢的" }; // 默认「喜欢的视频」最高
            }

            // 5. 转换为当前上下文的视频类型
            var maxPriorityType = (VideoTypeEnum)maxPriority.Id; // 配置的最高优先级类型

            // 7. 优先级逻辑判断（核心）
            if (currentType == maxPriorityType)
            {
                // 情况1：当前要下载的是「最高优先级」视频
                if (exitVideoType == currentType)
                {
                    // 已存在同优先级视频 → 跳过下载（避免重复）
                    return DuplicateVideoAction.SkipDownload;
                }
                else
                {
                    // 已存在「低优先级」视频 → 替换（删除旧文件，继续下载新的最高优先级视频）
                    return DuplicateVideoAction.ReplaceExisting;
                }
            }
            else
            {
                // 情况2：当前要下载的是「非最高优先级」视频
                if (exitVideoType == maxPriorityType)
                {
                    // 已存在「最高优先级」视频 → 跳过（不替换最高优先级）
                    return DuplicateVideoAction.SkipDownload;
                }
                else
                {
                    // 已存在「其他非最高优先级」视频 → 比较两者优先级
                    var currentSort = priorityLevels.FirstOrDefault(x => x.Id == (int)currentType)?.Sort ?? int.MaxValue;
                    var exitSort = priorityLevels.FirstOrDefault(x => x.Id == (int)exitVideoType)?.Sort ?? int.MaxValue;

                    if (currentSort < exitSort)
                    {
                        // 当前类型优先级更高 → 替换旧视频
                        return DuplicateVideoAction.ReplaceExisting;
                    }
                    else
                    {
                        // 当前类型优先级更低或相等 → 跳过
                        return DuplicateVideoAction.SkipDownload;
                    }
                }
            }
        }
```

`SyncDecisionHelper.cs` already has `using dy.net.model.dto;` (where `PriorityLevelDto`, `VideoTypeEnum`, and the new `DuplicateVideoAction` all live) and `<ImplicitUsings>enable</ImplicitUsings>` covers `System.Collections.Generic`/`System.Linq`. No new `using`.

- [ ] **Step 3: Thin `AutoDistinct` to delegate to the helper**

In `job/DouyinBasicSyncJob.cs`, replace the entire body of `AutoDistinct` (currently lines 662-778, from `private async Task<bool> AutoDistinct(...)` through its closing `}`). The new method — signature, visibility, parameter order unchanged:

```csharp
        private async Task<bool> AutoDistinct(AppConfig config, DouyinVideo exitVideo, DouyinCookie cookie)
        {
            // 去重，检查视频是否已存在（按优先级下载）
            if (config.AutoDistinct)
            {
                if (exitVideo != null)
                {
                    // 2. 已存在视频：先判断本地文件是否存在
                    if (File.Exists(exitVideo.VideoSavePath))
                    {
                        List<PriorityLevelDto> priLevs = new List<PriorityLevelDto>();
                        if (!string.IsNullOrWhiteSpace(config.PriorityLevel))
                        {
                            priLevs = JsonConvert.DeserializeObject<List<PriorityLevelDto>>(config.PriorityLevel);
                        }

                        var action = SyncDecisionHelper.ResolveDuplicateVideoAction(VideoType, exitVideo.ViedoType, priLevs);
                        if (action == DuplicateVideoAction.SkipDownload)
                        {
                            return false;
                        }

                        // ReplaceExisting：删除旧的低优先级文件，继续下载（覆盖旧数据）
                        try
                        {
                            DeleteOldViedo(exitVideo);
                        }
                        catch (Exception ex)
                        {
                            Log.Error($"[{cookie.UserName}][{VideoType.GetDesc()}]-删除重复的文件[{exitVideo.VideoTitle}]失败：{ex.Message}", ex);
                        }
                    }
                    else
                    {
                        if (exitVideo.OnlyImgOrOnlyMp3)
                        {
                            return true;//说明是图文视频，不需要再下载视频了
                        }
                        else
                        {
                            //记录存在，但本地文件不存在，则继续下载。
                            //删除原来的记录
                            await douyinVideoService.DeleteById(exitVideo.Id);
                        }
                    }
                }
            }
            return true;
        }
```

Notes for the implementer:
- This is a verbatim structural extraction **except one deliberate deviation** (already approved + recorded in the spec): the original had two `DeleteOldViedo` call sites — one wrapped in `try/catch`, one not. The merged shell uses a single `try/catch`-guarded delete. Do not "fix" anything else.
- Indentation: `AutoDistinct` sits at 8-space method indent inside the class — match the surrounding methods exactly.
- `DeleteOldViedo` (the `private static` method at ~line 780) and the call site `ProcessVideoList:491` are **not** touched.
- `JsonConvert` / `Log` / `File` / `VideoType` / `douyinVideoService` are all already in scope in this file (unchanged usage).

- [ ] **Step 4: Build — verify 0 errors**

Run: `DOTNET_ROLL_FORWARD=LatestMajor dotnet build dy.net.csproj`
Expected: `Build succeeded. 0 Error(s)`.

- [ ] **Step 5: Run the existing suite — verify still green**

Run: `DOTNET_ROLL_FORWARD=LatestMajor dotnet test tests/dy.net.Tests/dy.net.Tests.csproj`
Expected: `Passed!  - Failed: 0` — **74 passed** (no new tests yet; the extraction must not break the existing golden masters).

- [ ] **Step 6: Commit**

```bash
git add model/dto/DuplicateVideoAction.cs utils/SyncDecisionHelper.cs job/DouyinBasicSyncJob.cs
git -c user.name='Claude Code' -c user.email='mjgenab@gmail.com' commit -m "$(cat <<'EOF'
refactor(job): extract AutoDistinct priority decision to SyncDecisionHelper

Move the four-level nested priority 判定 out of
DouyinBasicSyncJob.AutoDistinct into a pure
SyncDecisionHelper.ResolveDuplicateVideoAction, returning the new
DuplicateVideoAction enum. The job keeps a thin shell: config /
exitVideo / File.Exists guards, JsonConvert deserialization,
BRANCH 2 (OnlyImgOrOnlyMp3 / DeleteById) and DeleteOldViedo all stay.

One deliberate behavior deviation: the original's two "replace"
branches called DeleteOldViedo with asymmetric try/catch (one
guarded, one not). The single merged call site is now uniformly
try/catch-guarded — the safer direction, matching the author's
evident intent in the guarded branch.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 2: Characterization tests for `ResolveDuplicateVideoAction`

**Files:**
- Modify: `tests/dy.net.Tests/SyncDecisionHelperTests.cs` (append a new section before the class-closing `}`)

These are golden-master tests: they pin the helper's CURRENT behavior. The helper is a verbatim port, so first-run values ARE the golden values.

- [ ] **Step 1: Append the test section**

In `tests/dy.net.Tests/SyncDecisionHelperTests.cs`, insert the following immediately after the last test method (`BuildCoverPosterPath_OtherType_UsesFileNamePrefixedPoster`'s closing `}`) and before the class-closing `}`. `PriorityLevelDto.Id` carries a `VideoTypeEnum` integer value (`dy_favorite=1`, `dy_collects=2`, `dy_follows=3`); the section-local `Levels(...)` helper builds the priority list.

```csharp

        // ---- ResolveDuplicateVideoAction ----
        // pin: current behavior, not aspirational

        private static List<PriorityLevelDto> Levels(params (int id, int sort)[] entries)
            => entries.Select(e => new PriorityLevelDto { Id = e.id, Sort = e.sort }).ToList();

        [Fact]
        public void ResolveDuplicateVideoAction_EmptyLevels_CurrentFavoriteExitFavorite_Skips()
        {
            var action = SyncDecisionHelper.ResolveDuplicateVideoAction(
                VideoTypeEnum.dy_favorite, VideoTypeEnum.dy_favorite, new List<PriorityLevelDto>());
            Assert.Equal(DuplicateVideoAction.SkipDownload, action);
        }

        [Fact]
        public void ResolveDuplicateVideoAction_EmptyLevels_CurrentFavoriteExitCollects_Replaces()
        {
            var action = SyncDecisionHelper.ResolveDuplicateVideoAction(
                VideoTypeEnum.dy_favorite, VideoTypeEnum.dy_collects, new List<PriorityLevelDto>());
            Assert.Equal(DuplicateVideoAction.ReplaceExisting, action);
        }

        [Fact]
        public void ResolveDuplicateVideoAction_EmptyLevels_CurrentCollectsExitFavorite_Skips()
        {
            var action = SyncDecisionHelper.ResolveDuplicateVideoAction(
                VideoTypeEnum.dy_collects, VideoTypeEnum.dy_favorite, new List<PriorityLevelDto>());
            Assert.Equal(DuplicateVideoAction.SkipDownload, action);
        }

        [Fact]
        public void ResolveDuplicateVideoAction_ConfiguredHighestIsCurrent_ExitLower_Replaces()
        {
            // maxPriority = Sort-min = {Id=3} → dy_follows; current == max, exit != current
            var action = SyncDecisionHelper.ResolveDuplicateVideoAction(
                VideoTypeEnum.dy_follows, VideoTypeEnum.dy_collects, Levels((3, 1), (2, 2)));
            Assert.Equal(DuplicateVideoAction.ReplaceExisting, action);
        }

        [Fact]
        public void ResolveDuplicateVideoAction_NeitherIsHighest_CurrentSortSmaller_Replaces()
        {
            // max = {Id=1}; current dy_collects Sort=2 < exit dy_follows Sort=3
            var action = SyncDecisionHelper.ResolveDuplicateVideoAction(
                VideoTypeEnum.dy_collects, VideoTypeEnum.dy_follows, Levels((1, 1), (2, 2), (3, 3)));
            Assert.Equal(DuplicateVideoAction.ReplaceExisting, action);
        }

        [Fact]
        public void ResolveDuplicateVideoAction_NeitherIsHighest_CurrentSortLarger_Skips()
        {
            // max = {Id=1}; current dy_follows Sort=3 >= exit dy_collects Sort=2
            var action = SyncDecisionHelper.ResolveDuplicateVideoAction(
                VideoTypeEnum.dy_follows, VideoTypeEnum.dy_collects, Levels((1, 1), (2, 2), (3, 3)));
            Assert.Equal(DuplicateVideoAction.SkipDownload, action);
        }

        [Fact]
        public void ResolveDuplicateVideoAction_NeitherIsHighest_EqualSort_Skips()
        {
            // max = {Id=1}; current Sort=5, exit Sort=5; 5 < 5 is false → skip
            var action = SyncDecisionHelper.ResolveDuplicateVideoAction(
                VideoTypeEnum.dy_follows, VideoTypeEnum.dy_collects, Levels((1, 1), (2, 5), (3, 5)));
            Assert.Equal(DuplicateVideoAction.SkipDownload, action);
        }

        [Fact]
        public void ResolveDuplicateVideoAction_CurrentTypeMissingFromList_FallsBackToMaxValueSort_Skips()
        {
            // max = {Id=1}; current dy_follows absent → currentSort = int.MaxValue >= exit Sort=2
            var action = SyncDecisionHelper.ResolveDuplicateVideoAction(
                VideoTypeEnum.dy_follows, VideoTypeEnum.dy_collects, Levels((1, 1), (2, 2)));
            Assert.Equal(DuplicateVideoAction.SkipDownload, action);
        }
```

Notes for the implementer:
- The test project resolves `System.Linq` (`.Select`/`.ToList`) and `System.Collections.Generic` (`List<>`) via ImplicitUsings — existing tests already use `Path.*`/LINQ without explicit `using`. No new `using` directives.
- `DuplicateVideoAction`, `VideoTypeEnum`, `PriorityLevelDto` are in `dy.net.model.dto`, already imported at the top of the file.
- Do **not** add a null-`priorityLevels` test — that path NREs by design (see spec "保留怪癖"); pinning a crash is forbidden.

- [ ] **Step 2: Run the new section — verify all 8 pass**

Run: `DOTNET_ROLL_FORWARD=LatestMajor dotnet test tests/dy.net.Tests/dy.net.Tests.csproj --filter "FullyQualifiedName~ResolveDuplicateVideoAction"`
Expected: `Passed!  - Failed: 0` — **8 passed**.

If any fails: the helper is a verbatim port, so a failure means either the test input was mis-traced or the port deviated. Re-trace by hand against the spec's "Pocket" listing; fix the helper if it deviated, fix the test if the trace was wrong. Never weaken an assertion.

- [ ] **Step 3: Run the full suite — verify 82 green**

Run: `DOTNET_ROLL_FORWARD=LatestMajor dotnet test tests/dy.net.Tests/dy.net.Tests.csproj`
Expected: `Passed!  - Failed: 0` — **82 passed** (74 + 8).

- [ ] **Step 4: Commit**

```bash
git add tests/dy.net.Tests/SyncDecisionHelperTests.cs
git -c user.name='Claude Code' -c user.email='mjgenab@gmail.com' commit -m "$(cat <<'EOF'
test: golden-master tests pinning ResolveDuplicateVideoAction

8 characterization [Fact]s covering all five leaves of the priority
判定: empty-levels default (dy_favorite highest), configured-highest
replace/skip, neither-highest sort comparison, equal-sort skip, and
the missing-type int.MaxValue fallback. Filtered 57→65, full 74→82.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 3: Update `tests/README.md` coverage doc

**Files:**
- Modify: `tests/README.md`

- [ ] **Step 1: Add `ResolveDuplicateVideoAction` to the `SyncDecisionHelper` table row**

In `tests/README.md`, the "What is pinned" table has one row for `SyncDecisionHelper` (line ~24). It currently ends with:

```
..., `BuildCoverPosterPath` (dy_mix·dy_series→poster.jpg / 其余→{名}-poster.jpg) |
```

Append `ResolveDuplicateVideoAction` before the closing ` |`:

```
..., `BuildCoverPosterPath` (dy_mix·dy_series→poster.jpg / 其余→{名}-poster.jpg), `ResolveDuplicateVideoAction` (优先级去重判定：空表→默认 dy_favorite 最高 / 配置表→Sort 最小者最高 / 四层嵌套产出 SkipDownload·ReplaceExisting / 缺项 Sort 回退 int.MaxValue) |
```

- [ ] **Step 2: Update the "What is intentionally NOT covered" `DouyinBasicSyncJob` entry**

In the bullet that lists extracted decision logic (lines ~40-59), the `PickCoverUrl`/`BuildCoverPosterPath` clause ends with:

```
...对 `Video`/`Cover` 无空安全的 NRE 路径保留不测) — all pinned (see table
  above). Still uncovered:
  `ProcessSingleVideo`/`ProcessDynamicVideo`/`ProcessImageSetAndMergeToVideo`
  orchestration bodies, `AutoDistinct`, `SaveVideos`, `DownAuthorAvatar`,
  `CleanupFailedVideos`, `HandleSyncCompletion` — all retain HTTP / FS / DB
  coupling and will be characterized as further seams are extracted in
  follow-up plans.
```

Replace from `...NRE 路径保留不测)` through `follow-up plans.` with:

```
...对 `Video`/`Cover` 无空安全的 NRE 路径保留不测),
  `ResolveDuplicateVideoAction` (`AutoDistinct` 的四层嵌套优先级判定已抽出并
  pinned；其 `config.AutoDistinct`/`File.Exists` 守卫、`JsonConvert` 反序列化、
  `DeleteOldViedo`/`DeleteById` I/O、BRANCH 2 仍在 job 薄壳内、未覆盖；
  `priorityLevels` 为 null 的 NRE 路径保留不测；薄壳 `DeleteOldViedo` 的
  try/catch 归一化为本刀唯一行为偏差、不在测试覆盖内) — all pinned (see table
  above). Still uncovered:
  `ProcessSingleVideo`/`ProcessDynamicVideo`/`ProcessImageSetAndMergeToVideo`
  orchestration bodies, `SaveVideos`, `DownAuthorAvatar`,
  `CleanupFailedVideos`, `HandleSyncCompletion` — all retain HTTP / FS / DB
  coupling and will be characterized as further seams are extracted in
  follow-up plans.
```

(`AutoDistinct` is removed from the "Still uncovered" list — its pure logic is now extracted and pinned.)

- [ ] **Step 3: Update the standalone `AutoDistinct` bullet**

The bullet at lines ~60-62 currently reads:

```
- **`AutoDistinct`** — private, instance-level, DB-coupled. Not directly
  reachable. Its supporting pure helpers (above) are pinned; characterize
  `AutoDistinct` itself only after it is extracted to a testable seam.
```

Replace it with:

```
- **`AutoDistinct` shell** — the priority 判定 is now extracted to
  `SyncDecisionHelper.ResolveDuplicateVideoAction` and pinned. What remains
  in `AutoDistinct` is a private, DB-coupled I/O shell (guards, JsonConvert,
  DeleteOldViedo, DeleteById) — not directly reachable, not characterized.
```

- [ ] **Step 4: Verify the doc reads correctly**

Run: `grep -n "ResolveDuplicateVideoAction" tests/README.md`
Expected: 2 matches (the table row + the NOT-covered entry).

Run: `DOTNET_ROLL_FORWARD=LatestMajor dotnet test tests/dy.net.Tests/dy.net.Tests.csproj`
Expected: `Passed!  - Failed: 0` — **82 passed** (doc change must not affect the build/tests).

- [ ] **Step 5: Commit**

```bash
git add tests/README.md docs/superpowers/plans/2026-05-22-extract-autodistinct-priority-decision.md
git -c user.name='Claude Code' -c user.email='mjgenab@gmail.com' commit -m "$(cat <<'EOF'
docs: pin ResolveDuplicateVideoAction coverage in tests/README

Also commits the seventh-slice implementation plan.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Final Steps (after all tasks)

- [ ] Push the commit chain to origin: `git push origin decompile/dy-sync-lib` — **do NOT merge, do NOT open a PR** (standing constraint). Note: this environment may print a misleading "User cancelled dialog"; verify with `git status -sb` (expect `## decompile/dy-sync-lib...origin/decompile/dy-sync-lib` with no `ahead`).
- [ ] Update project memory (`project-dysync-security-hardening.md`): seventh slice done, `SyncDecisionHelper` now 11 pure methods, `SyncDecisionHelperTests` 65 cases, full suite 82 green, branch head = new push SHA.

---

## Self-Review

**Spec coverage:**
- New enum `DuplicateVideoAction` → Task 1 Step 1. ✓
- `SyncDecisionHelper.ResolveDuplicateVideoAction` (verbatim port, `VideoType`→`currentType`) → Task 1 Step 2. ✓
- Thin `AutoDistinct` shell, all guards/BRANCH 2/I/O retained → Task 1 Step 3. ✓
- The one deliberate try/catch normalization, recorded in commit message → Task 1 Step 3 notes + Step 6 commit body. ✓
- 8 characterization `[Fact]`s covering 5 leaves + empty-default + `int.MaxValue` fallback → Task 2 Step 1. ✓
- "保留怪癖" (null `priorityLevels` NRE not pinned) → Task 1 Step 2 doc comment + Task 2 Step 1 note. ✓
- `tests/README.md` updates (table row + NOT-covered entry + standalone bullet) → Task 3. ✓
- Build/test via `DOTNET_ROLL_FORWARD=LatestMajor`, explicit `git add <path>`, push not merge → all task steps + Final Steps. ✓

**Placeholder scan:** No TBD/TODO; every code step shows complete code; every command shows expected output. ✓

**Type consistency:** `ResolveDuplicateVideoAction(VideoTypeEnum currentType, VideoTypeEnum exitVideoType, List<PriorityLevelDto> priorityLevels)` and `DuplicateVideoAction { SkipDownload, ReplaceExisting }` — identical across Task 1 (enum, helper, job call) and Task 2 (test calls). The `Levels((int id, int sort)[])` helper signature is consistent across all 5 uses. ✓

**Test trace check:** Each of the 8 tests was hand-traced against the four-level if/else in Task 1 Step 2 — empty-levels tests use the default `{Id=1}`→`dy_favorite`; configured tests compute `maxPriority` as Sort-min; leaf outcomes match. ✓
