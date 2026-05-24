# Extract Merge-Video Cover-URL Pick Logic — Design Spec

**Status:** Approved 2026-05-24

**Slice:** 12th of the `DouyinBasicSyncJob` god-class decomposition campaign.

## Goal

Extract the 3-line ternary that derives the cover URL inside `DouyinBasicSyncJob.ProcessImageSetAndMergeToVideo` (lines 985-987) into a pure, independently-testable `SyncDecisionHelper.PickMergeVideoCoverUrl(DouyinCollectCate cate, Aweme item, List<DouyinMergeVideoDto> imageUrls) → string`. The job retains the one-line call plus all surrounding I/O (avatar download, virtualBitRate construction, CreateVideoEntity wiring).

## Why now

After slices 9-11 carved three pure sub-blocks out of orchestration bodies, the remaining viable seam inside the still-uncovered orchestration is this small but well-bounded cover-URL ternary. It's the structural sibling of slice 6's `PickCoverUrl` — completing the `cover-decision` family. Outside this seam, the rest of `ProcessImageSetAndMergeToVideo` is either pure I/O (`MergeToVideo`, `DownAuthorAvatar`, `DownVideoCover`, file cleanup, `CreateVideoEntity`) or impure data construction (`virtualBitRate` reads `FileInfo.Length`).

## Sibling pattern

`PickMergeVideoCoverUrl` mirrors slice 6's `PickCoverUrl(DouyinCollectCate cate, Aweme item) → string`:

| | `PickCoverUrl` (slice 6) | `PickMergeVideoCoverUrl` (slice 12) |
| -- | -- | -- |
| Serves | `DownVideoCover` (single / dynamic video) | `ProcessImageSetAndMergeToVideo` (merge video) |
| Cate-branch chain | `MixInfo → Video(Last) → Music` | `MixInfo → imageUrls → Music` |
| Non-cate branch | `Video(First) → Images` | `imageUrls` only |
| Why different middle | Single-video has `item.Video.Cover` | Merge-video synthesizes the video from images → uses pre-built `imageUrls` DTO list |

The cover-decision family is complete at slice 12.

## File-level scope

- **Modify:** `utils/SyncDecisionHelper.cs` — append one pure static method `PickMergeVideoCoverUrl`. Existing 15 methods untouched.
- **Modify:** `job/DouyinBasicSyncJob.cs:985-987` (the 3-line ternary inside `ProcessImageSetAndMergeToVideo`'s `try` block): replace with a one-line helper call.
- **Modify:** `tests/dy.net.Tests/SyncDecisionHelperTests.cs` — append one `// ---- PickMergeVideoCoverUrl ----` section with 9 `[Fact]`s + 2 section-local helpers.
- **Modify:** `tests/README.md` — record the new pinned coverage.

`ProcessImageSetAndMergeToVideo` is `protected async Task<DouyinVideo>` (non-`virtual`, no overrides) with one call site (`:623`, same file, in `ProcessVideoList`) → job-side change is confined to `DouyinBasicSyncJob.cs`.

## Current state (verbatim — what gets ported)

The block at `job/DouyinBasicSyncJob.cs:985-987`, inside `ProcessImageSetAndMergeToVideo`'s `try` body, after the merge-video file-validation branch and before `DownAuthorAvatar`:

```csharp
                var coverUrl = cate is not null && cate.CateType != VideoTypeEnum.dy_custom_collect
              ? (item.MixInfo?.CoverUrl?.UrlList?.FirstOrDefault() ?? imageUrls.FirstOrDefault()?.Path ?? item.Music?.CoverHd?.UrlList?.FirstOrDefault())
              : imageUrls.FirstOrDefault()?.Path;
```

Note the original has irregular indent (`var coverUrl` at 16 spaces, `?` and `:` at 14 spaces — off by 2). When ported into the helper, the body re-indents naturally under `return` at consistent 12 spaces; this is the same cosmetic re-indent allowed in slices 9/10.

## Helper design

### Signature

```csharp
public static string PickMergeVideoCoverUrl(DouyinCollectCate cate, Aweme item, List<DouyinMergeVideoDto> imageUrls)
```

### Body

Verbatim port of the ternary, wrapped in `return`:

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

### Imports

`SyncDecisionHelper.cs` already has all required namespaces (slice 6's `PickCoverUrl` uses `DouyinCollectCate`, `VideoTypeEnum`, `Aweme`, all `?.UrlList?.FirstOrDefault()` chains; slice 10's `BuildImageUrls` uses `DouyinMergeVideoDto`). No new `using`.

## Job-side change

In `job/DouyinBasicSyncJob.cs`, inside `ProcessImageSetAndMergeToVideo`'s `try` body, replace the 3-line ternary at lines 985-987 with this single line at 16-space indent (matching the surrounding `try` block):

```csharp
                var coverUrl = SyncDecisionHelper.PickMergeVideoCoverUrl(cate, item, imageUrls);
```

Notes:
- The method signature, `try`/`catch`, the upstream `imageUrls` build (`:908`) and null/empty guard (`:911-914`), the file-validation branch (`:964-983`), the downstream `DownAuthorAvatar` (`:990`), `virtualBitRate` (`:993-1001`), `DownVideoCover` (`:1003`), `CreateVideoEntity` (`:1006`), the special-field assignment (`:1010-1013`), and the `catch` block are ALL verbatim — do NOT touch.
- `coverUrl` is used at `:1003` as the first positional arg to `DownVideoCover` — must remain unchanged. The variable is still declared inline via `var ... = SyncDecisionHelper...` so its type (`string`) is unchanged.
- The 16-space indent matches the surrounding `try` block.
- Do NOT touch the call site (`:623`).
- The original's irregular `?`/`:` indent disappears (no ternary in the job anymore).

## Load-bearing quirks (preserved + pinned)

1. **Branch decision** — the condition `cate is not null && cate.CateType != VideoTypeEnum.dy_custom_collect` is a 3-way decision: `cate==null` → non-cate; `cate!=null && cate.CateType==dy_custom_collect` → non-cate (the carve-out); other → cate. Pin all 3 cases.
2. **`dy_custom_collect` carve-out** — `cate` may be non-null but its CateType equals `dy_custom_collect`; this is treated identically to `cate==null`. A future "simplification" to `cate is not null ? cate-branch : non-cate-branch` would break this. Pin explicitly.
3. **cate branch fallback chain `MixInfo → imageUrls → Music`** — fully `?.` safe. Any null link in the MixInfo chain (`MixInfo`, `MixInfo.CoverUrl`, `MixInfo.CoverUrl.UrlList`, or empty UrlList → `FirstOrDefault()` returns null) triggers the `??` to `imageUrls`. Pin: MixInfo wins, MixInfo-missing → imageUrls wins, MixInfo+imageUrls-empty → Music wins, all null → null.
4. **Non-cate branch uses only `imageUrls`** — does NOT consult MixInfo (even if cate is null, no MixInfo fallback); does NOT consult Music. A future "consistency" refactor that always falls through to Music would break this. Pin: cate=null with valid Music cover → still returns null when imageUrls empty.
5. **Defensive `FirstOrDefault()?.Path` on imageUrls** — production callers guard imageUrls non-null and non-empty at `:911`, but the helper's contract is "give me a sane answer regardless". `FirstOrDefault()` on empty list returns null (the default for the class); `?.Path` short-circuits to null. Pin empty-imageUrls case for both branches.
6. **`FirstOrDefault()` semantics** — takes the FIRST element by encounter order. Implicit but worth pinning once to lock the ordering.

### Edge cases left UNCOVERED on purpose

- `imageUrls == null` (NRE inside helper: `imageUrls.FirstOrDefault()` throws on null reference) — not pinned (#1 rule — don't lock crash paths). Caller's `:911` guard ensures non-null; helper's contract is "imageUrls non-null".
- `item == null` (NRE on `item.MixInfo`) — same reason.
- `item.MixInfo.CoverUrl.UrlList` containing a null entry — `FirstOrDefault()` returns null → falls through to `??`. Standard nullable semantics. Not pinned (over-coverage; production never produces this).

## Characterization tests

Append a new section `// ---- PickMergeVideoCoverUrl ----` to `tests/dy.net.Tests/SyncDecisionHelperTests.cs`, immediately after the last `CollectAlternateVideoUrls_*` `[Fact]` (the multiple-BitRates encounter-order test) and before the class-closing `}`.

### Section-local helpers

Names deliberately distinct from prior slices' helpers (`DynBitRate`/`DynPlayAddr`/`DynVideo`/`DynImage`/`AwemeWithImages` slice 9; `ImageUrlItem`/`AwemeWithImageItems` slice 10; `AltBitRate`/`AwemeWithBitRates` slice 11):

```csharp
        private static DouyinMergeVideoDto MergeDto(string path)
            => new DouyinMergeVideoDto { Path = path };

        private static DouyinCollectCate CateOfType(VideoTypeEnum t)
            => new DouyinCollectCate { CateType = t };
```

`DouyinCollectCate` lives in `dy.net.model.entity` (or wherever slice 6 imports from — the test file already imports it for `PickCoverUrl` tests). `VideoTypeEnum`, `Aweme`, `DouyinMergeVideoDto` all already imported.

### 9 `[Fact]`s

| # | Name | Pinned behavior |
| - | ---- | --------------- |
| 1 | `PickMergeVideoCoverUrl_CateNull_UsesNonCateBranch_ReturnsFirstImageUrlPath` | Branch decision (cate=null) + non-cate happy path |
| 2 | `PickMergeVideoCoverUrl_CateCustomCollect_UsesNonCateBranch_ReturnsFirstImageUrlPath` | `dy_custom_collect` carve-out |
| 3 | `PickMergeVideoCoverUrl_CateNonCustomCollect_MixInfoPresent_PrefersMixInfo` | cate branch arm 1 |
| 4 | `PickMergeVideoCoverUrl_CateNonCustomCollect_MixInfoNull_FallsBackToImageUrls` | cate branch arm 2 (MixInfo=null) |
| 5 | `PickMergeVideoCoverUrl_CateNonCustomCollect_MixInfoUrlListEmpty_FallsBackToImageUrls` | cate branch arm 2 (chain non-null but UrlList empty → FirstOrDefault=null) |
| 6 | `PickMergeVideoCoverUrl_CateNonCustomCollect_MixInfoAndImageUrlsEmpty_FallsBackToMusic` | cate branch arm 3 (Music fallback) |
| 7 | `PickMergeVideoCoverUrl_CateNonCustomCollect_AllSourcesNull_ReturnsNull` | cate branch all-null → null |
| 8 | `PickMergeVideoCoverUrl_CateNull_EmptyImageUrls_ReturnsNull` | non-cate branch with empty imageUrls → null (defensive contract) |
| 9 | `PickMergeVideoCoverUrl_CateNonCustomCollect_ImageUrlsHasMultiple_PrefersFirstNotMusic` | `FirstOrDefault()` order; also confirms imageUrls beats Music when MixInfo is null |

Detailed test bodies deferred to the implementation plan. Each test is single-purpose; complex setups (cate construction, MixInfo nesting, Aweme with Music) use the section-local helpers or direct `new` initializers.

After this slice: `SyncDecisionHelper` 15 → 16 pure methods; `SyncDecisionHelperTests` 94 → 103 `[Fact]`s; full `dy.net.Tests` suite 111 → 120 green.

## Documentation updates

`tests/README.md` (two updates, same pattern as slices 9-11):

1. Append `PickMergeVideoCoverUrl (...)` to the `SyncDecisionHelper` row of the "What is pinned" table.
2. Append a `PickMergeVideoCoverUrl` clause to the `DouyinBasicSyncJob` bullet in the "What is intentionally NOT covered" section.

## Out of scope

- **`BuildMergeVideoBitRate`** from `ProcessImageSetAndMergeToVideo` (`:993-1001`): contains `File.Exists` + `new FileInfo(savePath).Length` — impure FS reads, disqualified. The analogous block in `ProcessDynamicVideo` (`:846-854`) is also impure via `DouyinFileUtils.GetTotalFileSize`.
- **`MediaMergeRequest` construction** (`:942-951`): pure but pure object-literal assembly with no decision branching — no interesting test surface, contrived helper. Slice 11 brainstorming already excluded for the same reason.
- After slice 12, `ProcessImageSetAndMergeToVideo`'s residual body is pure orchestration (FS / HTTP / DB) and has no further extractable pure seams. The next slice candidate would need to come from `ProcessSingleVideo`/`ProcessDynamicVideo`/`ProcessVideoList`/`GetAndSaveViedos` or be a residual sub-method (`DownAuthorAvatar`, `DownVideoCover`).

## Environment + tooling

- Build/test commands MUST be prefixed `DOTNET_ROLL_FORWARD=LatestMajor` (local SDK 10, project targets `net8.0`).
- Commits via `git -c user.name='Claude Code' -c user.email='mjgenab@gmail.com'`; messages append `Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>`.
- Stage only intended files with explicit `git add <path>` — never `git add -A`.
- Push to `origin decompile/dy-sync-lib`; do NOT merge, do NOT open a PR.
