# Extract Alternate-URL Collection Logic — Design Spec

**Status:** Approved 2026-05-23

**Slice:** 11th of the `DouyinBasicSyncJob` god-class decomposition campaign.

## Goal

Extract the alternate-URL collection logic from `DouyinBasicSyncJob.SwitchOtherUrlAddressDown` (the double-nested loop that builds `otherUrls` from `item.Video.BitRate[*].PlayAddr.UrlList`) into a pure, independently-testable `SyncDecisionHelper.CollectAlternateVideoUrls`. The job retains a one-line call plus all surrounding logging, the empty-list branch decision, the HTTP `DownloadAsync` retry, and the tri-state return.

## Why now

After slice 10, the remaining seams inside `DouyinBasicSyncJob` are mostly orchestration bodies tightly coupled to HTTP / FS / DB. `SwitchOtherUrlAddressDown` is one of the few methods whose first half is a closed pure transform (BitRate list + one excluded URL → candidate URL list) and whose second half is pure I/O. The seam is unambiguous and the resulting helper has real branching worth pinning — both `||` arms of the `PlayAddr`/`UrlList` guard and the `payurl == excludeUrl` ordinal equality.

Once extracted, `SwitchOtherUrlAddressDown` is left as a thin retry-orchestration shell with no further pure sub-blocks worth extracting (whatever remains is HTTP).

## Sibling pattern

Structurally identical to slice 9 (`BuildDynamicVideoUrls`) and slice 10 (`BuildImageUrls`): pure data → DTO/string-list transform lifted out of an orchestration body. The helper is appended to `SyncDecisionHelper`, the job swaps a multi-line block for a single call, and a new section of golden-master `[Fact]`s pins the helper.

## File-level scope

- **Modify:** `utils/SyncDecisionHelper.cs` — append one pure static method `CollectAlternateVideoUrls`. Existing 14 methods untouched.
- **Modify:** `job/DouyinBasicSyncJob.cs` — `SwitchOtherUrlAddressDown` (lines 870-891, the double-loop building `otherUrls`): replace the 22-line block with a one-line helper call. The rest of the method (`:867-869, 892-914`) is verbatim.
- **Modify:** `tests/dy.net.Tests/SyncDecisionHelperTests.cs` — append one `// ---- CollectAlternateVideoUrls ----` section with 9 `[Fact]`s + 2 section-local helpers.
- **Modify:** `tests/README.md` — record the new pinned coverage.

`SwitchOtherUrlAddressDown` is `private async Task<(bool, DouyinVideo)>` (non-`virtual`, cannot be overridden) with exactly one call site (`:764` from `ProcessSingleVideo`, same file) → job-side change is confined to `DouyinBasicSyncJob.cs`.

## Current state (verbatim — what gets ported)

The block at `job/DouyinBasicSyncJob.cs:870-891`, inside `SwitchOtherUrlAddressDown`'s body, after the opening `Log.Debug`:

```csharp
            var otherUrls = new List<string>();

            foreach (var bit in item.Video.BitRate)
            {
                if (bit == null)
                {
                    continue;
                }
                var payUrls = bit.PlayAddr;

                if (payUrls == null || payUrls.UrlList == null || payUrls.UrlList.Count == 0)
                {
                    continue;
                }

                foreach (var payurl in payUrls.UrlList)
                {
                    if (payurl == videoUrl)//   排除最开始下载失败的视频地址
                        continue;
                    otherUrls.Add(payurl);
                }
            }
```

`videoUrl` here is the parameter of `SwitchOtherUrlAddressDown` (the URL whose initial download failed).

## Helper design

### Signature

```csharp
public static List<string> CollectAlternateVideoUrls(Aweme item, string excludeUrl)
```

### Parameter renaming rationale

The job names the parameter `videoUrl` because in the calling context it's "the video URL that just failed download". In the helper context that semantics is lost — the helper's contract is generic ("collect every candidate URL except this one"). Renaming to `excludeUrl` makes the helper self-documenting. This is the same kind of caller-context-to-generic rename done in slice 1 (`VideoType` abstract prop → `videoType` param) and slice 6 (`VideoType` abstract prop → `videoType` enum param in `BuildCoverPosterPath`).

### Body

Verbatim port of the loop, with three changes:

1. Variable initialization is moved INSIDE the helper (the job no longer keeps the local).
2. Trailing `return otherUrls;` added.
3. The inline `//   排除最开始下载失败的视频地址` comment is preserved verbatim, with its 3 internal spaces — even though the wording references the caller's context, the verbatim-port discipline established in slices 9 and 10 keeps such comments intact (slice 9 also kept its caller-context comment `// 当需要下载动态视频时，获取其他URL` inside the helper).

```csharp
        /// <summary>
        /// 从 DouyinBasicSyncJob.SwitchOtherUrlAddressDown 抽出的纯候选 URL 收集逻辑（无 I/O）。
        /// 行为逐字保留：遍历 item.Video.BitRate，跳过 null 元素与 PlayAddr/UrlList 为 null·空 的项，
        /// 然后对每个 UrlList 中的 URL，与 excludeUrl 做 C# string == 比较（值相等、ordinal、区分大小写）；
        /// 不等者按"BitRate 顺序 → 每 BitRate 内 UrlList 顺序"加入结果，跨 BitRate 的重复 URL 不去重。
        /// 注意 helper 不守护 item.Video / item.Video.BitRate 为 null（原代码亦不守护）→ NRE 直读，由调用方保证。
        /// 由特征化测试 SyncDecisionHelperTests 锁定当前行为。
        /// </summary>
        public static List<string> CollectAlternateVideoUrls(Aweme item, string excludeUrl)
        {
            var otherUrls = new List<string>();

            foreach (var bit in item.Video.BitRate)
            {
                if (bit == null)
                {
                    continue;
                }
                var payUrls = bit.PlayAddr;

                if (payUrls == null || payUrls.UrlList == null || payUrls.UrlList.Count == 0)
                {
                    continue;
                }

                foreach (var payurl in payUrls.UrlList)
                {
                    if (payurl == excludeUrl)//   排除最开始下载失败的视频地址
                        continue;
                    otherUrls.Add(payurl);
                }
            }

            return otherUrls;
        }
```

### Imports

`SyncDecisionHelper.cs` already has `using dy.net.model.response;` (`Aweme`, `Video`, `VideoBitRate`, `PlayAddr` all live there). The project has `<ImplicitUsings>enable</ImplicitUsings>` covering `System.Collections.Generic`. **No new `using` directive is needed.**

## Job-side change

In `job/DouyinBasicSyncJob.cs`, inside `SwitchOtherUrlAddressDown`'s body, replace the 22-line block at lines 870-891 (the `var otherUrls = new List<string>();` declaration through the closing `}` of the outer `foreach`) with this single line at the same 12-space indent:

```csharp
            var otherUrls = SyncDecisionHelper.CollectAlternateVideoUrls(item, videoUrl);
```

Notes:
- The method signature, the opening `Log.Debug` at `:869`, the `if (otherUrls.Count > 0)` HTTP-retry block at `:893-906`, the `else` empty-branch error log + return at `:907-911`, and the trailing `return (flowControl: true, value: null);` at `:913` are ALL verbatim — must NOT be touched.
- `otherUrls` is used again later (`otherUrls.Count > 0` guard, `otherUrls` passed as 4th arg to `DownloadAsync`) — those references read the same local variable and must NOT change.
- 12-space indent matches the surrounding method body.
- The `Log.Debug` at `:895` (commented-out by upstream — `//Log.Debug(...)`) stays as-is.
- Do NOT touch the call site at `:764` (in `ProcessSingleVideo`).

## Load-bearing quirks (preserved + pinned)

1. **`item.Video.BitRate` direct read, no `?.` guard** — if `item.Video` or `item.Video.BitRate` is null, the helper throws `NullReferenceException`. Original code has the same behavior. Preserve verbatim. **Do NOT pin** (consistent with slice 6 / 8 / 9 discipline — don't lock crash paths via `Assert.Throws`; they discourage the obvious future guard fix).
2. **`bit == null` skip** — `BitRate` list may legitimately contain null elements (model is `List<VideoBitRate>` from JSON deserialization). Pin.
3. **`payUrls == null || payUrls.UrlList == null || payUrls.UrlList.Count == 0`** — compound `||` short-circuit guard. Pin all three arms independently.
4. **`payurl == excludeUrl` is C# `string.operator ==`** — value-equal, ordinal, **case-sensitive**. Not `StringComparison.OrdinalIgnoreCase`. Not URL-canonical equality. A future "improvement" to case-insensitive comparison would silently change which URLs survive. Pin with a `DiffersOnlyInCase_NotExcluded` test.
5. **No dedup across BitRates** — if two `VideoBitRate` entries' `UrlList`s contain the same URL and neither equals `excludeUrl`, both copies appear in the result. Pin.
6. **Encounter order** — outer `BitRate` order, then inner `UrlList` order. Pin via `Assert.Collection`.

### Edge cases left UNCOVERED on purpose

- `BitRate == null` (NRE) — not pinned (#1 rule).
- A null entry inside a `UrlList` (`UrlList = [null, "u1"]`): if `excludeUrl` is non-null, `null == excludeUrl` is false → null gets added to the result; if `excludeUrl` is null, the null is skipped. Not pinned — the production data never contains null URLs inside `UrlList`, and the behavior is the bog-standard `string.operator ==` semantics already exercised by the case-sensitivity test. Adding it would be over-coverage.
- `excludeUrl == null`: the `==` operator handles null-LHS and null-RHS the same way (both null → equal; one null → unequal). Not pinned — same reason, this is standard `string.operator ==` semantics already covered by the equality tests.

## Characterization tests

Append a new section `// ---- CollectAlternateVideoUrls ----` to `tests/dy.net.Tests/SyncDecisionHelperTests.cs`, immediately after the last `BuildImageUrls_*` `[Fact]` (the multiple-images encounter-order test) and before the class-closing `}`.

### Section-local helpers

Names deliberately distinct from slice 9 (`DynBitRate`, `DynPlayAddr`, `DynVideo`, `DynImage`, `AwemeWithImages`) and slice 10 (`ImageUrlItem`, `AwemeWithImageItems`):

```csharp
        private static VideoBitRate AltBitRate(params string[] urls)
            => new VideoBitRate { PlayAddr = new PlayAddr { UrlList = urls.ToList() } };

        private static Aweme AwemeWithBitRates(params VideoBitRate[] bits)
            => new Aweme { Video = new Video { BitRate = bits.ToList() } };
```

(`Aweme`, `Video`, `VideoBitRate`, `PlayAddr` all live in `dy.net.model.response` — namespace already imported by the test file. No new `using` needed.)

### 9 `[Fact]`s

| # | Name | Pinned behavior |
| - | ---- | --------------- |
| 1 | `CollectAlternateVideoUrls_BitRateEmpty_ReturnsEmptyList` | Empty `BitRate` → empty non-null list |
| 2 | `CollectAlternateVideoUrls_BitRateContainsNull_SkipsNullEntries` | `bit == null` → continue (alongside one valid BitRate) |
| 3 | `CollectAlternateVideoUrls_PlayAddrNull_SkipsBitRate` | Compound-guard arm 1: `payUrls == null` |
| 4 | `CollectAlternateVideoUrls_UrlListNull_SkipsBitRate` | Compound-guard arm 2: `payUrls.UrlList == null` |
| 5 | `CollectAlternateVideoUrls_UrlListEmpty_SkipsBitRate` | Compound-guard arm 3: `payUrls.UrlList.Count == 0` |
| 6 | `CollectAlternateVideoUrls_MatchesExcludeUrl_Excluded` | Basic ordinal equality match → skip |
| 7 | `CollectAlternateVideoUrls_DiffersOnlyInCase_NotExcluded` | Case-sensitivity quirk pinned (`"UrlA" != "urla"`) |
| 8 | `CollectAlternateVideoUrls_DuplicateUrlsAcrossBitRates_NotDeduplicated` | Same URL in two BitRates → both kept |
| 9 | `CollectAlternateVideoUrls_MultipleBitRatesAndUrls_PreservesEncounterOrder` | Outer BitRate order × inner UrlList order |

Detailed test bodies are deferred to the implementation plan; each `[Fact]` is single-purpose and uses the section-local helpers above (or direct `new VideoBitRate`/`new PlayAddr` for the `PlayAddr=null` and `UrlList=null` cases that the helper can't express via `params string[]`).

After this slice: `SyncDecisionHelper` 14 → 15 pure methods; `SyncDecisionHelperTests` 85 → 94 `[Fact]`s; full `dy.net.Tests` suite 102 → 111 green.

## Documentation updates

`tests/README.md` (two updates, same pattern as slices 9 and 10):

1. Append `CollectAlternateVideoUrls (...)` to the `SyncDecisionHelper` row of the "What is pinned" table.
2. Append a `CollectAlternateVideoUrls` clause to the `DouyinBasicSyncJob` bullet in the "What is intentionally NOT covered" section, describing what's been pinned (the pure URL-collection sub-block) and what remains in the job thin shell (HTTP retry, logging, return tristate).

## Out of scope

- **`BuildDynamicVirtualBitRate`** from `ProcessDynamicVideo` (`:846-854`): contains `DouyinFileUtils.GetTotalFileSize` which reads `FileInfo` from disk — impure, disqualified.
- **`BuildMergeVideoRequest`** from `ProcessImageSetAndMergeToVideo` (`:963-972`): pure but pure object-literal assembly with no decision branching — no interesting test surface, contrived helper.
- **`PickMergeVideoCoverUrl`** from `ProcessImageSetAndMergeToVideo` (`:1006-1008`): genuinely viable (3-line ternary, structurally symmetric to slice 6's `PickCoverUrl`), but deferred to slice 12 to maintain the "one slice = one helper" discipline (slice 6's two helpers were both from the same overload pair; B and D are from different families).
- The HTTP retry / logging / tristate-return orchestration that remains in `SwitchOtherUrlAddressDown` after this slice is intentionally left as a thin shell — its further decomposition would require mocking the HTTP client, outside this campaign's scope.

## Environment + tooling

- Build/test commands MUST be prefixed `DOTNET_ROLL_FORWARD=LatestMajor` (local SDK 10, project targets `net8.0`).
- Commits via `git -c user.name='Claude Code' -c user.email='mjgenab@gmail.com'`; messages append `Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>`.
- Stage only intended files with explicit `git add <path>` — never `git add -A`.
- Push to `origin decompile/dy-sync-lib`; do NOT merge, do NOT open a PR.
