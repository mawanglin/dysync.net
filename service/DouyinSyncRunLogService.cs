using ClockSnowFlake;
using dy.net.model.dto;
using dy.net.model.entity;
using dy.net.repository;

namespace dy.net.service
{
    public class DouyinSyncRunLogService
    {
        private const int KeepPerType = 100;
        private readonly DouyinSyncRunLogRepository _repo;
        public DouyinSyncRunLogService(DouyinSyncRunLogRepository repo) { _repo = repo; }

        public Task RecordAsync(VideoTypeEnum type, string name, DateTime startedAt, DateTime endedAt, int downloaded, int failed, string status)
            => _repo.AddAndPruneAsync(new DouyinSyncRunLog
            {
                Id = IdGener.GetLong().ToString(),
                Type = type.ToString(),
                Name = name,
                StartedAt = startedAt,
                EndedAt = endedAt,
                Downloaded = downloaded,
                Failed = failed,
                Status = status,
                CreatedAt = DateTime.Now
            }, KeepPerType);

        public Task<(List<DouyinSyncRunLog> list, int total)> GetPagedAsync(string type, int page, int size)
            => _repo.GetPagedAsync(type, page, size);
    }
}
