# M0 Spike 报告(可行性验证)

> 日期:本次会话 · 对应设计:v2(第八节 M0)
> 工程:`UsageTracker.M0Spike/`(C#/.NET 8;当时为避免污染系统目录,SDK 与 NuGet 包都放在开发目录内)
> 结论:**三个技术点全部实测通过**,可进入 M1。

## 环境与构建

- 当时开发机原无 .NET SDK(仅运行时)。已把 **.NET 8 SDK 8.0.424** 本地安装到开发目录下的 `dotnet-sdk\`,NuGet 包落在 `.nuget-packages\`,未写系统目录。(这些目录未入库;一般克隆用系统 .NET 8 SDK 即可。)
- 构建:`dotnet build UsageTracker.M0Spike.csproj -c Debug` → **0 警告 0 错误**。
- 运行产物:`UsageTracker.M0Spike\bin\Debug\net8.0-windows\UsageTracker.Spike.exe`。

## 探针①:无管理员进程快照差分 → PASS

- 手段:`NtQuerySystemInformation(SystemProcessInformation)`(ntdll P/Invoke,零管理员),以 **(PID, CreateTime)** 为稳定主键做快照差分(间隔 3 s)。
- 实测 45 s:全程捕捉到真实进程启停(测试拉起 msedge 进程树、QQ/网易云子进程等);程序自动开/关 2 个休眠 6 s 的子进程,**启动事件=True、退出事件=True**,闭环成立。
- 顺带验证 PID 复用安全:输出中多次出现同 PID 不同 CreateTime 的进程,主键未串账。
- **M1 注意点**(探针暴露):
  1. 首帧快照应只做“播种”不输出 diff,否则会把历史进程当启动事件刷屏(本次第一拍打印了数百条历史启动)。
  2. 退出检测延迟 = 快照间隔(≤3~5 s),与 v2 验收 T2 的误差界一致。
  3. 噪声过滤需用“交互会话 + 系统白名单 + 父进程树”,不能只看 SessionId(本机 Session 13 含大量自启后台程序,均需进后台池而非使用统计)。

## 探针②:Edge 地址栏 UIA 域名读取 → PASS(核心疑问已实证)

- 手段:`System.Windows.Automation` 读前台 Edge 窗口内地址栏 Edit 的 `ValuePattern.Value`。
- 实测(两次 dump,无需人工干预):
  - `Name="地址和搜索栏" AutomationId="view_1021" Focused=False` → **Value 仍可读到完整 URL**(`https://gradient.shapefactory.co/...`)。这正是 v2 3.4 要求的「Edge 前台但地址栏未聚焦」场景——**成立**。
  - 打开 `https://space.bilibili.com` 新窗口再次 dump → `Value="https://space.bilibili.com/407503099"`,规范化输出 **`www.bilibili.com`** —— bilibili 全子域归并规则验证通过。
  - 通用主域折叠验证:`gradient.shapefactory.co → www.shapefactory.co`。
- **对 M1 的价值**:
  - 地址栏控件可用**稳定特征**定位:中文名“地址和搜索栏” + AutomationId `view_1021`(多语言兜底再加“Address and search bar”等)。
  - 读取不要求焦点在地址栏;全程只取 host、不存完整 URL,符合隐私设计。
  - Edge 每顶层窗口是独立进程 → 前台归属按窗口查进程即可自然得到“哪个窗口在台前”,读该窗口的地址栏即为当前活动标签页。
- 残留风险:仅在一台机器实测;不同 Edge 语言/版本下控件名可能变化——M1 用「特征集(名称含 地址/Address + AutomationId 以 view_ 开头)+ 值以 http/https 开头」多重匹配,并在 T5 验收里加入多种语言可选回归。

## 探针③:SQLite 骨架 + 托盘 → PASS

- SQLite(WAL):4 表(process_sessions / foreground_segments / app_info / daily_stats)+ 索引按 v2 DDL 建库;**写入→查询→`PRAGMA integrity_check=ok` 全通过**,WAL 附属文件确认激活,域名字段按 `www.bilibili.com` 聚合正确(60 000 ms)。
- 托盘:NotifyIcon + 右键菜单 + 气球提示,消息循环运行 25/30 s 后干净退出(exit 0),无异常。
- 可视化核对:多显示器桌面下自动截图未定位到托盘图标,需主人在真机上跑 `probe-tray 30` 用肉眼确认一次(功能层已通过)。

## 通过判据汇总

| 判据 | 结果 |
|---|---|
| ① 快照差分启停事件闭环 | ✅ PASS |
| ② Edge 地址栏未聚焦仍可读 URL + bilibili 归并 | ✅ PASS |
| ③a SQLite 建库/读写/integrity | ✅ PASS |
| ③b 托盘消息循环冒烟 | ✅ PASS(功能);图标肉眼待确认 |

## 对 M1 的落地建议

1. 采集引擎骨架按探针代码合并:前台 1 s 轮询(`GetForegroundWindow`→PID→进程名缓存)+ 生命周期 3~5 s 差分;**首帧播种不输出**。
2. 进程→显示名映射缓存:前台命中新 PID 时用快照映像名兜底,避免每拍 OpenProcess(个别保护进程会被拒)。
3. Edge 域名链路直接复用:前台判定 msedge → 每 2 s 读 `view_1021` 类地址栏 Value → 规范化 host 落库(注意 Edge 子进程多,窗口句柄要用有 MainWindowHandle 的顶层进程)。
4. 系统进程噪声:Session 0 与白名单表(见设计 3.5)+ 父进程为用户的判定。
5. 保持工程双模式骨架(引擎 + CLI 命令),方便后续 T1~T11 自动化验收脚本直接调用。

## 遗留事项

- 托盘图标肉眼确认(可选)。
- 前台归属与生命周期两表的时间轴对账(算“后台时段”)留到 M1 实现并在 T2 验收。
