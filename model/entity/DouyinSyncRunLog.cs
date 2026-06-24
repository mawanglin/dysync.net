using System;
using System.Text.Json.Serialization;
using SqlSugar;

namespace dy.net.model.entity
{
    [SugarTable("dy_sync_run_log")]
    public class DouyinSyncRunLog
    {
        [SugarColumn(IsPrimaryKey = true, Length = 50)]
        [JsonPropertyName("id")] public string Id { get; set; }
        [SugarColumn(Length = 50)]
        [JsonPropertyName("type")] public string Type { get; set; }
        [SugarColumn(Length = 50)]
        [JsonPropertyName("name")] public string Name { get; set; }
        [JsonPropertyName("startedAt")] public DateTime StartedAt { get; set; }
        [JsonPropertyName("endedAt")] public DateTime EndedAt { get; set; }
        [JsonPropertyName("downloaded")] public int Downloaded { get; set; }
        [JsonPropertyName("failed")] public int Failed { get; set; }
        [SugarColumn(Length = 20)]
        [JsonPropertyName("status")] public string Status { get; set; }
        [JsonPropertyName("createdAt")] public DateTime CreatedAt { get; set; }
    }
}
