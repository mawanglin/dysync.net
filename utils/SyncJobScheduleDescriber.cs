namespace dy.net.utils
{
    /// <summary>
    /// 把已从 Quartz 触发器提取出的原始调度值，描述为人类可读的周期文案。
    /// 纯函数（不依赖 Quartz 类型），便于单测；触发器字段的提取留在调用方。
    /// </summary>
    public static class SyncJobScheduleDescriber
    {
        public static string Describe(bool isCron, string cronExpr, int? simpleIntervalMinutes)
        {
            if (isCron && !string.IsNullOrWhiteSpace(cronExpr))
                return $"Cron: {cronExpr}";
            if (simpleIntervalMinutes.HasValue && simpleIntervalMinutes.Value > 0)
                return $"每 {simpleIntervalMinutes.Value} 分钟";
            return "自定义";
        }
    }
}
