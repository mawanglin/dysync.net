# Await `RealDeleteVideos` Instead of Fire-and-Forget — Design Spec

**Status:** Approved 2026-06-01

**Campaign:** Code-review CRITICAL remediation (report `docs/code-review-2026-05-18.md` #6).
Second of three remaining CRITICAL fixes.

## Goal

`service/DouyinVideoService.RealDeleteVideos` (line 472) takes two branches on `videos.Count`:

- `<= 30`: `await`s `ReDownloadViedoAsync` + `AddDeleteVideo`, returns the **real** success/failure.
- `> 30`: wraps the identical body in `Task.Run(...)` **fire-and-forget** and unconditionally `return true`.

The `> 30` branch is the #6 vulnerability: a destructive DB+file operation runs detached, its result is never observed, and the caller is told `true` ("success") regardless of whether the deletion actually succeeded — silent data/file loss masked as success. (The `UseTranAsync` half of #6 was already fixed: `BaseRepository.UseTranAsync` now `return await ... res.IsSuccess`.)

## Decision

Per review confirmation, fix by **awaiting** the operation in all cases (no background queue). For a single-admin self-hosted tool, batch deletes of >30 items are not latency-critical; correctness (truthful success/failure) outweighs request latency. A background-queue + status-poll design was considered and rejected as disproportionate.

## Change

Post-fix, the two branches become **identical** (both `await` and return the real result), so the `videos.Count <= 30` split and its magic number `30` are collapsed into a single path:

```csharp
if (videos != null && videos.Count > 0)
{
    var result = await ReDownloadViedoAsync(new ReDownViedoDto { Ids = videos.Select(x => x.Id)?.ToList() }, true);
    if (result)
    {
        //加入删除逻辑
        var deletes = await AddDeleteVideo(videos);
        Serilog.Log.Debug($"批量永久删除博主{videos.FirstOrDefault()?.Author}，共{deletes}条记录");
        return true;
    }
    else
    {
        Serilog.Log.Error($"批量删除{videos.FirstOrDefault()?.Author}视频失败");
        return false;
    }
}
else
{
    Serilog.Log.Error($"没有查询到可删除的视频");
    return false;
}
```

**Behavior change (intentional):** for `> 30` items the call now blocks until the delete completes and returns the true result instead of an immediate optimistic `true`. The `<= 30` path is unchanged.

## File-level scope

- **Modify:** `service/DouyinVideoService.cs:478-520` — collapse the count branch, remove `Task.Run`.

## Testing

`RealDeleteVideos` is I/O orchestration (DB via `_dyCollectVideoRepository`, `ReDownloadViedoAsync`, `AddDeleteVideo`) with no pure-logic seam and is not in the characterization suite. No golden-master fact applies. Verification: build 0 errors + existing 120 tests stay green + the manual reasoning above. The behavior change is deliberate and cannot be pinned by a no-behavior-change golden master.
