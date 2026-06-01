using System.Threading.Tasks;
using dy.net.model.dto;
using dy.net.model.entity;
using dy.net.repository;
using dy.net.service;
using dy.net.utils;
using Xunit;

namespace dy.net.Tests
{
    /// <summary>
    /// Pins admin-user seed + change-password behavior against the real SQLite stack,
    /// including review-#1 "force first-login password change" (MustChangePwd):
    /// the default-credential seed sets it true; a successful UpdatePwd clears it;
    /// a rejected UpdatePwd leaves it untouched. Also pins that passwords are stored
    /// as PBKDF2 (not legacy MD5).
    /// </summary>
    public class AdminUserRepositoryTests
    {
        private static AdminUserService Service(TestDb t) => new AdminUserService(new AdminUserRepository(t.Db));
        private static AdminUserRepository Repo(TestDb t) => new AdminUserRepository(t.Db);

        [Fact]
        public void InitUser_OnEmptyTable_SeedsUser_WithMustChangePwdTrue()
        {
            using var t = new TestDb();

            var (code, _) = Service(t).InitUser("douyin", "douyin2026");

            Assert.Equal(0, code);
            var user = t.Db.Queryable<AdminUserInfo>().First();
            Assert.NotNull(user);
            Assert.Equal("douyin", user.UserName);
            Assert.True(user.MustChangePwd);                                 // 新行为：强制首登改密
            Assert.True(PasswordUtil.Verify(user.Password, "douyin2026"));   // 已 PBKDF2 哈希
            Assert.False(PasswordUtil.IsLegacyMd5(user.Password));
        }

        [Fact]
        public void InitUser_WhenUserExists_DoesNotCreateSecondRow()
        {
            using var t = new TestDb();
            Service(t).InitUser("douyin", "douyin2026");

            var (code, erro) = Service(t).InitUser("other", "pwd");

            Assert.Equal(-1, code);
            Assert.Equal("系统用户已存在", erro);
            Assert.Equal(1, t.Db.Queryable<AdminUserInfo>().Count());
        }

        [Fact]
        public async Task UpdatePwd_WithCorrectOldPassword_ClearsMustChangePwd_AndRehashes()
        {
            using var t = new TestDb();
            Service(t).InitUser("douyin", "douyin2026");
            var seeded = t.Db.Queryable<AdminUserInfo>().First();

            var (code, erro) = await Repo(t).UpdatePwd(new UpdatePwdRequest
            {
                UserId = seeded.Id,
                UserName = "douyin",
                OldPassword = "douyin2026",
                Password = "newSecret123",
                ConfirmPassword = "newSecret123"
            });

            Assert.Equal(0, code);
            Assert.Equal("更新成功", erro);
            var after = t.Db.Queryable<AdminUserInfo>().First();
            Assert.False(after.MustChangePwd);                              // 标记已清
            Assert.True(PasswordUtil.Verify(after.Password, "newSecret123"));
            Assert.False(PasswordUtil.Verify(after.Password, "douyin2026"));
        }

        [Fact]
        public async Task UpdatePwd_WithWrongOldPassword_Rejected_FlagUnchanged()
        {
            using var t = new TestDb();
            Service(t).InitUser("douyin", "douyin2026");
            var seeded = t.Db.Queryable<AdminUserInfo>().First();

            var (code, erro) = await Repo(t).UpdatePwd(new UpdatePwdRequest
            {
                UserId = seeded.Id,
                UserName = "douyin",
                OldPassword = "wrong",
                Password = "newSecret123",
                ConfirmPassword = "newSecret123"
            });

            Assert.Equal(-1, code);
            Assert.Equal("原密码错误", erro);
            var after = t.Db.Queryable<AdminUserInfo>().First();
            Assert.True(after.MustChangePwd);                               // 未改密 → 标记仍在
            Assert.True(PasswordUtil.Verify(after.Password, "douyin2026"));
        }

        [Fact]
        public void ResetPwd_ViaPwdTxt_ClearsMustChangePwd_AndRehashes()
        {
            using var t = new TestDb();
            Service(t).InitUser("douyin", "douyin2026");

            var ok = Repo(t).ResetPwd("resetSecret123");

            Assert.True(ok);
            var after = t.Db.Queryable<AdminUserInfo>().First();
            Assert.False(after.MustChangePwd);                              // pwd.txt 显式改密 → 标记清除
            Assert.True(PasswordUtil.Verify(after.Password, "resetSecret123"));
        }

        [Fact]
        public async Task UpdatePwd_WithUnknownUserId_ReturnsUserNotFound()
        {
            using var t = new TestDb();
            Service(t).InitUser("douyin", "douyin2026");

            var (code, erro) = await Repo(t).UpdatePwd(new UpdatePwdRequest
            {
                UserId = "does-not-exist",
                UserName = "douyin",
                OldPassword = "douyin2026",
                Password = "x",
                ConfirmPassword = "x"
            });

            Assert.Equal(-1, code);
            Assert.Equal("用户不存在", erro);
        }
    }
}
