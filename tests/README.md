# dy.net Characterization Test Suite

These are **golden-master / characterization tests**. They do not assert what the
code *should* do — they pin what it *currently* does, so the planned
WARNING-tier refactors (god-class split, in-memory→SQL aggregation rewrite,
async cleanup) can be proven non-behavior-changing.

## How to run

```bash
DOTNET_ROLL_FORWARD=LatestMajor dotnet test tests/dy.net.Tests/dy.net.Tests.csproj
```

(`DOTNET_ROLL_FORWARD=LatestMajor` only because the local box has SDK 10 and the
projects target net8.0; drop it on a net8.0 SDK.) Filter a single class with
`--filter <ClassName>`.

## What is pinned

| Area | Test | Locks |
|------|------|-------|
| `DouyinFileNameHelper` | `DouyinFileNameHelperTests` | `SanitizeLinuxFileName` (illegal-char scrub, whitespace strip, folder mode, control chars), `KeepChineseLettersAndNumbers`, `RemoveNumberSuffix`, `LimitUnifiedCount` |
| `Md5Util.Md5` | `PureHelperTests` | Standard RFC-1321 MD5 output |
| `SyncDecisionHelper` | `SyncDecisionHelperTests` | `GetNextCursor` (Cursor→MaxCursor→`"0"`, null-safe), `IsAwemeValid` (3-level null guard), `GetVideoTags` (per-level pick, missing→null), `IsSyncLimitReached` (cate 30-cap vs `BatchCount` cap, `OnlySyncNew` passthrough, `dy_follows` `!FullSync`, mix/series short-circuit) |
| `VideoTitleGenerator.Generate` | `PureHelperTests` | Placeholder substitution, char-filtering of title/author, unknown-token passthrough, empty-field placeholder, 60-char cap |
| `DouyinVideoService.GetStatics` | `VideoStatsCharacterizationTests` | Full `VideoStaticsDto` snapshot: counts by type, distinct author/category, GB size formatting incl. the `<0.01` zero-substitution branch, **plus the `Categories` list (Tag1 grouping, empty→`其他`, desc order) and `Authors` list (Author grouping, desc order, last-row `Icon`/`UperId` semantics)** |
| `DouyinVideoService.GetChartData` | `VideoStatsCharacterizationTests` | Per-day `SyncTime` grouping and per-type counts (Graphic = empty `FileHash`), **single-day and multi-day group ordering** |

> These tests were deliberately widened (Categories/Authors/multi-day) before
> the in-memory→SQL aggregation refactor, then `GetStatics`/`GetChartData` were
> rewritten to push counts/sums to SQL and use narrow column projections
> instead of `GetAllAsync()`. The suite staying green is the proof that the
> refactor changed performance only, not behavior.

DB-bound tests run against a real temporary SQLite file built by SqlSugar
CodeFirst (`TestDb`), i.e. the production data stack — not mocks.

## What is intentionally NOT covered (and why)

- **`DouyinBasicSyncJob` orchestration** — HTTP + filesystem + DB coupled with
  no seams. Its side-effect-free decision logic has been extracted to
  `SyncDecisionHelper` and is now pinned (see table above); the remaining
  orchestration/HTTP/FS/DB body stays uncovered until further seams are
  extracted in follow-up plans.
- **`AutoDistinct`** — private, instance-level, DB-coupled. Not directly
  reachable. Its supporting pure helpers (above) are pinned; characterize
  `AutoDistinct` itself only after it is extracted to a testable seam.
- **Frontend (`http.ts` interceptor, `account.ts`)** — no JS toolchain in this
  environment; out of scope for this .NET suite.

## Refactor-safety rule

**A refactor change is only safe if `dotnet test` stays green.**

If a refactor *legitimately* changes observable behavior, update the affected
golden value **in the same commit**, with a one-line justification in the commit
message explaining why the new value is correct. Never weaken or delete an
assertion just to make a refactor pass — that defeats the purpose of the
safety net.
