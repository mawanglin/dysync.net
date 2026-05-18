# Characterization Test Foundation Implementation Plan

> Execute task-by-task. Build/run after each task. This plan delivers ONLY the test safety-net. The actual refactors (god-class split, query→SQL rewrite) are SEPARATE follow-up plans, each gated on these tests being green.

**Goal:** Stand up an xUnit test project and pin the *current observable behavior* (golden-master / characterization tests) of the pure logic a refactor would extract, plus the DB-aggregation methods a refactor would rewrite — so later refactors can be verified non-behavior-changing.

**Architecture:** New `tests/dy.net.Tests` xUnit project (net8.0) with a `ProjectReference` to the web project. Pure helpers tested directly. DB-bound methods tested against a real temporary SQLite database via SqlSugar CodeFirst (faithful to the production stack). The main csproj glob is fixed so test sources are NOT compiled into the web app.

**Tech Stack:** xUnit, Microsoft.NET.Test.Sdk, SqlSugarCore (SQLite), .NET 8 / SDK 10.

**Characterization method:** tests call the unit with fixed inputs and assert equality with a constant. The constant is the *observed current output* (captured by running once). This is golden-master testing, not a placeholder — each task explicitly has a "run, observe, lock the value" step.

**Out of scope (later plans, now protected):** rewriting `GetStatics`/`GetChartData` to SQL aggregation; splitting `DouyinBasicSyncJob`; async cleanup; frontend interceptor changes. `AutoDistinct` is private+instance+DB-coupled → NOT directly characterizable until extracted; its supporting pure helpers ARE pinned here.

---

### Task 1: Exclude `tests/` from the web project compile glob

**Files:** Modify `dy.net.csproj`

- [ ] **Step 1:** In `dy.net.csproj`, every `Exclude="app\**;db\**;expand\**;logs\**..."` list must also exclude `tests\**`. Replace each occurrence of `app\**;db\**;expand\**;logs\**` with `app\**;db\**;expand\**;logs\**;tests\**` in the `<Compile Remove>`, `<Content Remove>`, `<EmbeddedResource Remove>`, `<None Remove>` item groups AND in the `<Compile Include="**/*.cs" Exclude=...>` / `<Content Include=...>` lines.

  Concretely, the two glob-include lines become:
```xml
		<Compile Include="**/*.cs" Exclude="app\**;db\**;expand\**;logs\**;tests\**;obj\**;bin\**" />
		<Content Include="**/*.json;**/*.xml;**/*.config" Exclude="app\**;db\**;expand\**;logs\**;tests\**;obj\**;bin\**" />
```
  and add `<Compile Remove="tests\**" />`, `<Content Remove="tests\**" />`, `<EmbeddedResource Remove="tests\**" />`, `<None Remove="tests\**" />` alongside the existing `app\**` Remove items.

- [ ] **Step 2:** `dotnet build dy.net.csproj -c Debug` → 0 errors (unchanged behavior; just glob hygiene).
- [ ] **Step 3:** Commit: `build: exclude tests/ from web project compile glob`

---

### Task 2: Scaffold the xUnit test project

**Files:** Create `tests/dy.net.Tests/dy.net.Tests.csproj`

- [ ] **Step 1:** Create `tests/dy.net.Tests/dy.net.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>disable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <IsPackable>false</IsPackable>
    <GenerateDocumentationFile>false</GenerateDocumentationFile>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.11.1" />
    <PackageReference Include="xunit" Version="2.9.2" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\dy.net.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2:** Add a trivial smoke test `tests/dy.net.Tests/SmokeTest.cs`:

```csharp
namespace dy.net.Tests
{
    public class SmokeTest
    {
        [Fact]
        public void Harness_Works()
        {
            Assert.Equal(4, 2 + 2);
        }
    }
}
```

- [ ] **Step 3:** `dotnet test tests/dy.net.Tests/dy.net.Tests.csproj` → 1 passed. (Confirms the test project compiles against the web project and the runner works.)
- [ ] **Step 4:** Commit: `test: scaffold xUnit project (tests/dy.net.Tests)`

---

### Task 3: Characterize `DouyinFileNameHelper` (pure, refactor will move this)

**Files:** Create `tests/dy.net.Tests/DouyinFileNameHelperTests.cs`

- [ ] **Step 1:** Write tests calling each pure function with fixed inputs, asserting placeholder constants:

```csharp
using dy.net.utils;

namespace dy.net.Tests
{
    public class DouyinFileNameHelperTests
    {
        [Theory]
        [InlineData("a/b:c*?\"<>|\\d", "def", false)]
        [InlineData("   ", "FallBack Name", false)]
        [InlineData("正常名字 123", "def", true)]
        [InlineData("line\nbreak\ttab", "def", false)]
        public void SanitizeLinuxFileName_locks_current_behavior(string input, string def, bool folder)
        {
            var actual = DouyinFileNameHelper.SanitizeLinuxFileName(input, def, folder);
            // GOLDEN: replace "<<OBSERVED>>" after Step 2
            Assert.Equal(Golden(input, def, folder), actual);
        }

        private static string Golden(string i, string d, bool f) => (i, d, f) switch
        {
            ("a/b:c*?\"<>|\\d", "def", false) => "<<OBSERVED-1>>",
            ("   ", "FallBack Name", false) => "<<OBSERVED-2>>",
            ("正常名字 123", "def", true) => "<<OBSERVED-3>>",
            ("line\nbreak\ttab", "def", false) => "<<OBSERVED-4>>",
            _ => throw new System.ArgumentException()
        };

        [Theory]
        [InlineData("abc定义123!!", "<<OBSERVED-K1>>")]
        [InlineData("  spaced  ", "<<OBSERVED-K2>>")]
        public void KeepChineseLettersAndNumbers_locks(string input, string expected)
            => Assert.Equal(expected, DouyinFileNameHelper.KeepChineseLettersAndNumbers(input));

        [Theory]
        [InlineData("name123", "<<OBSERVED-R1>>")]
        [InlineData("clip(2)", "<<OBSERVED-R2>>")]
        public void RemoveNumberSuffix_locks(string input, string expected)
            => Assert.Equal(expected, DouyinFileNameHelper.RemoveNumberSuffix(input));

        [Theory]
        [InlineData("一二三四五六七八九十", 5, false, "<<OBSERVED-L1>>")]
        [InlineData("hello world long text", 8, true, "<<OBSERVED-L2>>")]
        public void LimitUnifiedCount_locks(string input, int max, bool ell, string expected)
            => Assert.Equal(expected, input.LimitUnifiedCount(max, ell));
    }
}
```

- [ ] **Step 2:** Run `dotnet test --filter DouyinFileNameHelperTests`. Tests FAIL showing `Actual:` values. Replace every `<<OBSERVED-*>>` with the exact actual string from the failure output (this captures current behavior). For the switch-based test, fill `<<OBSERVED-1..4>>`.
- [ ] **Step 3:** Re-run → all pass. The current behavior is now locked.
- [ ] **Step 4:** Commit: `test: characterize DouyinFileNameHelper (golden master)`

---

### Task 4: Characterize `VideoTitleGenerator.Generate` and `Md5Util.Md5`

**Files:** Create `tests/dy.net.Tests/PureHelperTests.cs`

- [ ] **Step 1:** Inspect `utils/VideoTitleGenerator.cs` `Generate(...)` signature and `model/dto/VideoTitleDataTemplate.cs`. Write a test that constructs a fixed input template + data and asserts the produced title against a golden constant. Also:

```csharp
using dy.net.utils;

namespace dy.net.Tests
{
    public class PureHelperTests
    {
        [Fact]
        public void Md5_is_stable()
        {
            Assert.Equal("900150983cd24fb0d6963f7d28e17f72", "abc".Md5());
        }
        // VideoTitleGenerator.Generate test added after inspecting its real signature in Step 1.
    }
}
```

(The `Md5("abc")` value `900150983cd24fb0d6963f7d28e17f72` is the well-known MD5 of "abc"; if the test fails the helper is non-standard and that fact must be captured instead.)

- [ ] **Step 2:** Add the `VideoTitleGenerator.Generate` characterization test using the real signature found in Step 1 (fixed inputs → golden output captured by running once).
- [ ] **Step 3:** `dotnet test --filter PureHelperTests` → all pass after golden values locked.
- [ ] **Step 4:** Commit: `test: characterize VideoTitleGenerator + Md5`

---

### Task 5: Characterize DB aggregation `GetStatics` / `GetChartData` via temp SQLite

**Files:** Create `tests/dy.net.Tests/VideoStatsCharacterizationTests.cs`, `tests/dy.net.Tests/TestDb.cs`

- [ ] **Step 1:** Create a SQLite test-db helper:

```csharp
using dy.net.model.entity;
using SqlSugar;

namespace dy.net.Tests
{
    public sealed class TestDb : System.IDisposable
    {
        public readonly string Path;
        public readonly SqlSugarClient Db;

        public TestDb()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"dytest_{System.Guid.NewGuid():N}.db");
            Db = new SqlSugarClient(new ConnectionConfig
            {
                ConnectionString = $"DataSource={Path}",
                DbType = DbType.Sqlite,
                IsAutoCloseConnection = true,
                InitKeyType = InitKeyType.Attribute
            });
            Db.CodeFirst.InitTables<DouyinVideo, DouyinCookie>();
        }

        public void Dispose()
        {
            Db.Dispose();
            try { System.IO.File.Delete(Path); } catch { }
        }
    }
}
```

- [ ] **Step 2:** Write the characterization test seeding a *fixed, documented* dataset and snapshotting the `VideoStaticsDto` / chart output:

```csharp
using dy.net.model.entity;
using dy.net.repository;
using dy.net.service;

namespace dy.net.Tests
{
    public class VideoStatsCharacterizationTests
    {
        private static DouyinVideo V(string id, string author, long size, dy.net.model.dto.VideoTypeEnum type, System.DateTime sync)
            => new DouyinVideo { Id = id, AuthorId = author, VideoSize = size, ViedoType = type, SyncTime = sync };
        // NOTE: exact DouyinVideo property names verified against model/entity/DouyinVideo.cs in Step 0 below.

        [Fact]
        public async System.Threading.Tasks.Task GetStatics_locks_current_aggregation()
        {
            using var t = new TestDb();
            var seed = new System.Collections.Generic.List<DouyinVideo>
            {
                V("1","authorA",1000, dy.net.model.dto.VideoTypeEnum.dy_collects, new System.DateTime(2026,5,1)),
                V("2","authorA",2000, dy.net.model.dto.VideoTypeEnum.dy_favorite, new System.DateTime(2026,5,2)),
                V("3","authorB",3000, dy.net.model.dto.VideoTypeEnum.dy_follows,  new System.DateTime(2026,5,3)),
            };
            await t.Db.Insertable(seed).ExecuteCommandAsync();

            var svc = new DouyinVideoService(
                new DouyinVideoRepository(t.Db),
                new DouyinCookieRepository(t.Db),
                t.Db);

            var dto = await svc.GetStatics();

            // GOLDEN — lock after first run (Step 3)
            Assert.Equal(3, dto.VideoCount);
            Assert.Equal(2, dto.AuthorCount);
            // ... remaining VideoStaticsDto fields asserted with observed values
        }
    }
}
```

- [ ] **Step 0 (do before Step 2):** Read `model/entity/DouyinVideo.cs` and `model/entity/DouyinCookie.cs` to get the EXACT property names/types used by `GetStatics`/`GetChartData` (size field, type enum, author id, sync time). Adjust the `V(...)` factory and seed accordingly — no guessed property names.
- [ ] **Step 3:** Run the test; it will fail on the golden asserts. Replace each asserted value with the observed actual. Add asserts for every `VideoStaticsDto` field that `GetStatics` populates (full snapshot) and a `GetChartData(7)` test the same way.
- [ ] **Step 4:** Re-run → green. Current aggregation behavior is now fully pinned.
- [ ] **Step 5:** Commit: `test: characterize GetStatics/GetChartData via temp SQLite`

---

### Task 6: Test inventory doc + handoff

**Files:** Create `tests/README.md`

- [ ] **Step 1:** Write `tests/README.md` listing: how to run (`dotnet test`), what is pinned (DouyinFileNameHelper, VideoTitleGenerator, Md5, GetStatics, GetChartData), what is intentionally NOT covered and why (DouyinBasicSyncJob orchestration: HTTP+FS+DB coupled — refactor must extract seams first; AutoDistinct: private/instance/DB — pin after extraction), and the rule: **a refactor PR is only safe if `dotnet test` stays green; if behavior legitimately changes, update the golden value in the same commit with justification.**
- [ ] **Step 2:** `dotnet test` (whole project) → all green.
- [ ] **Step 3:** Commit: `docs: test inventory + refactor-safety rule`. Push branch.

## Self-Review
- Spec ("tests before refactor") covered: harness (T1,T2), pure-logic pins (T3,T4), DB-aggregation pins (T5), inventory/handoff (T6). Refactors deliberately excluded — that is the point.
- No placeholders in *plan structure*; the `<<OBSERVED>>`/golden constants are the golden-master methodology with explicit capture steps, not unfilled plan gaps.
- Type consistency: `TestDb` reused by T5; `DouyinVideoService`/`DouyinVideoRepository`/`DouyinCookieRepository` ctors match verified signatures; entity property names deferred to T5 Step 0 explicit read (no guessing).
- Risk: referencing a `Microsoft.NET.Sdk.Web` project from a test project + main glob picking up test sources — mitigated by T1 (glob exclude) ordered first.
