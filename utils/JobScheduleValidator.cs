using Quartz;

namespace dy.net.utils
{
    /// <summary>校验并归一化任务周期输入。interval→正整数分钟字符串；cron→Quartz 校验。</summary>
    public static class JobScheduleValidator
    {
        public static (bool ok, string normalized, string error) ValidateAndNormalize(string scheduleType, string expression)
        {
            if (scheduleType == "interval")
            {
                if (int.TryParse(expression?.Trim(), out var m) && m > 0)
                    return (true, m.ToString(), null);
                return (false, null, "间隔分钟必须是正整数");
            }
            if (scheduleType == "cron")
            {
                var expr = expression?.Trim();
                if (!string.IsNullOrWhiteSpace(expr) && CronExpression.IsValidExpression(expr))
                    return (true, expr, null);
                return (false, null, "cron 表达式不合法");
            }
            return (false, null, "未知的周期类型");
        }
    }
}
