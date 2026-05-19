# 设计：抽取 CreateVideoEntity 纯映射逻辑 + 特征化测试

日期：2026-05-19
分支：`decompile/dy-sync-lib`
关联：`docs/code-review-2026-05-18.md`（WARNING：1520 行 god-class `job/DouyinBasicSyncJob.cs`）；
上一刀 `docs/superpowers/specs/2026-05-19-extract-syncjob-pure-logic-design.md`（四个纯决策方法已抽出并 pinned）；
特征化安全网纪律见 `tests/README.md`。

## 背景与目标

`CreateVideoEntity`（`job/DouyinBasicSyncJob.cs:1385-1440`）在上一刀被显式延后，因其混入
NFO 文件写入副作用与 `IdGener`/`DateTime.Now` 非确定字段。本轮作为 god-class 拆解的下一刀，
延续「最低风险、行为保持、先有特征化测试」纪律：把其中**真正纯的字段映射**剥离到可独立
测试的静态 helper，两个非确定字段提升为入参，NFO 文件写入这一副作用原样留在 job。

**显式不做**：不动 `DynamicVideos`/`NfoFileGenerator` 的 if/else 控制流（整块原样留 job）；
不引入 `IClock`/`IIdGenerator` 等新抽象；不碰 `AutoDistinct`、编排/HTTP/DB；不做无关重构；
不修复既有的不一致空引用行为（保持现状）。

## 现状分析

`CreateVideoEntity` 组成：

| 部分 | 行 | 纯度 |
|------|----|------|
| `new DouyinVideo { … }` 初始化块 | 1390-1418 | 纯（除下两项） |
| `Id = IdGener.GetLong().ToString()` | 1401 | 非确定（雪花 ID）→ 提升为入参 |
| `SyncTime = DateTime.Now` | 1412 | 非确定（时钟）→ 提升为入参 |
| cate-非-custom 标题覆盖 | 1419-1422 | 纯（含 `item.MixInfo.Statis.CurrentEpisode` 链式解引用） |
| `DynamicVideos` 序列化 / `NfoFileGenerator` 写文件 if/else | 1423-1437 | 含文件系统副作用 → 整块留 job |
| `VideoType` 抽象属性 | 1392 | 改为入参 `videoType`（与上一刀同手法） |

复用项：`GetVideoTags(item)`（上一刀已抽到 `SyncDecisionHelper`，纯）、
`DateTimeUtil.Convert10BitTimestamp`（既有纯 util）。
既有不一致空引用（如 `item.Author.AvatarLarger` 非空条件解引用、`item.Video.Cover.UrlList`、
cate 分支 `item.MixInfo.Statis.CurrentEpisode`）属现状行为，特征化测试只覆盖可达 happy
path，不去触发这些不可达 NRE 路径（沿用上一刀对 `followed.FullSync` 的纪律）。

签名为 `private async Task<DouyinVideo>` 且方法体无 `await`（既有 sync-over-async 小瑕疵，
本轮不修）；仅 `private`、调用点均在基类内（行 899/994/1171），6 子类零改动。

## 架构

### 1. `utils/SyncDecisionHelper.cs` 新增 `BuildVideoEntity`

复用已有 helper（`namespace dy.net.utils`，`public static class SyncDecisionHelper`，
与 `DouyinFileNameHelper`/`VideoTitleGenerator`/`Md5Util`/`NfoFileGenerator` 同目录同约定）。

```csharp
public static DouyinVideo BuildVideoEntity(
    VideoTypeEnum videoType, AppConfig config, DouyinCookie cookie, Aweme item,
    VideoBitRate bitRate, string savePath, string coverSavePath, string avatorPath,
    string id, DateTime syncTime, DouyinCollectCate cate)
```

- 行 1388-1422 的逻辑**逐字**搬入（含 `GetVideoTags(item)` 调用、`new DouyinVideo { … }`
  初始化块、cate-非-custom 标题覆盖）。其中 `GetVideoTags(item)` 在 `BuildVideoEntity`
  内为同类未限定调用，解析为同类静态方法 `SyncDecisionHelper.GetVideoTags`（非 job 实例
  方法），行为等价。
- 仅做必要替换：`VideoType` → `videoType`；`IdGener.GetLong().ToString()` → `id`；
  `DateTime.Now` → `syncTime`。其余表达式（含既有空引用写法）原样保留。
- **不**含 `DynamicVideos` 赋值、**不**含任何 `NfoFileGenerator` 调用。
- 返回构造好的 `video`。

### 2. `DouyinBasicSyncJob.CreateVideoEntity` 改为薄委托

签名/可见性/调用点不变（仍 `private async Task<DouyinVideo>`，三处调用点不动，6 子类零改动）。
方法体替换为：

```csharp
var video = SyncDecisionHelper.BuildVideoEntity(
    VideoType, config, cookie, item, bitRate, savePath, coverSavePath, avatorPath,
    IdGener.GetLong().ToString(), DateTime.Now, cate);
if (dynamicVideos != null && dynamicVideos.Count > 0)
{
    video.DynamicVideos = JsonConvert.SerializeObject(dynamicVideos);
}
else
{
    if (cate != null && (VideoType == VideoTypeEnum.dy_mix || VideoType == VideoTypeEnum.dy_series))
    {
        NfoFileGenerator.GenerateVideoNfoFile(config.CloseNfo, video, cate.Name);
    }
    else
    {
        NfoFileGenerator.GenerateVideoNfoFile(config.CloseNfo, video);
    }
}
return video;
```

`DynamicVideos`/NFO 的 if/else 整块**原样**保留在 job，控制流与条件零改动、无重复判定。
Job 可观察行为完全不变（同样的 `video` 字段、同样的 NFO 写入条件）。

### 3. 特征化测试 `tests/dy.net.Tests/SyncDecisionHelperTests.cs` 追加

- 纯内存，**不使用** `TestDb`（无 DB 依赖）。
- 遵循 golden-master 纪律（`tests/README.md`）：传入固定 `id`/`syncTime` 常量，
  先跑出实际值再锁死，绝不为通过而弱化断言。
- 用例覆盖：
  - **基础映射**（标准 Aweme/BitRate/cookie，`cate=null`，固定 id/syncTime）：逐字段锁定
    `ViedoType`/`AwemeId`/`Author`/`AuthorId`/`AuthorAvatar`/`AuthorAvatarUrl`/`CreateTime`/
    `Resolution`/`FileSize`/`FileHash`/`Tag1`/`Tag2`/`Tag3`/`VideoUrl`/`VideoCoverUrl`/
    `VideoSavePath`/`VideoCoverSavePath`/`DyUserId`/`CookieId`/`Id`/`SyncTime`。
  - **VideoTitle**：`item.Desc` 空 → `"{Author?.Nickname}-{item.CreateTime}"`；非空 → `item.Desc` 原样。
  - **cate 非空且 `CateType != dy_custom_collect`**：标题变为
    `(Desc 空 ? cate.Name : "[cate.Name]_"+Desc) + "_" + item.MixInfo.Statis.CurrentEpisode`；
    `CateId`/`CateXId` 取 `cate.Id`/`cate.XId`。
  - **OnlyImgOrOnlyMp3**：`savePath` 空 + `!config.DownImageVideo` + (`DownImage||DownMp3`)
    的真/假组合各一例。
  - **DyUserId 分支**：`item.AuthorUserId == 0` → `item.Author?.Uid`；非 0 → `AuthorUserId.ToString()`。
  - **AuthorAvatarUrl 回落**：`Author.AvatarLarger` 首项优先；`AvatarLarger` 空 → `AvatarThumb` 首项。
  - **FileSize 回落**：`bitRate.PlayAddr.DataSize` 为 null → `0`。

### 4. 文档更新 `tests/README.md`

- 「What is pinned」`SyncDecisionHelper` 行追加 `BuildVideoEntity`（纯字段映射 / cate 标题覆盖 /
  OnlyImgOrOnlyMp3 / DyUserId 分支 / AuthorAvatarUrl 回落）。
- 「What is intentionally NOT covered」`DouyinBasicSyncJob` 条目更新：`CreateVideoEntity` 纯映射
  已抽出并 pinned；`DynamicVideos`/NFO 文件写入副作用仍由 job 持有、未覆盖；编排/HTTP/FS/DB
  仍待后续接缝抽取。

## 测试策略与正确性

- 行为保持型重构：成功判据 = 构建 0 错误且 `dotnet test` 全绿（现 37 个 + 新增 BuildVideoEntity 用例）。
- 委托是纯结构搬移，逻辑零改动；首次运行得到的即「当前行为」，锁死该值并在测试旁注明
  pin 的是现状而非应然。
- 若搬移过程中行为确有变化，必须在同一提交内更新对应 golden 值并在 commit message 注明原因
  （沿用 `tests/README.md` 的 refactor-safety 规则）。

## 验证与收尾

- 构建/测试统一用 `DOTNET_ROLL_FORWARD=LatestMajor`（本机 SDK 10，项目 target net8.0）。
- 显式 `git add <path>` 仅暂存目标文件（不用 `git add -A`，避免 CRLF 抖动）。
- 以 `git -c user.name='Claude Code' -c user.email='mjgenab@gmail.com'` 提交。
- 推送到 `origin decompile/dy-sync-lib`；**不合并、不开 PR**（既定约束）。
- 收尾后更新项目记忆，标注本 WARNING 子项进展。
