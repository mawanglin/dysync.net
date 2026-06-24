using dy.net.model.entity;
using SqlSugar;

namespace dy.net.repository
{
    public class DouyinJobScheduleRepository : BaseRepository<DouyinJobSchedule>
    {
        public DouyinJobScheduleRepository(ISqlSugarClient db) : base(db) { }

        public Task<List<DouyinJobSchedule>> GetAllAsync()
            => Db.Queryable<DouyinJobSchedule>().ToListAsync();

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
