namespace dy.net.model.entity
{
    [SqlSugar.SugarTable(TableName = "login_user_info")]
    public class AdminUserInfo
    {
        public string UserName { get; set; }
        public string Password { get; set; }

        [SqlSugar.SugarColumn(IsPrimaryKey = true)]
        public string Id { get; set; }

        public DateTime CreateTime { get; set; }


        /// <summary>
        /// 更新时间
        /// </summary>
        [SqlSugar.SugarColumn(IsNullable = true)]
        public DateTime UpdateTime { get; set; }

        /// <summary>
        /// 原密码，不存在数据库
        /// </summary>
        [SqlSugar.SugarColumn(IsIgnore = true)]
        public string? OldPwd { get; set; }

        /// <summary>
        /// 头像
        /// </summary>
        [SqlSugar.SugarColumn(IsNullable = true)]
        public string Avatar { get; set; }

        /// <summary>
        /// 是否必须修改密码（以默认凭据首次创建后强制改密）。
        /// 既有部署升级时新列为 NULL，读回为 false，不被回溯强制；仅新 seed 置 true。
        /// </summary>
        [SqlSugar.SugarColumn(IsNullable = true)]
        public bool MustChangePwd { get; set; }
    }

}
