using dy.net.model.entity;
using SqlSugar;

namespace dy.net.repository
{
    public class DouyinSyncRunLogRepository : BaseRepository<DouyinSyncRunLog>
    {
        public DouyinSyncRunLogRepository(ISqlSugarClient db) : base(db) { }

        /// <summary>插入一条；并裁剪该类型超出最近 keepPerType 条的旧记录。</summary>
        public async Task AddAndPruneAsync(DouyinSyncRunLog log, int keepPerType)
        {
            await Db.Insertable(log).ExecuteCommandAsync();
            var oldIds = await Db.Queryable<DouyinSyncRunLog>()
                .Where(x => x.Type == log.Type)
                .OrderBy(x => x.StartedAt, OrderByType.Desc)
                .Skip(keepPerType)
                .Select(x => x.Id)
                .ToListAsync();
            if (oldIds.Count > 0)
                await Db.Deleteable<DouyinSyncRunLog>().In(oldIds).ExecuteCommandAsync();
        }

        public async Task<(List<DouyinSyncRunLog> list, int total)> GetPagedAsync(string type, int page, int size)
        {
            RefAsync<int> total = 0;
            var list = await Db.Queryable<DouyinSyncRunLog>()
                .Where(x => x.Type == type)
                .OrderBy(x => x.StartedAt, OrderByType.Desc)
                .ToPageListAsync(page, size, total);
            return (list, total);
        }
    }
}
