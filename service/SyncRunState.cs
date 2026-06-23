using System.Text.Json.Serialization;   // [JsonPropertyName]，与项目响应 DTO 约定一致（显式 camelCase，不依赖全局策略）
using dy.net.utils;       // VideoTypeEnum.GetDesc()
using dy.net.model.dto;   // VideoTypeEnum

namespace dy.net.service
{
    /// <summary>
    /// 同步运行状态中枢（内存单例，不持久化）。
    /// 5 类同步作业共用：自报启停、写进度、协作式取消、供状态接口快照。
    /// 所有可变状态读写都在 _gate 锁内，保证 5 作业并发安全。
    /// 时间统一由调用方传入（now），便于单元测试且避免散落 DateTime.Now。
    /// </summary>
    public class SyncRunState
    {
        private readonly object _gate = new();
        private readonly Dictionary<VideoTypeEnum, TypeProgress> _types = new();
        private readonly Queue<SyncLogSnapshot> _logs = new();
        private CancellationTokenSource _cts;
        private DateTime? _manualTriggerAt;   // 防双击/防并发触发的短时闸
        private const int MaxLogs = 50;
        private static readonly TimeSpan ManualTriggerGuard = TimeSpan.FromSeconds(15);

        /// <summary>任意类型正在运行即为 true。</summary>
        public bool IsAnyRunning
        {
            get { lock (_gate) { return _types.Values.Any(t => t.Running); } }
        }

        /// <summary>当前批次的取消令牌（无批次时为 None）。作业应在循环检查点读取。</summary>
        public CancellationToken Token
        {
            get { lock (_gate) { return _cts?.Token ?? CancellationToken.None; } }
        }

        /// <summary>手动触发前调用：已在运行或处于短时闸内则返回 false（拒绝触发）。</summary>
        public bool TryBeginManualTrigger(DateTime now)
        {
            lock (_gate)
            {
                if (_types.Values.Any(t => t.Running)) return false;
                if (_manualTriggerAt.HasValue && now - _manualTriggerAt.Value < ManualTriggerGuard) return false;
                _manualTriggerAt = now;
                return true;
            }
        }

        /// <summary>作业开始：批次首个作业会重建取消令牌。</summary>
        public void RegisterStart(VideoTypeEnum type, string cookieName, DateTime now)
        {
            lock (_gate)
            {
                bool firstOfBatch = !_types.Values.Any(t => t.Running);
                if (firstOfBatch)
                {
                    _cts?.Dispose();
                    _cts = new CancellationTokenSource();
                    _manualTriggerAt = null;   // 批次已真正开始，清闸
                }
                _types[type] = new TypeProgress
                {
                    Running = true,
                    StartedAt = now,
                    EndedAt = null,
                    CookieName = cookieName ?? "",
                    Downloaded = 0,
                    Failed = 0,
                    PageTotal = 0,
                    CurrentTitle = ""
                };
            }
        }

        public void RegisterFinish(VideoTypeEnum type, DateTime now)
        {
            lock (_gate)
            {
                if (_types.TryGetValue(type, out var p))
                {
                    p.Running = false;
                    p.CurrentTitle = "";
                    p.EndedAt = now;
                }
            }
        }

        public void SetCurrentCookie(VideoTypeEnum type, string cookieName)
        {
            lock (_gate) { if (_types.TryGetValue(type, out var p)) p.CookieName = cookieName ?? ""; }
        }

        public void SetPageTotal(VideoTypeEnum type, int total)
        {
            lock (_gate) { if (_types.TryGetValue(type, out var p)) p.PageTotal = total; }
        }

        public void UpdateCurrentVideo(VideoTypeEnum type, string title)
        {
            lock (_gate) { if (_types.TryGetValue(type, out var p)) p.CurrentTitle = title ?? ""; }
        }

        public void OnDownloaded(VideoTypeEnum type, bool ok, string title, DateTime now)
        {
            lock (_gate)
            {
                if (_types.TryGetValue(type, out var p))
                {
                    if (ok) p.Downloaded++; else p.Failed++;
                }
                _logs.Enqueue(new SyncLogSnapshot
                {
                    Time = now,
                    Text = $"[{type.GetDesc()}]{(ok ? "完成" : "失败")}：{title}"
                });
                while (_logs.Count > MaxLogs) _logs.Dequeue();
            }
        }

        /// <summary>请求停止当前批次：无运行返回 false；否则取消令牌返回 true。</summary>
        public bool RequestStop()
        {
            lock (_gate)
            {
                if (!_types.Values.Any(t => t.Running)) return false;
                _cts?.Cancel();
                return true;
            }
        }

        public SyncStatusSnapshot GetSnapshot(DateTime now)
        {
            lock (_gate)
            {
                bool running = _types.Values.Any(t => t.Running);
                DateTime? startedAt = _types.Values
                    .Where(t => t.Running)
                    .Select(t => (DateTime?)t.StartedAt)
                    .OrderBy(t => t)
                    .FirstOrDefault();
                return new SyncStatusSnapshot
                {
                    Running = running,
                    StartedAt = startedAt,
                    ElapsedSec = startedAt.HasValue ? (int)(now - startedAt.Value).TotalSeconds : 0,
                    Types = _types
                        .OrderByDescending(kv => kv.Value.Running)   // 进行中的排前
                        .Select(kv => new TypeProgressSnapshot
                        {
                            Type = kv.Key.ToString(),
                            Name = kv.Key.GetDesc(),
                            Running = kv.Value.Running,
                            Downloaded = kv.Value.Downloaded,
                            Failed = kv.Value.Failed,
                            PageTotal = kv.Value.PageTotal,
                            CurrentTitle = kv.Value.CurrentTitle,
                            CookieName = kv.Value.CookieName,
                            EndedAt = kv.Value.EndedAt
                        })
                        .ToList(),
                    RecentLogs = _logs.ToArray().Reverse().ToList()   // 锁内拍快照再反转：最新在前
                };
            }
        }

        private class TypeProgress
        {
            public bool Running;
            public DateTime StartedAt;
            public DateTime? EndedAt;
            public int Downloaded;
            public int Failed;
            public int PageTotal;
            public string CurrentTitle = "";
            public string CookieName = "";
        }
    }

    public class SyncStatusSnapshot
    {
        [JsonPropertyName("running")] public bool Running { get; set; }
        [JsonPropertyName("startedAt")] public DateTime? StartedAt { get; set; }
        [JsonPropertyName("elapsedSec")] public int ElapsedSec { get; set; }
        [JsonPropertyName("types")] public List<TypeProgressSnapshot> Types { get; set; } = new();
        [JsonPropertyName("recentLogs")] public List<SyncLogSnapshot> RecentLogs { get; set; } = new();
    }

    public class TypeProgressSnapshot
    {
        [JsonPropertyName("type")] public string Type { get; set; }
        [JsonPropertyName("name")] public string Name { get; set; }
        [JsonPropertyName("downloaded")] public int Downloaded { get; set; }
        [JsonPropertyName("failed")] public int Failed { get; set; }
        [JsonPropertyName("pageTotal")] public int PageTotal { get; set; }
        [JsonPropertyName("currentTitle")] public string CurrentTitle { get; set; }
        [JsonPropertyName("cookieName")] public string CookieName { get; set; }
        [JsonPropertyName("running")] public bool Running { get; set; }
        [JsonPropertyName("endedAt")] public DateTime? EndedAt { get; set; }
    }

    public class SyncLogSnapshot
    {
        [JsonPropertyName("time")] public DateTime Time { get; set; }
        [JsonPropertyName("text")] public string Text { get; set; }
    }

}
