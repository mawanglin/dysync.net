using dy.net.model.dto;
using dy.net.service;

namespace dy.net.Tests
{
    public class SyncRunStateTests
    {
        private static readonly DateTime T0 = new DateTime(2026, 6, 23, 10, 0, 0);

        [Fact]
        public void Idle_initially_notRunning_and_stop_returns_false()
        {
            var s = new SyncRunState();
            Assert.False(s.IsAnyRunning);
            Assert.False(s.RequestStop());          // 空闲时停止无效
            Assert.False(s.Token.IsCancellationRequested);
        }

        [Fact]
        public void RegisterStart_marks_running_and_TryBeginManual_rejected_while_running()
        {
            var s = new SyncRunState();
            Assert.True(s.TryBeginManualTrigger(T0));        // 空闲可触发
            s.RegisterStart(VideoTypeEnum.dy_collects, "Zoe", T0);
            Assert.True(s.IsAnyRunning);
            Assert.False(s.TryBeginManualTrigger(T0));       // 运行中拒绝再次触发
        }

        [Fact]
        public void RequestStop_cancels_token_while_running()
        {
            var s = new SyncRunState();
            s.RegisterStart(VideoTypeEnum.dy_favorite, "Zoe", T0);
            var token = s.Token;
            Assert.True(s.RequestStop());
            Assert.True(token.IsCancellationRequested);
        }

        [Fact]
        public void New_batch_after_all_finished_rebuilds_token()
        {
            var s = new SyncRunState();
            s.RegisterStart(VideoTypeEnum.dy_collects, "Zoe", T0);
            var first = s.Token;
            s.RequestStop();
            s.RegisterFinish(VideoTypeEnum.dy_collects, T0);
            Assert.False(s.IsAnyRunning);

            s.RegisterStart(VideoTypeEnum.dy_collects, "Zoe", T0.AddMinutes(30));
            var second = s.Token;
            Assert.False(second.IsCancellationRequested);    // 新批次令牌未被取消
            Assert.NotEqual(first, second);
        }

        [Fact]
        public void Concurrent_types_running_flag_tracks_any()
        {
            var s = new SyncRunState();
            s.RegisterStart(VideoTypeEnum.dy_collects, "Zoe", T0);
            s.RegisterStart(VideoTypeEnum.dy_favorite, "Zoe", T0);
            s.RegisterFinish(VideoTypeEnum.dy_collects, T0);
            Assert.True(s.IsAnyRunning);                     // 喜欢仍在跑
            s.RegisterFinish(VideoTypeEnum.dy_favorite, T0);
            Assert.False(s.IsAnyRunning);
        }

        [Fact]
        public void OnDownloaded_accumulates_and_snapshot_reflects_progress()
        {
            var s = new SyncRunState();
            s.RegisterStart(VideoTypeEnum.dy_collects, "Zoe", T0);
            s.SetPageTotal(VideoTypeEnum.dy_collects, 18);
            s.UpdateCurrentVideo(VideoTypeEnum.dy_collects, "视频A");
            s.OnDownloaded(VideoTypeEnum.dy_collects, true, "视频A", T0);
            s.OnDownloaded(VideoTypeEnum.dy_collects, false, "视频B", T0);

            var snap = s.GetSnapshot(T0.AddSeconds(5));
            Assert.True(snap.Running);
            Assert.Equal(5, snap.ElapsedSec);
            var t = Assert.Single(snap.Types);
            Assert.Equal(1, t.Downloaded);
            Assert.Equal(1, t.Failed);
            Assert.Equal(18, t.PageTotal);
            Assert.Equal(2, snap.RecentLogs.Count);
            Assert.Contains("视频B", snap.RecentLogs[0].Text);   // 最新在前
        }

        [Fact]
        public void Finished_type_persists_in_snapshot_with_endedAt()
        {
            var s = new SyncRunState();
            s.RegisterStart(VideoTypeEnum.dy_collects, "Zoe", T0);
            s.OnDownloaded(VideoTypeEnum.dy_collects, true, "A", T0);
            s.RegisterFinish(VideoTypeEnum.dy_collects, T0.AddSeconds(10));

            var snap = s.GetSnapshot(T0.AddSeconds(20));
            Assert.False(snap.Running);
            var t = Assert.Single(snap.Types);
            Assert.False(t.Running);
            Assert.Equal(1, t.Downloaded);
            Assert.Equal(T0.AddSeconds(10), t.EndedAt);
        }

        [Fact]
        public void OnDownloaded_caps_recent_logs_at_50()
        {
            var s = new SyncRunState();
            s.RegisterStart(VideoTypeEnum.dy_collects, "Zoe", T0);
            for (int i = 1; i <= 51; i++)
                s.OnDownloaded(VideoTypeEnum.dy_collects, true, $"视频{i}", T0);

            var snap = s.GetSnapshot(T0);
            Assert.Equal(50, snap.RecentLogs.Count);                      // 上限 50
            Assert.Contains("视频51", snap.RecentLogs[0].Text);           // 最新在前（列表头）
            Assert.Contains("视频2", snap.RecentLogs.Last().Text);        // 最旧保留项（列表尾）：视频1 已被淘汰
        }

        [Fact]
        public void NonOverlapping_types_each_keep_their_own_row()
        {
            var s = new SyncRunState();
            s.RegisterStart(VideoTypeEnum.dy_collects, "Zoe", T0);
            s.OnDownloaded(VideoTypeEnum.dy_collects, true, "A", T0);
            s.RegisterFinish(VideoTypeEnum.dy_collects, T0.AddSeconds(5));
            s.RegisterStart(VideoTypeEnum.dy_favorite, "Zoe", T0.AddSeconds(6));
            s.OnDownloaded(VideoTypeEnum.dy_favorite, true, "B", T0.AddSeconds(6));
            s.RegisterFinish(VideoTypeEnum.dy_favorite, T0.AddSeconds(8));

            var snap = s.GetSnapshot(T0.AddSeconds(9));
            Assert.False(snap.Running);
            Assert.Equal(2, snap.Types.Count);
            Assert.All(snap.Types, t => Assert.False(t.Running));
            Assert.Contains(snap.Types, t => t.Type == "dy_collects" && t.Downloaded == 1);
            Assert.Contains(snap.Types, t => t.Type == "dy_favorite" && t.Downloaded == 1);
        }

        [Fact]
        public void Running_and_finished_types_coexist_in_snapshot()
        {
            var s = new SyncRunState();
            s.RegisterStart(VideoTypeEnum.dy_collects, "Zoe", T0);
            s.RegisterStart(VideoTypeEnum.dy_favorite, "Zoe", T0);
            s.RegisterFinish(VideoTypeEnum.dy_favorite, T0.AddSeconds(3));

            var snap = s.GetSnapshot(T0.AddSeconds(4));
            Assert.True(snap.Running);
            Assert.Equal(2, snap.Types.Count);
            Assert.Contains(snap.Types, t => t.Type == "dy_collects" && t.Running);
            Assert.Contains(snap.Types, t => t.Type == "dy_favorite" && !t.Running);
        }

        [Fact]
        public void Rerunning_a_type_refreshes_its_row()
        {
            var s = new SyncRunState();
            s.RegisterStart(VideoTypeEnum.dy_collects, "Zoe", T0);
            s.OnDownloaded(VideoTypeEnum.dy_collects, true, "A", T0);
            s.RegisterFinish(VideoTypeEnum.dy_collects, T0.AddSeconds(5));
            s.RegisterStart(VideoTypeEnum.dy_collects, "Zoe", T0.AddMinutes(30));

            var snap = s.GetSnapshot(T0.AddMinutes(30));
            var t = Assert.Single(snap.Types);
            Assert.True(t.Running);
            Assert.Equal(0, t.Downloaded);
            Assert.Null(t.EndedAt);
        }
    }
}
