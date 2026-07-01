using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;

namespace dy.net.service.qrlogin
{
    /// <summary>每 30s 清理超龄扫码会话，兜底释放被遗弃的浏览器进程。</summary>
    public sealed class QrLoginSessionReaper : BackgroundService
    {
        private readonly DouyinQrLoginService _svc;

        public QrLoginSessionReaper(DouyinQrLoginService svc)
        {
            _svc = svc;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await _svc.SweepExpiredAsync();
                }
                catch (Exception ex)
                {
                    Serilog.Log.Warning(ex, "QrLogin 会话清理异常");
                }

                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
                }
                catch (TaskCanceledException)
                {
                    break;
                }
            }
        }
    }
}
