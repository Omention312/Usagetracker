# UsageTracker — 应用运行时长记录

Windows 10/11 上的本地常驻托盘小工具:采集每个进程的**运行时长**与**前台占用**,在 GUI 里看「今日前台 / 今日后台 / 总计」。
单机、单用户、**普通权限(不需要管理员)**、无网络依赖;数据只落在本机 SQLite。

## 特性一览

- 托盘常驻:1 秒轮询前台归属 + 4 秒进程快照差分,运行会话与前台片段**分离记录**
- 四页界面(今日概览 / 全部应用概览 / 黑名单 / 设置,`Ctrl+1..4` 切页),深浅主题、字号缩放
- 口径清晰:**今日后台** = 应用开着但不在前台的时间(含无窗口的音乐类后台);**总计** = 同应用重叠区间合并后的运行时长
- **睡眠/关机不计入**(60 秒心跳 + 墙钟跳变检测);跨自然日按天切分汇总
- 黑名单(加入后停记、历史保留)、用户可维护的排除名单、开机自启、每日自动备份(`VACUUM INTO`,滚动保留 30 份)
- 附带 CLI:`status / today / top / overview / report`(自包含 HTML 周报)`/ recent / blacklist / repair-spans / autostart`

> 文档:`docs/设计方案.md`(原始设计 v2)· `docs/HANDOVER.md`(**开发交接:构建、运行、数据口径、问题史与实测记录**)
## 下载

- **可执行包(Windows x64,免构建)**:[Releases](https://github.com/Omention312/Usagetracker/releases) 里下载最新 zip,解压后双击 `UsageTracker.Engine.exe`
  —— 需要 **.NET 8 Desktop Runtime**(框架依赖发布)
- 想自己构建:见下方「构建」一节

对应设计文档 `docs/设计方案.md`(v2);开发交接与问题史见 `docs/HANDOVER.md`。

## 目录结构

```
UsageTracker/
  UsageTracker.sln
  UsageTracker.M0Spike/          M0 spike(可行性验证;使用见文末)
  src/
    UsageTracker.Core/           共享库:进程快照/Edge UIA/域名归一/SQLite 数据层
    UsageTracker.Engine/         采集引擎(WinExe 无窗口,托盘常驻)
    UsageTracker.Cli/            查询/维护控制台
  M0-spike-report.md             M0 结论报告
  README.md                      本文档
  .runtime-data/                 本机自检产生的测试库(可删除)
```

## 构建(本地 SDK,不碰系统目录)

```powershell
dotnet build UsageTracker.sln -c Debug
```

需要 .NET 8 SDK(桌面开发)。NuGet 包默认走系统全局包目录;若克隆的**上一级**存在 `.nuget-packages\`(本机开发布局),会自动改用它 —— 见仓库根的 `Directory.Build.props`。

## 运行

```powershell
# 数据目录默认 %LOCALAPPDATA%\UsageTracker;测试/自检时用环境变量覆盖(写在本仓库内):
`$env:USAGETRACKER_DATA_DIR="$PWD\.runtime-data"

# 引擎:双击 = 托盘常驻(无控制台窗口);或用参数跑一次性/自检
UsageTracker.Engine.exe --headless 30     # 跑 30s 后退出(自动化用)
UsageTracker.Engine.exe --selftest 30     # 跑 30s 并自动开/关子进程验证(退出码 0=通过)

# CLI(只读查询,引擎运行中也可用,WAL 并发)
UsageTracker.Cli.exe status
UsageTracker.Cli.exe today
UsageTracker.Cli.exe top 7
UsageTracker.Cli.exe recent 10
UsageTracker.Cli.exe autostart status     # on/off 需给出引擎 exe 路径
```

## M1 验证记录(本机实测)

| 项 | 结果 |
|---|---|
| 前台归属 1s 轮询 → 前台片段无缝衔接(dsh-eac-shell → Edge) | ✅ |
| Edge 域名端到端:前台 Edge 片段带 `www.bilibili.com` | ✅ |
| 生命周期 4s 快照差分:自检子进程 启→止(8s,含检测延迟)入会话表 | ✅ |
| 引擎托盘模式启动无窗口;强杀(模拟崩溃)后遗留 98 个会话,新实例启动“对账封口 98” | ✅ |
| SQLite WAL 写入 + daily_stats 折叠 + integrity_check=ok | ✅ |
| 单实例(Mutex)、干净退出 meta 记录、CLI status/today/top/recent | ✅ |

## M2 记录(本机实测)

| 项 | 结果 |
|---|---|
| 托盘“今日概览”窗口(双击图标/菜单打开,60s 自动刷新) | ✅ 编译通过;与 CLI 共用 StatsReader |
| CLI `overview`:应用 + Edge 站点当日统计 | ✅ 实测输出(Edge 0m18s / www.bilibili.com 0m17s) |
| 自动备份:`VACUUM INTO` → `backups\usage-yyyyMMdd-HHmmss.db` | ✅ 实测产出 61KB 副本 |
| 90 天明细滚动清理(会话+片段)+ 量大自动 VACUUM | ✅ 路径执行(数据新,0 行属正常) |
| 备份滚动保留 30 份 | ✅ 逻辑就绪(未触发) |
| 自检(25s)子进程 启→止 闭环,退出码 0 | ✅ |

## M3 记录(本机实测)

| 项 | 结果 |
|---|---|
| Release 发布到 `release\`(Engine+Cli 同目录,稳定路径) | ✅ |
| 开机自启注册(HKCU Run → release Engine) | ✅ 已注册(`Cli autostart off` 可取消) |
| 验收自动化 `verify\acceptance.ps1`:T2 生命周期 / T4 崩溃对账 / T1 前台精度 | ✅ T2、T4 PASS;T1 需空闲桌面,`verify\t1-idle.ps1` |
| T4 实测:强杀后重启,`reconciled(exit=2)=99` 全部封口 | ✅ |
| CLI `report [days]` 自包含 HTML 周报(无常驻服务) | ✅ 生成 5KB HTML(USAGETRACKER_NO_OPEN=1 可禁止自动打开) |
| CLI `accept-check` 前台覆盖判定 | ✅ |

## M3 已知边界

- T1 精确验收需空闲桌面(引擎 1s 轮询 + 探针窗口抢前台);用户活跃时自动套件会 SKIP。
- 进程退出原因(正常/崩溃)仍不细分:运行中退出=exit_kind 1,重启对账遗留=2;精确到“崩溃”需 WER/审计事件(M3 后增强项)。
- 每次引擎重启会把“仍在运行”的进程续开新会话(旧段先对账封口),保证总时长连续;代价是重启会新增一批行(属设计内)。
- 当前为 framework-dependent 发布(本机已装 .NET 8 桌面运行时);若要拷去无运行时机器,再做 self-contained 单文件发布。

## M1 已知边界(设计内)

- 进程退出原因暂一律 exit_kind=1(快照无法区分正常/崩溃;M3 可加 WER/审计增强)。
- UWP(ApplicationFrameHost)前台识别留待 M2;窗口标题不采;Edge 域名读取失败连读 2 次才切“无站点”片段。
- 系统辅助进程黑名单(msedgewebview2/steamwebhelper 等)在 `UsageTracker.Core.Native.TrackingPolicy`,可按需增删。
- 开机自启未在本机执行(避免改注册表);确认后用 `Cli autostart on <Engine.exe路径>`。

---

## M0 Spike 使用(历史记录)

M0 目标:普通权限验证 3 个技术点(进程快照差分 / Edge UIA 域名 / SQLite+托盘),结论全部通过,详见 `M0-spike-report.md`。

```powershell
# ① 进程启停差分(45s,自动开/关 2 个子进程)
dotnet run --project UsageTracker.M0Spike -- probe-process 45
# ② Edge UIA:自动开 Edge 到 bilibili 并 dump 地址栏候选
dotnet run --project UsageTracker.M0Spike -- probe-edge-dump https://space.bilibili.com
# ③ SQLite 骨架自检 / 托盘冒烟
dotnet run --project UsageTracker.M0Spike -- probe-db
dotnet run --project UsageTracker.M0Spike -- probe-tray 25
```

## 许可证

[MIT](LICENSE) © 2026 Omention312
