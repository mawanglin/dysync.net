# Extract Alternate-URL Collection Logic — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Extract the alternate-URL collection logic (the double-nested loop building `otherUrls`) from `DouyinBasicSyncJob.SwitchOtherUrlAddressDown` into a pure, independently-testable `SyncDecisionHelper.CollectAlternateVideoUrls(Aweme item, string excludeUrl) → List<string>`, leaving all logging / HTTP retry / tri-state return in the job.

**Architecture:** Behavior-preserving "thin shell" extraction (11th god-class slice). Direct sibling of slice 9's `BuildDynamicVideoUrls` and slice 10's `BuildImageUrls`: a pure data → list transform of `item.Video.BitRate` (filtering null bits, null/empty `PlayAddr.UrlList`, and the caller-provided `excludeUrl`) moves to `SyncDecisionHelper`; the job keeps a one-line call plus all surrounding I/O. Golden-master characterization tests pin the helper. No new file, no new enum.

**Tech Stack:** .NET 8 (`net8.0`; local SDK 10 → all `dotnet` commands prefixed `DOTNET_ROLL_FORWARD=LatestMajor`), xUnit (`tests/dy.net.Tests`), C#.

**Spec:** `docs/superpowers/specs/2026-05-23-extract-collectalternatevideourls-design.md`

---

## File Structure

- **Modify:** `utils/SyncDecisionHelper.cs` — append one pure method `CollectAlternateVideoUrls`; existing 14 methods untouched.
- **Modify:** `job/DouyinBasicSyncJob.cs:870-891` (the 22-line double-loop inside `SwitchOtherUrlAddressDown`): replace with a one-line helper call; the rest of the method verbatim.
- **Modify:** `tests/dy.net.Tests/SyncDecisionHelperTests.cs` — append one `// ---- CollectAlternateVideoUrls ----` section (9 `[Fact]` + 2 section-local helpers).
- **Modify:** `tests/README.md` — record the new pinned coverage.

`SwitchOtherUrlAddressDown` is `private async Task<(bool, DouyinVideo)>` (non-`virtual`, cannot be overridden) with one call site (`:764` in `ProcessSingleVideo`) → job-side change is confined to `DouyinBasicSyncJob.cs`.

---

## Task 1: Extract `CollectAlternateVideoUrls` + thin `SwitchOtherUrlAddressDown`

**Files:**
- Modify: `utils/SyncDecisionHelper.cs` (append before the class-closing `}` — after the existing last method `BuildImageUrls`)
- Modify: `job/DouyinBasicSyncJob.cs:870-891` (the double-loop inside `SwitchOtherUrlAddressDown`'s body)

- [ ] **Step 1: Append `CollectAlternateVideoUrls` to `SyncDecisionHelper`**

In `utils/SyncDecisionHelper.cs`, insert this method immediately after `BuildImageUrls`'s closing `}` and before the class-closing `}`. The file currently ends with `BuildImageUrls`'s body (`            return item.Images?\n                .Where(...)\n                ...\n                .ToList();\n        }`), then `    }` (class close), then `}` (namespace close). Insert between the method close and the class close:

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

Notes:
- This is a verbatim port of the loop at `job/DouyinBasicSyncJob.cs:870-891`. The only changes vs the original block:
  1. `var otherUrls` declaration is moved INSIDE the helper (the job no longer keeps the local; it's reassigned via the helper call).
  2. The job's parameter name `videoUrl` is renamed to `excludeUrl` in the helper signature + the one comparison site (`if (payurl == excludeUrl)`) — caller-context-to-generic rename, same pattern as slice 1's `videoType` lift.
  3. Trailing `return otherUrls;` added.
- The inline `//   排除最开始下载失败的视频地址` comment is preserved verbatim with its 3 internal spaces — same verbatim-port discipline as slice 9's `// 当需要下载动态视频时，获取其他URL` (caller-context comment kept inside the helper).
- `item.Video.BitRate` is read with NO `?.` guard, mirroring the original — if `item.Video` or `item.Video.BitRate` is null the helper throws NRE. Preserved deliberately; not pinned (consistent with slice 6/8 discipline — don't lock crash paths via `Assert.Throws`).
- `SyncDecisionHelper.cs` already has `using dy.net.model.response;` (`Aweme`, `Video`, `VideoBitRate`, `PlayAddr` all live there); the project has `<ImplicitUsings>enable</ImplicitUsings>` covering `System.Collections.Generic`. Do NOT add any new `using`. No new file, no new enum.
- Match the 8-space method indent of the surrounding methods exactly.

- [ ] **Step 2: Thin the double-loop in `SwitchOtherUrlAddressDown`**

In `job/DouyinBasicSyncJob.cs`, inside `SwitchOtherUrlAddressDown`'s body, replace the 22-line block at lines 870-891. **Read the method first to confirm the exact text** before editing. The block to replace is currently (12-space indent — sits inside the method body, after the opening `Log.Debug` at `:869`):

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

Replace it with the single line at the same 12-space indent:

```csharp
            var otherUrls = SyncDecisionHelper.CollectAlternateVideoUrls(item, videoUrl);
```

Notes:
- This is an in-method local block edit, NOT a whole-method replacement. `SwitchOtherUrlAddressDown`'s signature (`:867`), the opening `Log.Debug` (`:869`), the `if (otherUrls.Count > 0)` HTTP-retry block (`:893-906` post-edit will shift to nearby lines), the `else` empty-branch error-log + return, and the trailing `return (flowControl: true, value: null);` are ALL verbatim — do NOT touch them.
- `otherUrls` is used again later (the `if (otherUrls.Count > 0)` guard, the 4th positional arg to `DownloadAsync`) — those references read the same local variable and must NOT change. The variable is still declared inline via `var ... = SyncDecisionHelper...` so its type (`List<string>`) is unchanged.
- The 12-space indent must match the surrounding method-body code exactly.
- The job's local parameter is still named `videoUrl` (the method signature is verbatim) — only inside the helper is the parameter named `excludeUrl`. The call passes `videoUrl` positionally.
- Do NOT touch the call site of `SwitchOtherUrlAddressDown` (`:764` in `ProcessSingleVideo`).

- [ ] **Step 3: Build — verify 0 errors**

Run: `DOTNET_ROLL_FORWARD=LatestMajor dotnet build dy.net.csproj`
Expected: `Build succeeded. 0 Error(s)`.

(The local SDK is 10 and the project targets net8.0 — the `DOTNET_ROLL_FORWARD=LatestMajor` prefix is REQUIRED on every `dotnet` command or the build/test host fails to launch.)

- [ ] **Step 4: Run the existing suite — verify still green**

Run: `DOTNET_ROLL_FORWARD=LatestMajor dotnet test tests/dy.net.Tests/dy.net.Tests.csproj`
Expected: `Passed!  - Failed: 0` — **102 passed** (no new tests yet; the extraction must not break the existing golden masters).

- [ ] **Step 5: Commit**

Stage ONLY the two files — explicit paths, never `git add -A`:

```bash
git add utils/SyncDecisionHelper.cs job/DouyinBasicSyncJob.cs
git -c user.name='Claude Code' -c user.email='mjgenab@gmail.com' commit -m "$(cat <<'EOF'
refactor(job): extract alternate-URL collection to SyncDecisionHelper

Move the double-nested loop that builds the alternate-URL candidate list
(item.Video.BitRate[*].PlayAddr.UrlList, minus the originally-failed URL)
out of DouyinBasicSyncJob.SwitchOtherUrlAddressDown into a pure
SyncDecisionHelper.CollectAlternateVideoUrls(Aweme item, string
excludeUrl) -> List<string>. The job keeps the opening Log.Debug, the
HTTP DownloadAsync retry inside the otherUrls.Count > 0 guard, the
empty-branch error log, and the tristate return.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 2: Characterization tests for `CollectAlternateVideoUrls`

**Files:**
- Modify: `tests/dy.net.Tests/SyncDecisionHelperTests.cs` (append a new section before the class-closing `}`)

Golden-master tests pinning the helper's CURRENT behavior. The helper is a verbatim port, so first-run values ARE the golden values.

- [ ] **Step 1: Append the test section**

In `tests/dy.net.Tests/SyncDecisionHelperTests.cs`, insert the following block immediately after the last test method (`BuildImageUrls_MultipleImagesMixed_KeepsOnlyValidInEncounterOrder` — its closing `}` is currently the last `}` before the class-closing `}`) and before the class-closing `}`:

```csharp

        // ---- CollectAlternateVideoUrls ----
        // pin: current behavior, not aspirational

        private static VideoBitRate AltBitRate(params string[] urls)
            => new VideoBitRate { PlayAddr = new PlayAddr { UrlList = urls.ToList() } };

        private static Aweme AwemeWithBitRates(params VideoBitRate[] bits)
            => new Aweme { Video = new Video { BitRate = bits.ToList() } };

        [Fact]
        public void CollectAlternateVideoUrls_BitRateEmpty_ReturnsEmptyList()
        {
            // empty BitRate → outer foreach runs 0 times → empty non-null list
            var item = AwemeWithBitRates();
            var result = SyncDecisionHelper.CollectAlternateVideoUrls(item, "any");
            Assert.Empty(result);
        }

        [Fact]
        public void CollectAlternateVideoUrls_BitRateContainsNull_SkipsNullEntries()
        {
            // bit == null → first guard continues; the valid bit's "u1" is still collected
            var item = AwemeWithBitRates((VideoBitRate)null, AltBitRate("u1"));
            var result = SyncDecisionHelper.CollectAlternateVideoUrls(item, "exclude-me");
            Assert.Equal(new[] { "u1" }, result);
        }

        [Fact]
        public void CollectAlternateVideoUrls_PlayAddrNull_SkipsBitRate()
        {
            // payUrls == null → compound guard arm 1 → BitRate skipped
            var item = AwemeWithBitRates(
                new VideoBitRate { PlayAddr = null },
                AltBitRate("u1"));
            var result = SyncDecisionHelper.CollectAlternateVideoUrls(item, "exclude-me");
            Assert.Equal(new[] { "u1" }, result);
        }

        [Fact]
        public void CollectAlternateVideoUrls_UrlListNull_SkipsBitRate()
        {
            // payUrls.UrlList == null → compound guard arm 2 → BitRate skipped
            var item = AwemeWithBitRates(
                new VideoBitRate { PlayAddr = new PlayAddr { UrlList = null } },
                AltBitRate("u1"));
            var result = SyncDecisionHelper.CollectAlternateVideoUrls(item, "exclude-me");
            Assert.Equal(new[] { "u1" }, result);
        }

        [Fact]
        public void CollectAlternateVideoUrls_UrlListEmpty_SkipsBitRate()
        {
            // payUrls.UrlList.Count == 0 → compound guard arm 3 → BitRate skipped
            var item = AwemeWithBitRates(AltBitRate(), AltBitRate("u1"));
            var result = SyncDecisionHelper.CollectAlternateVideoUrls(item, "exclude-me");
            Assert.Equal(new[] { "u1" }, result);
        }

        [Fact]
        public void CollectAlternateVideoUrls_MatchesExcludeUrl_Excluded()
        {
            // payurl == excludeUrl (C# string == is ordinal value equality) → skip;
            // the other URL is kept.
            var item = AwemeWithBitRates(AltBitRate("u1", "u2"));
            var result = SyncDecisionHelper.CollectAlternateVideoUrls(item, "u1");
            Assert.Equal(new[] { "u2" }, result);
        }

        [Fact]
        public void CollectAlternateVideoUrls_DiffersOnlyInCase_NotExcluded()
        {
            // payurl differs from excludeUrl only by case → C# string == is case-sensitive
            // ordinal, so the URL is NOT excluded — "UrlA" stays in the result even though
            // excludeUrl is "urla".
            var item = AwemeWithBitRates(AltBitRate("UrlA"));
            var result = SyncDecisionHelper.CollectAlternateVideoUrls(item, "urla");
            Assert.Equal(new[] { "UrlA" }, result);
        }

        [Fact]
        public void CollectAlternateVideoUrls_DuplicateUrlsAcrossBitRates_NotDeduplicated()
        {
            // Two BitRates contain the same URL; neither equals excludeUrl → both copies kept.
            var item = AwemeWithBitRates(AltBitRate("u1"), AltBitRate("u1"));
            var result = SyncDecisionHelper.CollectAlternateVideoUrls(item, "exclude-me");
            Assert.Equal(new[] { "u1", "u1" }, result);
        }

        [Fact]
        public void CollectAlternateVideoUrls_MultipleBitRatesAndUrls_PreservesEncounterOrder()
        {
            // Outer BitRate order × inner UrlList order: b1.[a, b, skip] then b2.[c, d]
            // → result is [a, b, c, d] (the mid-stream "skip" matches excludeUrl and is dropped).
            var item = AwemeWithBitRates(
                AltBitRate("a", "b", "skip"),
                AltBitRate("c", "d"));
            var result = SyncDecisionHelper.CollectAlternateVideoUrls(item, "skip");
            Assert.Equal(new[] { "a", "b", "c", "d" }, result);
        }
```

Notes:
- `Aweme`, `Video`, `VideoBitRate`, `PlayAddr` are all in `dy.net.model.response`; the namespace is already imported at the top of the file. `System.Linq` (`.ToList()`) resolves via `<ImplicitUsings>enable</ImplicitUsings>`. Do NOT add any `using` directives.
- The names `AltBitRate`, `AwemeWithBitRates` are new section-local helpers, deliberately distinct from slice 9's `DynBitRate`/`DynPlayAddr`/`DynVideo`/`DynImage`/`AwemeWithImages` and slice 10's `ImageUrlItem`/`AwemeWithImageItems`. BEFORE inserting, run `grep -n "AltBitRate\|AwemeWithBitRates" tests/dy.net.Tests/SyncDecisionHelperTests.cs`. If either name already exists, rename the new helper consistently across all its uses and report the rename.
- The `(VideoBitRate)null` cast in test #2 is required: `params VideoBitRate[]` with a bare `null` arg in the first position would be ambiguous to the C# compiler (the whole array could be null vs a one-element array with a null element). The explicit cast forces the latter.
- Tests #3 and #4 cannot use the `AltBitRate` helper because `params string[] urls` cannot express a null `PlayAddr` or a null `UrlList`; they use direct `new VideoBitRate { ... }` construction. Test #5 uses `AltBitRate()` (zero args → empty `UrlList`).
- `Assert.Equal(new[] { ... }, result)` works on `List<string>` because xUnit's `Assert.Equal<T>(IEnumerable<T>, IEnumerable<T>)` overload does element-by-element ordered comparison. This is the strongest assertion form for the ordered-output tests in this section (it pins both elements and order in one call) and is consistent with the slice-10 mixed test's use of `Assert.Collection`.
- Match the indentation of the surrounding test methods exactly (8-space method indent inside the class).
- Do NOT modify any existing test or the helper. Test-only change.

- [ ] **Step 2: Run the new section — verify all 9 pass**

Run: `DOTNET_ROLL_FORWARD=LatestMajor dotnet test tests/dy.net.Tests/dy.net.Tests.csproj --filter "FullyQualifiedName~CollectAlternateVideoUrls"`
Expected: `Passed!  - Failed: 0` — **9 passed**.

If any fails: the helper is a verbatim port, so a failure means the test input was mis-traced. Re-trace by hand against the helper logic; fix the TEST input/expectation. Do NOT modify the helper. Never weaken an assertion.

- [ ] **Step 3: Run the full suite — verify 111 green**

Run: `DOTNET_ROLL_FORWARD=LatestMajor dotnet test tests/dy.net.Tests/dy.net.Tests.csproj`
Expected: `Passed!  - Failed: 0` — **111 passed** (102 + 9).

- [ ] **Step 4: Commit**

Stage ONLY the test file:

```bash
git add tests/dy.net.Tests/SyncDecisionHelperTests.cs
git -c user.name='Claude Code' -c user.email='mjgenab@gmail.com' commit -m "$(cat <<'EOF'
test: golden-master tests pinning CollectAlternateVideoUrls

9 characterization [Fact]s: empty BitRate → empty list, null entries in
BitRate skipped, the three arms of the PlayAddr/UrlList compound guard
each independently pinned, basic ordinal equality match excludes the
URL, case-sensitivity quirk pinned ("UrlA" not excluded by "urla"),
duplicate URLs across BitRates kept (no dedup), and multi-BitRate
multi-UrlList encounter order preserved. Full suite 102 → 111.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 3: Update `tests/README.md` coverage doc

**Files:**
- Modify: `tests/README.md`

- [ ] **Step 1: Add `CollectAlternateVideoUrls` to the `SyncDecisionHelper` table row**

In `tests/README.md`, the "What is pinned" table has one row for `SyncDecisionHelper` (line 24). It currently ends with this exact item (the last entry before the closing ` |`):

```
`BuildImageUrls` (图片 URL 提取：遍历 Images 取每张图 UrlList 首个 URL + 宽高构造 DouyinMergeVideoDto / UrlList 空或首 URL 空白→滤除 / Images=null→null、Images=空→空 list / 多图保序) |
```

Append `CollectAlternateVideoUrls` before the closing ` |` — i.e. replace that exact text with:

```
`BuildImageUrls` (图片 URL 提取：遍历 Images 取每张图 UrlList 首个 URL + 宽高构造 DouyinMergeVideoDto / UrlList 空或首 URL 空白→滤除 / Images=null→null、Images=空→空 list / 多图保序), `CollectAlternateVideoUrls` (候选视频 URL 收集：遍历 item.Video.BitRate，跳过 null bit 与 PlayAddr/UrlList null·空 / payurl == excludeUrl ordinal 区分大小写 / 跨 BitRate 不去重 / 双层保序) |
```

- [ ] **Step 2: Update the "What is intentionally NOT covered" `DouyinBasicSyncJob` entry**

In the "## What is intentionally NOT covered (and why)" section, the first bullet (`**`DouyinBasicSyncJob` orchestration**`) currently ends its `BuildImageUrls` clause with this exact span (read the bullet first to confirm — it spans two lines in the file, lines 70-71):

```
  `CreateVideoEntity` 调用与特殊字段赋值、整体 `try/catch` 仍在 job、未覆盖) — all pinned (see table
```

Replace that exact span with (this appends a new `CollectAlternateVideoUrls` clause after the `BuildImageUrls` clause's close-paren, then resumes the original ` — all pinned (see table` text):

```
  `CreateVideoEntity` 调用与特殊字段赋值、整体 `try/catch` 仍在 job、未覆盖),
  `CollectAlternateVideoUrls` (`SwitchOtherUrlAddressDown` 的候选 URL 收集双循环已抽出并
  pinned；其开头 `Log.Debug`、`otherUrls.Count > 0` 守卫下的 `DownloadAsync` HTTP 重试、
  空分支错误日志、tristate `(flowControl, value)` 返回仍在 job 薄壳内、未覆盖；
  `item.Video` / `item.Video.BitRate` 为 null 的 NRE 路径保留不测) — all pinned (see table
```

The ` — all pinned (see table` suffix occurs exactly once in the file, so this span is a unique match. Do NOT change the "Still uncovered:" list that follows.

- [ ] **Step 3: Verify the doc reads correctly**

Run: `grep -n "CollectAlternateVideoUrls" tests/README.md`
Expected: 2 matches (the table row + the NOT-covered entry).

Run: `grep -c "all pinned (see table" tests/README.md`
Expected: `1` (the suffix should remain unique).

Run: `DOTNET_ROLL_FORWARD=LatestMajor dotnet test tests/dy.net.Tests/dy.net.Tests.csproj`
Expected: `Passed!  - Failed: 0` — **111 passed** (a doc change must not affect the build/tests).

- [ ] **Step 4: Commit**

Stage `tests/README.md` and this plan file:

```bash
git add tests/README.md docs/superpowers/plans/2026-05-23-extract-collectalternatevideourls.md
git -c user.name='Claude Code' -c user.email='mjgenab@gmail.com' commit -m "$(cat <<'EOF'
docs: pin CollectAlternateVideoUrls coverage in tests/README

Also commits the eleventh-slice implementation plan.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Final Steps (after all tasks)

- [ ] Push the commit chain to origin: `git push origin decompile/dy-sync-lib` — **do NOT merge, do NOT open a PR** (standing constraint). This environment may print a misleading "User cancelled dialog" or a transient TLS handshake error (`GnuTLS, handshake failed`) — retry up to 3×; verify the true state with `git status -sb` (expect `## decompile/dy-sync-lib...origin/decompile/dy-sync-lib` with no `ahead`).
- [ ] Update project memory (`project-dysync-security-hardening.md`): eleventh slice done, `SyncDecisionHelper` now 15 pure methods, `SyncDecisionHelperTests` 94 cases, full suite 111 green, branch head = new push SHA.

---

## Self-Review

**Spec coverage:**
- `SyncDecisionHelper.CollectAlternateVideoUrls` (verbatim port; `videoUrl` → `excludeUrl` rename; trailing `return`) → Task 1 Step 1. ✓
- Thin `SwitchOtherUrlAddressDown` block, `otherUrls` usage downstream retained → Task 1 Step 2. ✓
- The inline `//   排除最开始下载失败的视频地址` comment preserved verbatim inside the helper → Task 1 Step 1 (literal code block). ✓
- "Quirk 1" — `item.Video.BitRate` no-guard NRE preserved, not pinned → Task 1 Step 1 notes. ✓
- "Quirk 2" — `bit == null` skip → Task 2 test `BitRateContainsNull_SkipsNullEntries`. ✓
- "Quirk 3" — three arms of compound guard, pinned independently → Task 2 tests `PlayAddrNull_SkipsBitRate` / `UrlListNull_SkipsBitRate` / `UrlListEmpty_SkipsBitRate`. ✓
- "Quirk 4" — `string ==` ordinal case-sensitive → Task 2 tests `MatchesExcludeUrl_Excluded` + `DiffersOnlyInCase_NotExcluded`. ✓
- "Quirk 5" — no dedup across BitRates → Task 2 test `DuplicateUrlsAcrossBitRates_NotDeduplicated`. ✓
- "Quirk 6" — encounter order → Task 2 test `MultipleBitRatesAndUrls_PreservesEncounterOrder`. ✓
- 9 characterization `[Fact]`s with 2 section-local helpers → Task 2 Step 1. ✓
- `tests/README.md` updates (table row + NOT-covered clause) → Task 3 Steps 1-2. ✓
- Build/test via `DOTNET_ROLL_FORWARD=LatestMajor`, explicit `git add <path>`, push not merge → all task steps + Final Steps. ✓

**Placeholder scan:** No TBD/TODO; every code step shows complete code; every command shows expected output. ✓

**Type consistency:** `CollectAlternateVideoUrls(Aweme item, string excludeUrl)` returning `List<string>` — identical across Task 1 (helper, job call passes positional `videoUrl`) and Task 2 (9 test calls). Test helpers `AltBitRate(params string[]) → VideoBitRate` and `AwemeWithBitRates(params VideoBitRate[]) → Aweme` are consistent across all uses. `VideoBitRate.PlayAddr`, `PlayAddr.UrlList`, `Aweme.Video`, `Video.BitRate` types verified against `model/response/DouyinVideoInfoResponse.cs` (line 328: `public Video Video`; line 1144: `public List<VideoBitRate> BitRate`; line 1219: `public PlayAddr PlayAddr`; line 1252: `public List<string> UrlList`). ✓

**Test trace check:**
- BitRateEmpty — `BitRate=[]` → outer `foreach` 0 iter → `otherUrls=[]`. ✓
- BitRateContainsNull — `BitRate=[null, AltBitRate("u1")]` → bit#0 `null` continue; bit#1 → inner `["u1" == "exclude-me"]=false` → add → `["u1"]`. ✓
- PlayAddrNull — `BitRate=[{PlayAddr=null}, AltBitRate("u1")]` → bit#0 `payUrls==null` continue; bit#1 → `["u1"]`. ✓
- UrlListNull — `BitRate=[{PlayAddr={UrlList=null}}, AltBitRate("u1")]` → bit#0 `payUrls.UrlList==null` continue; bit#1 → `["u1"]`. ✓
- UrlListEmpty — `BitRate=[AltBitRate(), AltBitRate("u1")]` → bit#0 `payUrls.UrlList.Count==0` continue; bit#1 → `["u1"]`. ✓
- MatchesExcludeUrl — `BitRate=[AltBitRate("u1","u2")]`, exclude="u1" → "u1"==exclude→skip; "u2"≠exclude→add → `["u2"]`. ✓
- DiffersOnlyInCase — `BitRate=[AltBitRate("UrlA")]`, exclude="urla" → "UrlA"=="urla" is FALSE (C# `string==` is ordinal case-sensitive) → add → `["UrlA"]`. ✓
- DuplicateUrlsAcrossBitRates — `BitRate=[AltBitRate("u1"), AltBitRate("u1")]`, exclude≠"u1" → bit#0 → add "u1"; bit#1 → add "u1" → `["u1","u1"]`. ✓
- MultipleBitRatesAndUrls — `BitRate=[AltBitRate("a","b","skip"), AltBitRate("c","d")]`, exclude="skip" → bit#0: "a"≠skip→add, "b"≠skip→add, "skip"==skip→continue; bit#1: "c"→add, "d"→add → `["a","b","c","d"]`. ✓
