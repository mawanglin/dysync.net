# 设计：抽取 GetVideoFileName 纯文件名构造逻辑 + 特征化测试

日期：2026-05-20
分支：`decompile/dy-sync-lib`
关联：`docs/code-review-2026-05-18.md`（WARNING：~1440 行 god-class `job/DouyinBasicSyncJob.cs`）；
前序刀：
- `docs/superpowers/specs/2026-05-19-extract-syncjob-pure-logic-design.md`
- `docs/superpowers/specs/2026-05-19-extract-createvideoentity-mapping-design.md`
- `docs/superpowers/specs/2026-05-20-extract-pick-best-video-bitrate-design.md`

特征化安全网纪律见 `tests/README.md`。

## 背景与目标

`GetVideoFileName`（`job/DouyinBasicSyncJob.cs:229-253`）是 `protected virtual string`
基类方法，根据 `cate` / `VideoType` / `Aweme.MixInfo.Statis.CurrentEpisode` 在三条文件
命名路径中分支：(a) `dy_custom_collect` cate 用 BitRate.Format（或 mp4 兜底）；
(b) `dy_series`/`dy_mix` 且 chain 上有 `CurrentEpisode` → `S01E{D2}.mp4`；
(c) 其余 → `{AwemeId}.mp4`。无 I/O、无字段写入。

唯一 override：`DouyinFollowedSyncJob.GetVideoFileName`（`job/DouyinFollowedSyncJob.cs:74-`，
业务逻辑完全独立——构造 Format / FileHash / Height / Width 模板字符串，**不调** `base`）。
本刀**完全不动**该 override。

延续「最低风险、行为保持、先有特征化测试」纪律：将基类体逐字搬到
`SyncDecisionHelper.BuildVideoFileName`，job 内保留 `protected virtual` 薄壳委托。

**显式不做**：不动 `DouyinFollowedSyncJob` 的 override；不动 2 个调用点
（`ProcessSingleVideo:861`、`ProcessImageSetAndMergeToVideo:1068`）；不引入新抽象；
不修复 `cookie`/`config` 参数在 base body 中实际未引用的"形式参数 dead code"——
保留 virtual 契约不变；不修复"TryParse 失败"的不可达 fallback 分支（保持现状）。

## 现状分析

`GetVideoFileName` 基类体组成：

| 部分 | 行 | 纯度 |
|------|----|------|
| `cate != null && cate.CateType == VideoTypeEnum.dy_custom_collect` 分支 | 231-236 | 纯（BitRate.Format / mp4 兜底） |
| `videoType == dy_series \|\| videoType == dy_mix` 子分支 | 239 | 纯，**用 `VideoType` 抽象属性** |
| `int.TryParse + ToString("D2")` 数字 episode 路径 | 242-247 | 纯 |
| 「TryParse 失败」fallback `return $"S01E{...CurrentEpisode}.mp4";` | 249 | **不可达死代码**（见下） |
| 默认 `return $"{item.AwemeId}.mp4";` | 251 | 纯 |

**形式参数情况**：`cookie` 与 `config` 在 base body 中**未被引用**（grep 该方法体确认）；
仅保留为 virtual 契约的一部分以兼容 override（`DouyinFollowedSyncJob` 同签名但用法不同）。

**不可达分支说明**：`item.MixInfo.Statis.CurrentEpisode` 在数据模型中为
`int`（`model/response/DouyinVideoInfoResponse.cs:710 MixStatis.CurrentEpisode`，非可空）。
`int.ToString()` 产出的字符串永远满足 `int.TryParse`，故第 248-249 行的"TryParse 失败 →
返回原值"路径在当前 model 下**不可达**。本刀**不删除**这段死代码（行为保持纪律），但
**不为它写特征化测试**（无法在不改 model 的前提下触发；强行 mock 会引入测试与模型脱节
的风险）。spec 与代码注释里记录此事实即可。

**对 `videoType` 提为入参的处理**：与前几刀（`IsSyncLimitReached`、`BuildVideoEntity`）
完全对齐——基类的 `VideoType` 抽象属性在 helper 端改为 `videoType` 显式入参；job 端
delegate 在调用 helper 时传 `VideoType`，job 自身仍读基类抽象属性，子类感知不到变化。

**调用点 2 处**：
- `ProcessSingleVideo:861` — `var fileName = GetVideoFileName(cookie, item, config, cate);`
- `ProcessImageSetAndMergeToVideo:1068` — `var fileName = GetVideoFileName(cookie, item, config, cate);`

两处**不动**，仍走 virtual 派发（`DouyinFollowedSyncJob` 实例走子类 override；其余实例
走 base→delegate→helper）。

## 架构

### 1. `utils/SyncDecisionHelper.cs` 新增 `BuildVideoFileName`

复用已有 helper（`namespace dy.net.utils`，`public static class SyncDecisionHelper`）。
追加方法到类末尾，紧接当前最后一个方法 `PickBestVideoBitRate` 之后，类关闭 `}` 之前。
**不动**既有 6 个方法。

```csharp
/// <summary>
/// 从 DouyinBasicSyncJob.GetVideoFileName 抽出的纯文件名构造逻辑。
/// 行为逐字保留：cate=custom_collect 用 BitRate.Format（或 mp4 兜底）；
/// videoType=dy_series/dy_mix 且 MixInfo?.Statis?.CurrentEpisode 链非 null
/// → "S01E{D2}.mp4"；其余 → "{AwemeId}.mp4"。
/// 原方法 cookie/config 参数在 base body 中未引用，故 helper 签名不带这两项。
/// 抽象属性 VideoType 提升为 videoType 入参。
/// 「TryParse 失败」分支在当前 model（MixStatis.CurrentEpisode 为 int）下不可达；
/// 保留原代码不删，但不为其编写特征化测试。
/// 由特征化测试 SyncDecisionHelperTests 锁定当前行为。
/// </summary>
public static string BuildVideoFileName(VideoTypeEnum videoType, Aweme item, DouyinCollectCate cate)
{
    if (cate != null && cate.CateType == VideoTypeEnum.dy_custom_collect)
    {
        if (item.Video != null && item.Video.BitRate != null)
            return $"{item.AwemeId}.{item.Video.BitRate.FirstOrDefault().Format}";
        return $"{item.AwemeId}.mp4";
    }
    else
    {
        if ((videoType == VideoTypeEnum.dy_series || videoType == VideoTypeEnum.dy_mix) && item.MixInfo?.Statis?.CurrentEpisode != null)
        {
            // 第一步：将 CurrentEpisode 转换为整数（兼容字符串/数字类型）
            if (int.TryParse(item.MixInfo.Statis.CurrentEpisode.ToString(), out int episodeNum))
            {
                // 第二步：格式化数字，确保 1-9 补 0，10+ 保持原样
                string episodeStr = episodeNum.ToString("D2");
                return $"S01E{episodeStr}.mp4";
            }
            // 容错：如果转换失败，使用原始值（避免程序报错）
            return $"S01E{item.MixInfo.Statis.CurrentEpisode}.mp4";
        }
        return $"{item.AwemeId}.mp4";
    }
}
```

- 行 229-253 的逻辑**逐字**搬入。表达式、注释（含「第一步」/「第二步」/「容错」中文注释）、
  空白与花括号风格逐字保留。
- 仅做必要替换：`VideoType` → `videoType`。其余原样。
- 不补 null 守卫、不重写 `??`、不"美化"早返回风格。

### 2. `DouyinBasicSyncJob.GetVideoFileName` 改为薄委托

签名/可见性/参数顺序/XML doc 注释保持不变。方法体替换为：

```csharp
protected virtual string GetVideoFileName(DouyinCookie cookie, Aweme item, AppConfig config, DouyinCollectCate cate)
    => SyncDecisionHelper.BuildVideoFileName(VideoType, item, cate);
```

- `cookie`/`config` 参数仍在 virtual 签名上（virtual 契约不变）；
  仅 base impl 不再引用它们，行为等价。
- `DouyinFollowedSyncJob.GetVideoFileName` override **零改动**——继续覆盖自己的实现，
  不通过 base，不受 helper 引入影响。

### 3. 特征化测试 `tests/dy.net.Tests/SyncDecisionHelperTests.cs` 追加

新增 `// ---- BuildVideoFileName ----` 小节，包含 7 个 `[Fact]`。

| # | 用例（示意） | 输入 | 期望 |
|---|---|---|---|
| 1 | `CustomCollect_WithBitRate_UsesFormat` | `cate.CateType=dy_custom_collect`；`item.Video.BitRate=[{Format="webm"}, ...]`；`item.AwemeId="123"` | `"123.webm"` |
| 2 | `CustomCollect_VideoNull_Mp4Fallback` | `cate.CateType=dy_custom_collect`；`item.Video=null`；`item.AwemeId="123"` | `"123.mp4"` |
| 3 | `CustomCollect_BitRateNull_Mp4Fallback` | `cate.CateType=dy_custom_collect`；`item.Video.BitRate=null`；`item.AwemeId="123"` | `"123.mp4"` |
| 4 | `Series_NumericEpisode_S01E_D2_Padded` | `videoType=dy_series`；`cate=null`；`item.MixInfo.Statis.CurrentEpisode=5` | `"S01E05.mp4"` |
| 5 | `Mix_NumericEpisode_S01E_D2_NotPadded` | `videoType=dy_mix`；`cate=null`；`item.MixInfo.Statis.CurrentEpisode=12` | `"S01E12.mp4"` |
| 6 | `DefaultBranch_AwemeIdMp4_WhenNotCustomCollectAndNotEpisodic` | `videoType=dy_follows`；`cate=null`；`item.AwemeId="123"` | `"123.mp4"` |
| 7 | `CateNonCustomCollect_FollowsDefaultBranch` | `cate.CateType=dy_collects`；`videoType=dy_collects`；`item.AwemeId="123"` | `"123.mp4"`（验证 cate 非 custom_collect 时与 cate=null 同路径） |

**不写**：「TryParse 失败」fallback 分支测试——`MixStatis.CurrentEpisode` 为 `int`，
`.ToString()` 永远可被 `int.TryParse` 解析，不可达。

**测试构造原则**：
- 纯内存，不使用 `TestDb`。
- 助手方法在小节内部定义（`AwemeWith(awemeId, ...)` / `AwemeWithEpisode(awemeId, episode)` /
  `AwemeWithBitRateFormat(awemeId, format)` / `Cate(cateType)` 等），与 `BuildVideoEntity`/
  `PickBestVideoBitRate` 小节的助手隔离。
- 每个 Fact 一个不变量；断言用 `Assert.Equal(expected, actual)` 锁文件名字符串。
- 测试旁短注释 `// pin: current behavior, not aspirational`（沿用前刀风格）。

### 4. 文档更新 `tests/README.md`

- 「What is pinned」`SyncDecisionHelper` 行追加 `BuildVideoFileName`
  （custom_collect Format vs mp4 兜底 / series-mix 数字 episode 的 S01E{D2} / 默认 AwemeId.mp4 /
   cate 非 custom_collect 走默认）。
- 「What is intentionally NOT covered」`DouyinBasicSyncJob` 条目更新：
  `GetVideoFileName` 基类纯逻辑已抽出并 pinned；`DouyinFollowedSyncJob.GetVideoFileName`
  override 仍是子类业务实现，未覆盖；`「TryParse 失败」fallback` 在当前 model 下不可达，
  保留原代码但不写测试；剩余 `CreateSaveFolder`、`AutoDistinct`、编排/HTTP/FS/DB 仍待
  后续接缝抽取。

## 测试策略与正确性

- 行为保持型重构：成功判据 = 构建 0 错误（`dotnet build` 含 `dy.net` Web 项目）且
  `dotnet test` 全绿（现 53 个 + 新增 BuildVideoFileName 用例）。
- 委托是纯结构搬移，逻辑零改动；首次运行得到的即「当前行为」，锁死该值并在测试旁
  注明 pin 的是现状而非应然。
- 若搬移过程中行为确有变化，必须在同一提交内更新对应 golden 值并在 commit message
  注明原因（沿用 `tests/README.md` 的 refactor-safety 规则）。
- 子类不动：`DouyinFollowedSyncJob.GetVideoFileName` override 必须零改动，由 grep +
  diff 双重确认。

## 验证与收尾

- 构建/测试统一用 `DOTNET_ROLL_FORWARD=LatestMajor`（本机 SDK 10，项目 target net8.0）。
- 显式 `git add <path>` 仅暂存目标文件（不用 `git add -A`，避免 CRLF 抖动）。
  涉及文件：`utils/SyncDecisionHelper.cs`、`job/DouyinBasicSyncJob.cs`、
  `tests/dy.net.Tests/SyncDecisionHelperTests.cs`、`tests/README.md`、
  实现计划与本 spec（`docs/superpowers/{specs,plans}/...`）。
- 以 `git -c user.name='Claude Code' -c user.email='mjgenab@gmail.com'` 提交，
  沿用前三刀的提交作者风格。
- 推送到 `origin decompile/dy-sync-lib`；**不合并、不开 PR**（既定约束）。
- 收尾后更新项目记忆，标注本 WARNING 子项进展（god-class 拆分进度：四刀完成）。
