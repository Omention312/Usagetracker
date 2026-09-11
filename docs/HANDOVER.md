# UsageTracker — 项目交接说明(2026-09,无上下文可读)

> 本文件供“没有任何前文的新对话”快速接手。路径均相对**仓库根**(`UsageTracker\`);文中提到的本机专属目录(自带 .NET SDK、`问题\` 截图、`dist\` 打包输出)未入库。

## 1. 这是什么

Windows 10/11 本地应用「应用运行时长记录」:常驻托盘后台采集每个进程的运行与会话,并在 GUI 展示各应用「今日前台 / 今日后台 / 总计」使用时长、开始记录时间、黑名单等。无管理员权限、无网络依赖、单机单用户。

## 2. 代码与运行

- 解决方案:`UsageTracker.sln`(仓库根)
- 三个工程:
  - `src\UsageTracker.Core` — 数据层/进程快照/域名归一/SQLite
  - `src\UsageTracker.Engine` — WinExe 采集引擎 + WinForms UI(唯一可交互程序)
  - `src\UsageTracker.Cli` — 只读查询/维护命令行
- 数据目录默认 `%LOCALAPPDATA%\UsageTracker`(可用环境变量 `USAGETRACKER_DATA_DIR` 覆盖;`USAGETRACKER_DPI=96/120/125/144…` 强制 DPI 布局系数;`USAGETRACKER_SLEEP_JUMP_MS` 墙钟跳变阈值(默认 90000,测试可压到 5000);`USAGETRACKER_DEBUGLAYOUT=1` 打开布局诊断落盘;`USAGETRACKER_UI` 相关为开发后门)
- 构建(仓库自带本地 SDK,勿装系统目录):
  ``dotnet build UsageTracker.sln -c Debug``
- 发布:`…\UsageTracker\release\`(引擎入口 `UsageTracker.Engine.exe`,最新运行实例以此为准)
- 测试工具:引擎 `--ui-shot today|allapps|settings|menu <png>` 可离屏出图,但**用户已明确:视觉类结论一律以真机人工确认为准,禁止用离线渲染“验收”**。
- 维护命令:`release\UsageTracker.Cli.exe repair-spans [--apply] [--any-day] [--min-gap-minutes N]` —— 历史幻影修边(睡眠/关机被算成运行时长的那批会话),默认只预览(见 §5.12)。

## 3. 当前页面结构(GUI 逻辑简述)

- 主窗口左侧导航:今日概览 / 全部应用概览 / 黑名单 / 设置(Ctrl+1..4 也可切页)
- 每个页面顶部是页面大标题;下方滚动列表
- 「今日概览 / 全部应用概览」列表采用“区块头(名称|今日前台使用时长|今日后台使用时长|总计使用时长)+ 卡片行”:
  - 每张卡片:左=应用图标+应用名(第1行)、今日次数(第2行)、开始记录时间:HH:mm:ss(第3行,独立成行);
  - 中部三列数值(按列水平居中),右侧“加入/取消黑名单”按钮;
  - 「今日概览」下方另有可折叠「后台运行程序」区(默认收起)
- 数据刷新:5 秒一次**局部刷新**,只更新三列数值,不重建卡片、不改折叠开合(防闪烁)
- 深色/浅色主题可切换、字号可缩放;滚动条为自绘灰条(悬停/拖动提亮)

## 4. 数据口径(重要,用户多次强调)

- **今日前台** = 前台片段 ∩ 今日
- **今日总计(运行)** = 进程会话 ∩ 今日,且**同一应用的重叠区间已合并**(引擎重启会对仍运行进程重复播种会话,不合并会虚高到数百小时)
- **今日后台** = 今日总计 − 今日前台(即“运行但不处于前台”的真后台)
- 应用名下方“开始记录时间” = 该应用今日最早一次会话开始时刻
- 引擎黑名单/崩溃对账/自动备份等核心逻辑不在本次改动范围
- **(2026-09-11 修订,已实现,见 §5.11)**:①会话起点不早于“引擎开始观测的时刻”(不再用进程创建时间);②对账封口上限取“最后心跳 `meta['last_seen']`”,不是当前时刻;③睡眠/断电期间的墙钟**不计入**任何时长(loop 墙钟跳变 → 片段/会话封口到跳变前,新增 `exit_kind=3`);④跨自然日的会话/片段按天切分折叠进 `daily_stats`(不再整段记在起点那天)。

## 5. 已知未解决问题(接手者从这里继续;全部需真机人工确认)

1. **“大空白栏/遮挡”仍存在**:用户描述页面大标题下半与第一行卡片上半之间被一条空白栏遮挡(问题文件夹最新截图见**本机** `问题\` 目录(未入库))。已尝试:压缩页头高度、Flow 顶部留白 44→20→8、卡片/列头宽度铺满。**疑似原因**:WinForms Dock(页头 Top + ScrollArea Fill)在本机显示缩放环境下与实际渲染错位,或页头背景/空白带与滚动区重叠;尚未在真机确认修好。建议:逐控件打印实际矩形对照,或在真机不同缩放档核对“标题底缘 vs 首行卡片顶缘”的坐标。

    **2026-09-11 真机截图实测(窗口 1459x942,深色主题,设置里“字体大小 = 特大”),实情基本清楚**:
    - 「今日概览」(`问题\15.png`):可见内容顶部的第一行是「今日应用(55)」y≈175;但代码里 Flow 的前两个子控件是「开始统计时间:…」和「今日:前台使用 … · 总运行 …」,它们应当落在 y≈105~165 —— **正好是被页头标题带盖住的区间**。也就是说“大空白栏”并不是空白,而是**页头带压在滚动区之上、把内容顶部两行遮住了**。
    - 切到「全部应用概览」(`问题\16.png`):列头行被切掉上半(只剩下半截),整块内容比「今日概览」高约 130px;同样看不到顶部的「开始统计时间/口径说明」。
    - 两页右侧都**没有滚动条**:`15.png` 中 x≈1140~1459 一整条是纯背景色(#101010),而列表有 55 行、明显溢出。
    - 代码线索:`PageBase` 构造函数里 `Controls.Add(_scroll)`(Dock=Fill)先于 `Controls.Add(head)`(Dock=Top),再用 `Controls.SetChildIndex(head, 0)` 纠正层级;WinForms 的 Dock 按 z 序倒序布局,这种写法很容易让 Fill 的滚动区拿到**整个客户区**、页头只是overlay 画在上面 —— 与“内容顶部被盖住”完全吻合。建议:先 `Add(head)` 再 `Add(_scroll)`,或让滚动区避让(`Dock=Fill` + 相应 `Padding`)。
    - 诊断入口已有但不可用:`PageBase.DebugLayout()`(环境变量 `USAGETRACKER_DEBUGLAYOUT=1`)用 `Console.WriteLine`,而引擎是 WinExe 没有控制台,输出会丢 —— 改成追加写 `%LOCALAPPDATA%\UsageTracker\logs\engine.log` 就能直接拿到真机坐标对照。
2. **列头没有随卡片伸缩**:现象=窗口拉宽时卡片伸长但列头未同步;代码中列头与卡片共用 `RowGeom.Cols(Width)` 且由 `ScrollArea.SetChildWidth` 统一设宽,理论上同宽;真机仍不同步。建议在真机抓取“列头控件宽 vs 行卡片宽”对照排障(可能是嵌套 Flow 第二次布局顺序/ClientSize 读取时机问题)。
3. **应用名称列宽 / 开始记录时间整行显示**:用户已反馈需“名称列+40px、列距-20px、列宽足够”;最近一次已按该参数修改并发布(见 §6)。仍需真机确认名称与“开始记录时间”是否完整(名称列过窄时第3行会被裁)。
4. **高分屏与缩放**:已实现 SystemAware + DPI 布局系数(UI 尺寸随 DPI 放大)与按尺寸取大图标;但用户要求仅以真机 100/125/150% 各档确认布局无重叠、文字/图标清晰。此环境的系统缩放表现异常,PMv2 曾致“全乱”,勿随意改回 PMv2。
5. **历史遗留已知边界(未做)**:进程退出原因细分、UWP 前台细化、开机自启是否注册需确认(`Cli autostart status`)、图标在 >150% 下如需更清晰可接入 48/64 大图标源(现已按绘制尺寸取 exe 图标资源)。

6. **滚动条不显示**(2026-09 用户新报,未修):列表内容明显超出窗口时,右侧自绘滚动条不出现。代码路径 `UI\ScrollArea.cs`:`OnPaint` 第 165 行有 `if (ContentHeight <= ClientSize.Height) return;`(内容不足则不画),而 `ContentHeight = Max(_content.Height, ClientSize.Height)`;若 `ClientSize.Height` 在缩放/Dock 错位环境下被算得比真实可见区域大,则「内容不足」永远成立——滚动条不画,`MaxOffset` 也为 0(滚轮同时失效)。**疑与 §5.1/5.2 同源(Dock/缩放下的尺寸错位)**。排查方法:在 `LayoutContent()` 里把 `_content.Height` / `ClientSize.Height` / `MaxOffset` 写进 `engine.log`,对照真机看是哪一侧数值不合理。
7. **黑名单应用仍出现在「今日概览」**(2026-09 用户新报,未修):黑名单页文案承诺「加入后不再出现在今日概览/全部应用概览」,实际仍显示。定位:`UI\TodayPage.cs` 的 `Build()` 直接渲染 `Db.TodayTrue()`,**没有任何 `IsIgnored` 过滤**;`UiDb.TodayTrue()`(UiDb.cs:182)也不过滤 `app_info.is_ignored`。对照 `AllAppsPage.cs:51` 有 `if (ignored) return;` ——即「今日概览」漏掉了同一层过滤,属实现遗漏,不是口径问题。修法:在 `UiDb.TodayTrue()` 内过滤 `is_ignored=1`(一处生效)。注:`RunningApps()` 已过滤黑名单,「后台运行程序」区应当是对的。
8. **今日后台时长数据依旧异常**(2026-09 用户新报,未修,需具体数值):口径 `bg = run − fg`。可疑点:`fg` 用 `SUM(...)` 逐段相加、**没有像 `run` 那样做重叠合并**(UiDb.cs:196-205),而 `run` 有合并(UiDb.cs:208-245)——引擎重启重复播种前台片段时 `fg` 会虚高、`bg` 被 `Math.Max(0, ...)` 压到 0;若会话长时间挂后台(应用开着不动),`bg` 又会接近整日。需用户给出「应用名 / 卡片三列数值 / 同期 `Cli today` 或 `overview` 输出」才能判定是口径算错还是显示错。
   - **2026-09-11 补充(截图 `18.png` 设置页)**:黑名单页(`17.png`)当前只有一条 **AdobePCBbroker(AdobePCBbroker.exe)**。它加入后仍出现在「今日概览」——与 §5.7 的代码结论一致;因该列表按“今日前台时长”降序、共 55 条,AdobePCBbroker 排在靠后位置,截图首屏覆盖不到,需滚动截图或直接按代码修。
   - **口径对照(供判断“异常”)**:同一时刻 `Cli today` 的“总运行”来自 `daily_stats`,是**未做重叠合并的逐会话累加**(例:Edge 59 次 → 2h26m);界面走 `TodayTrue()` 并做了重叠合并(例:Edge 前台 1m1s + 后台 4m21s ≈ 5m22s)。两者口径不同,直接对比会显得“异常”,需要区分“口径差异”与“真算错”。另:`15.png` 中「文件资源管理器」后台显示 7?42?? (与前台 40s),而该会话按 CLI 是 07:35:22 起、仍在运行 —— 该问题已定案(2026-09-11):真机确认为 **7h42m**(后一张 19.png 为 7h49m/总计 7h51m),机制与修法见 §5.10.5 —— 是“跨午夜播种起点 + 钳到今日 00:00”,不是时间戳垃圾、也不是口径问题。

9. **卡片宽度与列几何不匹配:第 3 列(总计)被裁掉、「今日后台」数值被“加入黑名单”按钮压住**(2026-09-11 真机截图定位,未修):
   - 实测(`15.png`):卡片左缘 x≈274、右缘 x≈1140(宽约 866);内容右缘约 x≈1443(导航栏右缘约 x≈250)——**卡片右侧空出约 300px,并不“铺满右缘”**。
   - 列头「今日前台使用时长」中心 x≈760、「今日后台使用时长」中心 x≈1016(间距 256px);按同间距推算第 3 列应落在 x≈1272,但该处**既无列头也无数值**(整条为背景色#101010)→ 第三列(总计使用时长)被画到卡片矩形之外、被裁掉了。
   - “加入黑名单”按钮(粉底)约 x 957~1094,与「今日后台」数值(x≈964~1050)**完全重叠**,数字被按钮文字盖住(OCR 也只能读出“4mjJ32 名单”这类混排),这本身就是可读性 bug。
   - 数值比:866/1190 ≈ 0.73 ≈ 1/1.37,而设置页实测**字号 = 特大**。**已排除“双重缩放”猜想**:2026-09-11 用户把字号从“特大”调回“标准”后,三个列头都能显示,但数值仍压在按钮上、卡片右侧仍是空条 —— 真因另在(见 §5.10.3/§5.10.4:内层列表容器被 AutoSize 钉死在 900 宽 + `RowGeom` 的 240px 硬下限)。

## 5.10 实测定位结果(2026-09-11 · 用可落盘的 DebugLayout 一次测出,均未修)

> 诊断开关:`USAGETRACKER_DEBUGLAYOUT=1` 后跑 `Engine.exe --ui-shot today <png> dark 1.0`(或正常启动引擎后点一圈页面),
> 结果写 `<数据目录>\logs\layout.log`。**这是数值诊断,不是验收**;视觉验收仍以真机为准。
> 本次环境:page=1197x903、scrollBounds=(0,0,1197,903)、flow=1199x8460、uiScale=1.25、dpi=120。

1. **“大空白栏”= 页头带盖住内容(已证实)**:`scrollBounds=(0,0,1197,903)` 与 `page=1197x903` **完全相等** → Dock=Fill 的滚动区吃掉了整个页面,页头带(Dock=Top,高 56×UiScale≈70px)只是 overlay 画在上面;Flow 第 0 个子控件(汇总面板 loc=(2,8) 1193x100)整块落在 y 0~100,被页头带吃掉大半 —— 真机上就是“标题下那条空白栏、汇总行看不见”。**修法**:先 `Controls.Add(head)` 再 `Add(_scroll)`(或 `SetChildIndex(_scroll, 0)` 让 Fill 最后布局),或显式把 `_scroll.Top` 设为页头高;`PageBase.cs` 第 31~39 行。
2. **滚动条永远不显示(已证实)**:layout.log 里 `drawBar=True sbW=30 thumbH=96 maxOffset=7565`,量测全对、OnPaint 也会画,但**自绘 thumb 被内容 Flow 盖住** —— `_content.Width = ClientSize.Width - 4`(1199)铺满整条滚动条带,而 WinForms 子控件恒画在父控件之上;同理 `OnMouseDown` 的拖动命中区也被子控件吃掉(滚轮还能用,因为 MouseWheel 未被处理会向上冒泡)。**修法**:内容宽度减掉 `SbW` 留出滚动条带,或把 thumb 画在 `BringToFront` 的覆盖层控件里;`ScrollArea.cs`。
3. **卡片恒定 ~900 宽、右侧 ~300px 空条(已证实)**:内层列表 `FlowLayoutPanel` 实测 `900x8234`,外层 Flow 是 1199x8460 —— AutoSize 把它钉在“首选宽度”,而首选宽度来自 `new ColumnHeader { Width = 900 }`(`TodayPage.cs` / `AllAppsPage.cs`),`SetChildWidth` 设的 1199 被 AutoSize 顶回去。**修法**:列表容器不要 AutoSize 宽度(或让 900 跟随父宽),让 `SetChildWidth` 真正生效。
4. **数值压住“加入黑名单”按钮、第 3 列(总计)被裁(已证实)**:`RowGeom.Cols()` 的 `cw = Math.Max(240, (avail-2*gap)/3)` 有 **240px 硬下限**,行宽 900 时三列(3×240+2×12.5≈745)从 nameEnd 起硬排 → 第 2 列压到按钮、第 3 列越过行右缘被裁(离屏图里“总计”只露出半截、数值只剩 “26/25/81” 几个字符)。**修法**:①先修第 3 条让行宽回到内容区宽;②240 下限改为“按可用宽度收缩 + 最小值保护”;③按钮与最后一列之间留固定安全间距;`RowTable.cs:14-25`。
5. **今日后台/总计虚高(7h51m / 7h37m / 8h2m)机制定案 = 跨午夜播种 + 钳到 00:00(已证实)**:
   - `EngineHost.cs:125` 播种已有进程时写入 `started_at = r.CreatedMsUtc`(进程真实创建时间);开机就在跑的进程(explorer / winlogon / Rainmeter / AdobeIPCBroker…)会话起点在今日 00:00 之前。
   - `UiDb.TodayTrue()` 把每条会话区间与“今日 00:00”求交(`Math.Max(started_at, s)`),**跨午夜会话被整段算进“今日”** → 实测 08:02 时 explorer `run=8h2m`(恰好=距 00:00)、`winlogon run=8h2m cnt=0 first=-`(它唯一的会话起点在昨天 23:17)、`AdobeIPCBroker run=7h37m`(=`[00:00, 07:37:53]`)—— 与真机截图上的 7h51m / 7h37m 一一对应。
   - 叠加因素:`ReconcileOpenSessions` 用 `ended_at = now-1000` 封口遗留会话(`UsageDb.cs:257`),把“昨晚 23:17 之后关机/休眠”的空档也算成运行时间;旁证是 DB 里 00:00~07:00 **没有任何会话行**。
   - **已排除**:①时间戳垃圾(2000 行窗口内 explorer/Rainmeter 各只有 2 条会话,起点都是 07:35:22 / 07:38:02,无 1601/1970 怪值);②口径问题(用户已确认:后台本来就该算“应用开着但人不在”,含无窗口的音乐类后台)。
   - **修法建议**:①播种时 `started_at` 用“引擎开始观测的时刻”而不是进程创建时间(要不要保留旧设计里“总时长连续”的意图由用户定);②`ReconcileOpenSessions` 的 `ended_at` 用最后一次心跳/检查点,而不是重启时刻;③`TodayTrue()` 左界再夹一层 `max(今日00:00, 记录起点)`。
6. 资料夹里能看到 `winlogon` 这类**系统进程**也在被记录;若不需要,应加进 `TrackingPolicy` 黑名单。
7. **诊断设施已就位(本次唯一改动的代码)**:新增 `UI\LayoutLog.cs`,并把 `PageBase.DebugLayout()` 从 `Console.WriteLine` 改为落盘 `logs\layout.log`(环境变量 `USAGETRACKER_DEBUGLAYOUT=1` 时启用),`ContentChanged()` 自动触发;`ScrollArea.GeometryInfo()` 输出量测值;`TodayPage` 会把 run/bg ≥2h 的应用与 `Cli today` 口径一起落盘。**全部为空开关保护,未设环境变量时零开销;`release\` 产物未动,运行中的实例不受影响。**

## 5.11 数据层修复(2026-09-11 · 已改代码 + 已实测验证,**但尚未发布到 release**)

用户拍板:**(C)=① 已有进程从“引擎开始观测”算起** + **加心跳** + **⑧ 日汇总按天对齐**;另加一项必要补充(睡眠检测,否则明天早上还会复现同一类)。改动集中在 `UsageTracker.Core\Data\UsageDb.cs` 与 `UsageTracker.Engine\EngineHost.cs`:

1. **会话起点 = 观测起点((C)=①)** —— 播种已有进程时 `started_at` 不再取进程创建时间:引擎启动前就在跑的进程 → `obsEpoch`(引擎开始观测时刻);正常新启动的进程 → 其创建时刻(≈观测时刻,更准);垃圾/未来时间戳 → `now` 兜底。`CreateTime` 仍完整保留为 `pid_born`(身份键,防 PID 复用串账)。→ 没被观测到的时段(含关机/睡眠)不再被补成运行时长。
2. **对账封口 = 上次心跳** —— 新增 60s 心跳 `meta['last_seen']`(引擎启动时、每 60s checkpoint、干净退出各写一次);`ReconcileOpenSessions` 用它而不是“当前时刻”作为 `ended_at` 上限(设计 §4.4-3 原文就要求“按上次心跳/上次关机时刻”,旧实现违反了它)。误差上界 = 心跳周期 60s,与设计“断电丢失窗口 ≤60s”一致。
3. **睡眠/断电墙钟跳变检测(必要补充)** —— loop 每 200ms 一拍,若两拍间隔 > 阈值(`USAGETRACKER_SLEEP_JUMP_MS`,默认 90000,测试可压到 5000),则把前台片段与所有会话封口到“跳变前”时刻(新增 `exit_kind=3`),清空会话/进程缓存并把观测起点前移到恢复时刻。→ 落实设计 §3.5“睡眠/断电:心跳大跳变 → 片段封口到跳变前”与验收 **T6“睡眠墙钟不计入时长”**。
4. **⑧ 跨自然日折叠** —— 新增 `FoldDailyRange()`:把 [from,to] 按本地自然日切开、分别累加到各自那天(秒数按切片占比分配、余数补最后一片,Σ 切片 == 总秒数;会话次数只记起点那天);`SessionClose` / `WriteSegment` / `ReconcileOpenSessions` 全部改走它。→ 修掉“整段时长记在会话起点那天”的偏差,`Cli today/report` 与界面口径不再天然打架。

**实测验证(隔离数据目录,未碰真实库与运行中的实例)**:

| 验证项 | 结果 |
|---|---|
| 自检闭环 | `--selftest 30` → 日志 `[引擎退出] 自检:子进程 启动=True 停止=True` ✓ |
| 对账封口上限 | A(08:12:35 起)被强杀、最后心跳 08:13:36;B(08:14:25 起)对账日志 `封口上限=上次心跳 09-11 08:13:36` → 中间 49s 空档未计入(旧代码会封到 08:14:24) ✓ |
| 会话起点 | 库里所有“引擎启动前就在跑”的进程,会话起点 = 08:14:25/08:14:58(观测起点);`Cli today` 中资源管理器 `总 1m0s`,不再出现 7h37m/8h ✓ |
| 睡眠跳变 | 用挂起进程模拟睡眠 9s → `[睡眠/断电] 墙钟跳变 9s:片段与会话已封口到 08:15:05(会话 79 条,exit_kind=3)`;库内对应行 `08:14:58→08:15:05 (0m6s) exit=3` ✓ |
| 日汇总折叠 | `daily_stats` 数值与封口后的会话一致 ✓(跨天分支需等真实跨午夜数据确认) |

**尚未做(接手者注意)**:
- `release\` **未重新发布** → 正在运行的实例仍是旧行为;要生效需“停引擎 → publish → 重启”。
- 真实库里**已存在的历史幻影行**(昨天启动、今早 07:37:53/07:42:53 被封口的那些)**今天仍会算进今日总计**,明天 00:00 后自然失效;若想今天就干净,需要一次性的“按最后一次活动时刻修边”修复(未做)。
- 布局组 ④⑤⑥⑦、黑名单过滤(§5.7)、系统进程黑名单(§5.10-6)均未动。
- 干净退出时**没有**主动封口会话(留给下次对账),所以 `exit_kind=2` 里混着正常退出的情形 —— 可选改进:在 `finally` 里封口,可把误差从 ≤60s 降到 0、并让 exit_kind 语义更干净。

## 5.12 布局修复 + 发布 + 历史修边(2026-09-11 08:31 完成,本次全部落地)

**布局四项(§5.10 的 1~4,已改并实测)**

| 项 | 修前 → 修后(离屏矩形实测) |
|---|---|
| 页头遮挡「大空白栏」 | `scrollBounds=(0,0,1197,903)`(等于整页) → **`(0,70,1197,833)`**。删掉 `PageBase` 里那句 `Controls.SetChildIndex(head, 0)` —— 它让 Dock=Fill 的滚动区先占满整页,页头带退化成 overlay,把 Flow 顶部两行(「开始统计时间」/「今日:前台使用 …」)整块盖住。现在两行可见。 |
| 滚动条不显示 | 内容宽度 `ClientSize.Width - 4`(盖满滚动条带) → **`ClientSize.Width - SbW - 4`**。WinForms 子控件恒画在父控件之上,不自留带子则 thumb 永远被盖住、拖动命中区也被吃掉(滚轮靠事件冒泡还能用)。 |
| 卡片只有 900 宽、右侧 300px 空条 | 内层列表 **900 → 1163**(= 内容区宽)。`ScrollArea.SetChildWidth` 改为「先按目标宽铺满子项,再设容器宽度」,否则 AutoSize 会把容器顶回 `ColumnHeader` 的初始 900。 |
| 数值压「加入黑名单」、总计列被裁 | `RowGeom.Cols()` 去掉 `Math.Max(240, …)` 硬下限,改为**按可用宽度等分 + 逐列夹紧在按钮左侧**。三列完整、数值不再压按钮。 |

**顺带修的两处**
- **黑名单过滤(§5.7)**:`UiDb.TodayTrue()` 末尾按 `app_info.is_ignored` 过滤 → 加入黑名单的应用立即从「今日概览」消失(历史仍留在库里,可取消后继续累计)。
- **干净退出封口**:引擎 `finally` 里把仍开着的会话按 `exit_kind=4` 封口 —— 误差从「≤心跳周期 60s」降到 0,`exit_kind=2` 今后只代表崩溃/异常重启遗留。

**历史修边(新 CLI 命令,已对真实库执行)**
- 命令:`repair-spans [--apply] [--any-day] [--min-gap-minutes N]`(默认只预览;默认只处理**跨自然日边界**的行)。
- 原理:找出“区间内部有一整段完全没有任何写入活动”的会话(睡眠/关机),切开最大的那段死区 —— 前段封口、后段续开(与引擎自身行为一致),并同步改正 `daily_stats`;一条会话跨多个死区时循环跑到收敛。
- 真实库结果:**29 条候选 → 56 行改动,剔除 242h54m 凭空时长,2 轮收敛,`integrity ok`**。
- `--any-day` 模式(连同日长会话一起看)在副本上试过但**故意不启用**:它会把“午后离席两小时”这类**真会话**也裁掉(初次预览合计要剔除 5343h),过激。
- 修边后真实库实测(新版本):`文件资源管理器 fg=2m32s bg=53m50s run=56m23s`(修前 `bg 8h29m / run 8h30m`);56 分钟 = 07:35 登录至今的真实时长 ✓。winlogon 等同批幻影行同步消失。
- 备份与回滚:`backups\manual-before-repair-<时间戳>\`(db + wal + shm)、`UsageTracker\release_backup_20260911\`(旧 release 全量)。

**发布记录**:`dotnet publish src\UsageTracker.Engine -c Release -o release`(Cli 同参数),引擎已重启;启动日志 `对账封口 76 个遗留会话;封口上限=上次心跳 无(退回当前时刻)` —— 首次启动尚无心跳属预期,之后每 60s 写 `meta['last_seen']`。

**仍未做**:①真机人工验收(100/125/150% 三档 + 标准/大字号观感,以及“拉窗口看列头是否跟着动”)——离屏只能验证数值与结构;②`9/8–9/9` 的同日长会话未修边(见上,有意);③`winlogon` 等系统进程已在 §5.13 入排除名单(并支持 `<数据目录>\tracking-exclude.txt` 用户名单);④§5.3「名称列宽/开始记录时间整行显示」在布局修复后应已正常,待真机确认。

## 5.13 第三轮(2026-09-11 21:08 发布):系统进程排除 / 修边智能模式 / 列头重绘

1. **系统进程不再入库**:`TrackingPolicy.ExcludedExe` 追加 Windows 会话/外壳基础设施 —— `winlogon.exe`(曾被当成“应用”记了 8h+)、`userinit.exe`、`smss.exe`、`taskhost.exe`、`audiodg.exe`、`MHostProcess.exe`、`ShellHost.exe`、`rdpclip.exe`、`WUDFHost.exe`、`SecurityHealthService/Systray.exe`、`MoUsoCoreWorker/usocoreworker.exe`、`MusNotification*.exe`、`rundll32.exe`。另新增**用户可维护的排除名单** `<数据目录>\tracking-exclude.txt`(每行一个 exe 名,`#` 注释),引擎启动时加载并写日志 —— 以后加规则不必改代码。
   - 已验证:21:08 重启后不再产生新的 `winlogon` 会话(库里最后一条是 20:57 旧实例的产物)。
   - 注意:排除只影响**今后**的记录,历史行保留(与黑名单“历史数据保留”口径一致);今天列表里可能仍看得到,明天自然消失。
2. **修边命令进入智能模式(用户要求“启用”同日扫描)**:默认范围 = **跨自然日边界 + 心跳纪元之后的同日空档**。新增 meta `hb_since`(引擎首次运行新版本时写入):心跳之后“无写入”可确证引擎没在跑;心跳之前“无写入”也可能只是锁屏,盲修会裁掉真会话(首轮预览曾合计要剔除 **5343h**,故不做默认)。`--any-day` 保留为显式激进模式。真实库复验:0 条候选,范围显示“跨自然日 + 心跳后的同日” ✓。
3. **列头不随窗口缩放(§5.2 的真因)**:`ColumnHeader` 缺 `ControlStyles.ResizeRedraw` —— 列位置按 `Width` 计算,但控件尺寸变化时不重绘,于是“卡片跟着窗口伸缩、列头停在旧像素上”。已给 `ColumnHeader` 与 `ToggleSection` 补上该样式;并把三列与按钮间距从 10 收紧到 14(与 `AppTableRow.ActionRect` 的 12 对齐,列绝不贴边)。
   - 宽度实测(离屏,`USAGETRACKER_DPI` 96/120/144 三档):列起点 236 / 293 / 351,`scrollBounds` (0,56,·) / (0,70,·) / (0,84,·) —— 几何一直跟着宽度走,所以问题只在重绘。
4. **真实场景验证(意外收获)**:今天 17:11 机器关机(引擎被强杀,`last_clean_shutdown` 未更新),20:57 重启时新逻辑把 72 条遗留会话封口在 **上次心跳 17:11:13** —— 中间 3h45m 未被计入;21:08 再次重启封口在 21:07:15(偏差 54s,符合 ≤60s 设计)。**这正是用户最初报的“7h51m”类问题的真实复现与修复验证。**
   - 旁注:`exit_kind=4`(干净退出封口)今天未出现,因为两次都是我强杀 —— 只有 Windows 正常关机 / 托盘“退出”才走 `finally`;`exit_kind=3`(睡眠封口)也未出现,因为这台机器是关机而非睡眠,该路径已用挂起进程做过等价实测(§5.11)。

## 6. 最近一次改动与当前运行

- **2026-09-11 收尾**:打包可发布最简包(2.39 MB / zip 1.23 MB)、线上 release 瘦身(25→2.46 MB)、仓库清理(952.5→782.2 MB,清理后离线构建验证通过)。详见 §8。

- 2026-09-11 21:08:第三轮 —— 系统进程排除(含用户名单 `tracking-exclude.txt`)、修边命令智能模式(`hb_since` 心跳纪元)、列头 `ResizeRedraw` 修复;已发布并重启引擎,`integrity ok`。真实场景验证:17:11 关机→20:57 重启,遗留会话被封口在上次心跳(3h45m 未计入)。详见 §5.13。
- 2026-09-11 08:31:布局四项(页头遮挡/滚动条/卡片宽度/列几何)+ 黑名单过滤 + 干净退出封口 + 历史修边工具全部落地,**已发布到 `release\` 并重启引擎**。真实库已执行修边(剔除 242h54m 幻影时长),`integrity ok`;旧 release 与修边前数据库均已备份可回滚。详见 §5.12。
- 2026-09-11(第二轮):按用户拍板完成**数据层修复**(会话起点=观测起点 / 对账封口=上次心跳 / 睡眠跳变检测 / 跨天折叠),并在隔离数据目录实测四项验证(详见 §5.11);新增环境变量 `USAGETRACKER_SLEEP_JUMP_MS`、新增 meta 键 `last_seen`、新增 `exit_kind=3`(睡眠/断电封口)与 `exit_kind=4`(干净退出封口)。

- 2026-09-11:用户提供真机截图 `问题\15.png`~`19.png`(今日概览 / 全部应用概览 / 黑名单 / 设置 + 调整字号后的今日概览),并授权执行两项诊断:①`Cli recent 2000` 查 DB 原始会话行 → 排除“时间戳垃圾”,定位到跨午夜会话;②给引擎加落盘诊断(`UI\LayoutLog.cs` + `PageBase`/`ScrollArea`/`TodayPage` 三处埋点,`USAGETRACKER_DEBUGLAYOUT=1` 开关),跑 `--ui-shot` 实测出 §5.10 的六条结论。`release\` 未重新发布,运行实例不受影响。

- 2026-09 后续报告(仅登记,未改代码):滚动条不显示(§5.6)、黑名单应用仍出现在今日概览(§5.7)、今日后台数值仍异常(§5.8)。

- 最近改动:列宽下限 240px、列距≈10(缩小约20)、名称列≈160(+40)、开始记录独立成行、卡片行高≈96 基、名称/数值组垂直居中、卡片铺满右缘、列头几何与卡片一致、全部应用页改为今日口径。
- 已发布并重启,最新 pid 以 `Get-Process UsageTracker.Engine` 为准(写本文时为 27528);日志 `%LOCALAPPDATA%\UsageTracker\logs\engine.log`。

## 7. 交接后建议的第一步

在真机打开最新版,复核 §5.1/5.2 两处(空白栏位置、列头随卡片伸缩),并把“页面大标题 vs 首张卡片”的**全窗口截图(非窄条)** 与截图时间一起放入**本机** `问题\` 目录;新对话据此精修坐标/布局后再谈验收。任何视觉结论都要由用户真机给出,不要用离线渲染代替验收。

补充(2026-09 后续):§5.6–5.8 属功能性缺陷,可与视觉问题并行定位;其中 §5.7 已有明确修法(在 `UiDb.TodayTrue()` 里过滤 `is_ignored=1`),建议先做;§5.6 先加诊断日志再改;§5.8 需用户补具体数值后再动。

## 8. 交付包与仓库清理(2026-09-11 收尾)

- **可发布最简包**(本机 `dist\` 输出,未入库;已发布到 GitHub Releases):`UsageTracker-20260911-win-x64\`(**14 个文件 / 2.39 MB**)与同名 `.zip`(**1.23 MB**)。
  - 框架依赖发布(目标机需 .NET 8 Desktop Runtime);已剔除 `*.pdb` 与非 win-x64 的 22 个平台原生 sqlite(约省 23MB)。
  - 包内 `README.txt` 覆盖:运行方式、CLI 命令表、排除名单用法(`tracking-exclude.txt`)、数据口径、诊断开关与版本来源。
  - 已用**包内 CLI** 实测:可读真实库、`integrity ok`、含 `repair-spans` 命令 ✓。
- **线上 `release\` 同步瘦身**:25 MB → **2.46 MB**(只保留 `runtimes\win-x64`)。注意:重新 `dotnet publish` 会把其它平台的原生库再带回来,属正常现象,可手动删。
- **仓库清理**:952.5 MB → **782.2 MB**(省约 170MB)。删除项:`release_backup_20260911\`(验证通过后不再需要,源码可重建)、`shots\`(开发期离屏截图)、`.runtime-*` 六个测试数据目录、`src\*\bin|obj` 与 `M0Spike\bin|obj`(构建产物)、`.nuget-http-cache`、`.dotnet-cli-home`。
  - **清理后已验证仍可离线构建**:`dotnet build UsageTracker.sln -c Debug` → 0 警告 0 错误(自带 SDK + `.nuget-packages` 足够);`DOTNET_CLI_HOME`/`NUGET_HTTP_CACHE_PATH` 指向的目录不存在会自动重建。
  - 保留:`dotnet-sdk\`(713MB,构建工具链)、`.nuget-packages\`(49.5MB,离线还原必需)、`问题\`(真机截图证据)、`参考\`(设计参考图 1~5.png)、源码与数据目录(库 + 30 份滚动备份 + 修边前手工备份)。
- **`usage.cmd`** 已从 Debug CLI 改为指向 `release\UsageTracker.Cli.exe`(Debug 产物已清理),用法提示补上 `overview`/`report`/`repair-spans`。
- **入口速查**:开发/维护看 `readme.md`(§2 构建、§5 问题史、§6 变更、§8 交付);日常查询双击 `UsageTracker\usage.cmd`;交付给他人用 `dist\...zip`。

---

## 9. 仓库 / 发布 / 打包(2026-09-11)

- **源码仓库**:https://github.com/Omention312/Usagetracker (PUBLIC,`main`)
  - **仓库根 = `UsageTracker\`**;`.gitignore` 排除 `bin/ obj/ release/ .nuget-packages/` 与本机开发期离屏截图(`shot*.png`、`run*.png` 等)
  - 提交身份用 GitHub noreply 邮箱(`Omention312@users.noreply.github.com`),不暴露真实邮箱;要改:`git config user.email <你的邮箱>`
  - 推送凭据由 GitHub CLI 提供(`gh auth setup-git`,已配置 URL 级 helper)。若报 `Invalid username or token`,是 Windows 凭据管理器里旧账号凭据抢先 —— 用 `credential.https://github.com.helper` 覆盖即可(已处理)
- **Release**:https://github.com/Omention312/Usagetracker/releases/tag/v1.0.0
  - 资产 `UsageTracker-v1.0.0-win-x64.zip`(1.23 MB;框架依赖,已剔除 pdb 与非 win-x64 原生库)
- **GitHub Packages(NuGet)**:包已构建(`dist\nupkg\UsageTracker.Core.1.0.0.nupkg`),但推送被 **403** 拒绝 ——
  当前 gh token 的 scopes 仅 `gist / read:org / repo / workflow`,**缺 `write:packages`**。
  修法(二选一):① `gh auth refresh -h github.com -s write:packages` 后重推;
  ② 用带 `write:packages` 的 PAT:`dotnet nuget push dist\nupkg\*.nupkg --source https://nuget.pkg.github.com/Omention312/index.json --api-key <PAT>`
- **入库前做过的清理**:所有绝对路径(`D:\Program Files\dsh\...`)改为相对/泛化;`usage.cmd` 用 `%~dp0`、`verify\*.ps1` 用 `$PSScriptRoot`、`tools\IconGen` 用相对参数;各 csproj 的 `RestorePackagesPath` 上收到仓库根 `Directory.Build.props`(仅当克隆的上一级存在 `.nuget-packages\` 时才启用,否则走系统全局包目录)。
- **未入库的本机专属目录**:`dotnet-sdk\`(713MB)、`.nuget-packages\`(49.5MB)、`release\`、`dist\`、`问题\`(真机截图)、`参考\`(设计参考图)。
- **CI**:`.github/workflows/build.yml`(windows-latest 上 `dotnet build UsageTracker.sln -c Release`)。
- **许可证**:MIT(LICENSE,© 2026 Omention312);UsageTracker.Core 已带 NuGet 包元数据(License/Authors/RepositoryUrl/README)。