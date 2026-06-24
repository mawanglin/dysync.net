using dy.net.model.entity;
using SqlSugar;

namespace dy.net.repository
{
    public class DouyinJobScheduleRepository : BaseRepository<DouyinJobSchedule>
    {
        public DouyinJobScheduleRepository(ISqlSugarClient db) : base(db) { }

        // GetAllAsync 直接复用 BaseRepository<T> 的同名方法，无需在此重复定义。

        public async Task UpsertAsync(string type, string scheduleType, string expression, DateTime now)
        {
            await Db.Storageable(new DouyinJobSchedule
            {
                Type = type,
                ScheduleType = scheduleType,
                Expression = expression,
                UpdatedAt = now
            }).ExecuteCommandAsync();
        }
    }
}
