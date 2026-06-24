using System;
using System.Text.Json.Serialization;

namespace dy.net.model.dto
{
    /// <summary>定时任务总览（调度信息 + 最近一轮结果合并）。</summary>
    public class SyncJobOverview
    {
        [JsonPropertyName("type")] public string Type { get; set; }
        [JsonPropertyName("name")] public string Name { get; set; }
        [JsonPropertyName("scheduled")] public bool Scheduled { get; set; }
        [JsonPropertyName("scheduleDesc")] public string ScheduleDesc { get; set; }
        [JsonPropertyName("nextFireTime")] public DateTime? NextFireTime { get; set; }
        [JsonPropertyName("prevFireTime")] public DateTime? PrevFireTime { get; set; }
        [JsonPropertyName("triggerState")] public string TriggerState { get; set; }
        [JsonPropertyName("running")] public bool Running { get; set; }
        [JsonPropertyName("downloaded")] public int Downloaded { get; set; }
        [JsonPropertyName("failed")] public int Failed { get; set; }
        [JsonPropertyName("currentTitle")] public string CurrentTitle { get; set; }
        [JsonPropertyName("endedAt")] public DateTime? EndedAt { get; set; }
    }
}
