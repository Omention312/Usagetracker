using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Automation;
using UsageTracker.Spike.Native;

namespace UsageTracker.Spike.Probes;

/// <summary>
/// 探针②:Edge 地址栏 UIA 读取。要回答的问题:
/// 「Edge 在前台、但焦点不在地址栏(在页面里)时,能否用 UIA 读到当前标签 URL」。
/// 读取方式:前台窗口属主为 msedge 时,在窗口树里找地址栏 Edit(名称含 地址/Address/搜索/search),
/// 取 ValuePattern.Value。本 spike 把候选都打印出来,便于确认控件特征。
/// </summary>
internal static class EdgeUiaProbe
{
    public static int RunDump(string[] args)
    {
        Console.WriteLine("[probe-edge-dump] 查找 Edge 窗口…");
        var windows = FindEdgeWindows();
        if (windows.Count == 0)
        {
            string url = args.Length > 1 ? args[1] : "https://space.bilibili.com";
            Console.WriteLine($"[probe-edge-dump] 未发现 Edge 窗口,自动启动 Edge 打开: {url}");
            try
            {
                Process.Start(new ProcessStartInfo("msedge.exe", url) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[probe-edge-dump] Edge 启动失败: {ex.Message}");
            }
            for (int i = 0; i < 30 && windows.Count == 0; i++)
            {
                Thread.Sleep(500);
                windows = FindEdgeWindows();
            }
        }

        if (windows.Count == 0)
        {
            Console.WriteLine("[probe-edge-dump] FAIL: 等不到 Edge 窗口(请确认已安装 Edge 并登录会话)");
            return 1;
        }

        bool anyUrl = false;
        int idx = 0;
        foreach (var (hwnd, title) in windows)
        {
            idx++;
            Console.WriteLine($"\n--- Edge 窗口 #{idx} hwnd=0x{hwnd.ToInt64():X} 标题=\"{title}\" ---");
            var root = AutomationElement.FromHandle(hwnd);
            var found = FindAddressBarCandidates(root);
            if (found.Count == 0)
            {
                Console.WriteLine("  未找到 Edit 候选(可能窗口尚未就绪或 UIA 树受限)");
            }
            foreach (var c in found)
            {
                Console.WriteLine($"  Edit: Name=\"{c.Name}\" Aid=\"{c.Aid}\" Focused={c.Focused} Value=\"{c.Value}\"");
                if (!string.IsNullOrEmpty(c.Value) && LooksLikeWebUrl(c.Value))
                {
                    anyUrl = true;
                    Console.WriteLine($"  >> 命中 URL: {c.Value}  → 规范化 host: {NormalizeHost(c.Value)}");
                }
            }
        }

        Console.WriteLine();
        Console.WriteLine(anyUrl
            ? "[probe-edge-dump] PASS: 成功读到页面 URL(地址栏未聚焦场景已覆盖:新开页默认焦点在页面)"
            : "[probe-edge-dump] FAIL: 未读到任何 http(s) URL");
        return anyUrl ? 0 : 1;
    }

    public static int RunWatch(string[] args)
    {
        int seconds = args.Length > 1 && int.TryParse(args[1], out var s) ? s : 40;
        Console.WriteLine($"[probe-edge-watch] 每 1s 观测前台 {seconds}s。若前台是 Edge 就尝试读地址栏 URL。");
        Console.WriteLine("提示:请在运行期间把 Edge 切到前台,焦点留在页面内(别点地址栏),再切走一次,观察差异。");

        bool seenForegroundUrl = false;
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed.TotalSeconds < seconds)
        {
            IntPtr fg = NativeMethods.GetForegroundWindow();
            uint pid = 0;
            NativeMethods.GetWindowThreadProcessId(fg, out pid);
            string name = "";
            if (pid != 0)
            {
                try { using var p = Process.GetProcessById((int)pid); name = p.ProcessName; }
                catch { name = "?"; }
            }
            var sb = new StringBuilder(512);
            string title = (fg != IntPtr.Zero && NativeMethods.GetWindowText(fg, sb, sb.Capacity) > 0)
                ? sb.ToString() : "";

            string tag = pid == 0 ? "无前台(锁屏?)" : $"{name} (pid={pid})";
            if (name.Equals("msedge", StringComparison.OrdinalIgnoreCase))
            {
                var root = AutomationElement.FromHandle(fg);
                var cands = FindAddressBarCandidates(root);
                string url = cands.Select(c => c.Value).FirstOrDefault(v => LooksLikeWebUrl(v)) ?? "";
                if (url.Length > 0) seenForegroundUrl = true;
                Console.WriteLine($"[{sw.Elapsed.TotalSeconds,5:0}s] 前台={tag} 标题=\"{Trunc(title, 40)}\" → {(url.Length > 0 ? "URL=" + url : "未读到URL")}");
            }
            else
            {
                Console.WriteLine($"[{sw.Elapsed.TotalSeconds,5:0}s] 前台={tag} (非 Edge,跳过)");
            }
            Thread.Sleep(1000);
        }

        Console.WriteLine();
        Console.WriteLine(seenForegroundUrl
            ? "[probe-edge-watch] PASS: 在 Edge 前台期间至少一次成功读到 URL"
            : "[probe-edge-watch] FAIL: 期间未读到任何 URL");
        return seenForegroundUrl ? 0 : 1;
    }

    // ---------------- 内部实现 ----------------

    private static List<(IntPtr Hwnd, string Title)> FindEdgeWindows()
    {
        var list = new List<(IntPtr, string)>();
        Process[] procs;
        try { procs = Process.GetProcessesByName("msedge"); }
        catch { return list; }
        var seen = new HashSet<long>();
        foreach (var p in procs)
        {
            try
            {
                p.Refresh();
                IntPtr h = p.MainWindowHandle;
                if (h != IntPtr.Zero && seen.Add(h.ToInt64()))
                {
                    var sb = new StringBuilder(512);
                    string t = NativeMethods.GetWindowText(h, sb, sb.Capacity) > 0 ? sb.ToString() : "";
                    list.Add((h, t));
                }
            }
            catch { /* 个别进程句柄受限,跳过 */ }
            finally { p.Dispose(); }
        }
        return list;
    }

    private sealed class EditCandidate
    {
        public string Name = "";
        public string Aid = "";
        public string Value = "";
        public bool Focused;
    }

    private static List<EditCandidate> FindAddressBarCandidates(AutomationElement root)
    {
        var result = new List<EditCandidate>();
        if (root == null) return result;
        try
        {
            var editCond = new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit);
            var edits = root.FindAll(TreeScope.Descendants, editCond);
            foreach (AutomationElement e in edits)
            {
                string name = Safe(() => e.Current.Name ?? "");
                string aid = Safe(() => e.Current.AutomationId ?? "");
                bool focused;
                try { focused = e.Current.HasKeyboardFocus; } catch { focused = false; }
                string value = "";
                try
                {
                    if (e.TryGetCurrentPattern(ValuePattern.Pattern, out object? pat) && pat is ValuePattern vp)
                        value = vp.Current.Value ?? "";
                }
                catch { }
                // 只保留与地址/搜索相关的 Edit,避免列出页面内所有输入框
                if (name.Contains("地址", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("搜索", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("Address", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("Search", StringComparison.OrdinalIgnoreCase)
                    || LooksLikeWebUrl(value)
                    || value.StartsWith("edge://", StringComparison.OrdinalIgnoreCase))
                {
                    result.Add(new EditCandidate { Name = name, Aid = aid, Value = value, Focused = focused });
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  (UIA 遍历异常: {ex.Message})");
        }
        return result;
    }

    private static string Safe(Func<object?> f)
    {
        try { return f()?.ToString() ?? ""; }
        catch { return ""; }
    }

    private static bool LooksLikeWebUrl(string v)
        => Uri.TryCreate(v, UriKind.Absolute, out var u)
           && (u.Scheme == Uri.UriSchemeHttp || u.Scheme == Uri.UriSchemeHttps);

    /// <summary>取 host 的规范化「www.主域」形态。spike 用简化折叠:bilibili.com 及全部子域 → www.bilibili.com;其余 → www.<host 最末两段>。完整规则在 M1 实现 eTLD+1 折叠。</summary>
    public static string NormalizeHost(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var u)) return "(无法解析)";
        string host = u.Host.ToLowerInvariant();
        if (host == "localhost") return "localhost";
        if (host.EndsWith(".bilibili.com", StringComparison.Ordinal) || host == "bilibili.com")
            return "www.bilibili.com";
        // 简化的主域折叠(不处理公共后缀列表;M1 用正式 eTLD+1)
        var parts = host.Split('.');
        string main = parts.Length >= 2 ? string.Join('.', parts, parts.Length - 2, 2) : host;
        return "www." + main;
    }

    private static string Trunc(string s, int n) => s.Length <= n ? s : s[..n] + "…";
}
