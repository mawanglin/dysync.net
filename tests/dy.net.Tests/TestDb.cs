using dy.net.model.entity;
using SqlSugar;

namespace dy.net.Tests
{
    /// <summary>
    /// Disposable temporary SQLite database, schema created via SqlSugar CodeFirst
    /// (faithful to the production stack). One file per instance, deleted on Dispose.
    /// </summary>
    public sealed class TestDb : System.IDisposable
    {
        public readonly string Path;
        public readonly SqlSugarClient Db;

        public TestDb()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), $"dytest_{System.Guid.NewGuid():N}.db");
            Db = new SqlSugarClient(new ConnectionConfig
            {
                ConnectionString = $"DataSource={Path}",
                DbType = DbType.Sqlite,
                IsAutoCloseConnection = true,
                InitKeyType = InitKeyType.Attribute
            });
            Db.CodeFirst.InitTables<DouyinVideo, DouyinCookie>();
        }

        public void Dispose()
        {
            Db.Dispose();
            try { System.IO.File.Delete(Path); } catch { }
        }
    }
}
