using System.Diagnostics;
using System.Windows.Automation;

namespace UsageTracker.Core.Edge;

/// <summary>
/// Edge 地址栏 UIA 读取(M0 已验证:控件名“地址和搜索栏”/AutomationId view_*,未聚焦也可读 Value)。
/// 只读 URL,不落库完整 URL,调用方负责规范化。
/// </summary>
public static class EdgeUrlReader
{
    /// <summary>给定 Edge 顶层窗口句柄,读当前活动标签 URL;失败返回 null。</summary>
    public static string? TryReadUrl(IntPtr hwnd)
    {
        try
        {
            var root = AutomationElement.FromHandle(hwnd);
            if (root == null) return null;

            var editCond = new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit);
            var edits = root.FindAll(TreeScope.Descendants, editCond);
            foreach (AutomationElement e in edits)
            {
                string name = "";
                string aid = "";
                string value = "";
                try { name = e.Current.Name ?? ""; } catch { }
                try { aid = e.Current.AutomationId ?? ""; } catch { }
                try
                {
                    if (e.TryGetCurrentPattern(ValuePattern.Pattern, out object? pat) && pat is ValuePattern vp)
                        value = vp.Current.Value ?? "";
                }
                catch { }
                bool looksBar = name.Contains("地址", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("Address", StringComparison.OrdinalIgnoreCase)
                    || aid.StartsWith("view_", StringComparison.OrdinalIgnoreCase);
                if (!looksBar) continue;
                if (IsWebUrl(value) || value.StartsWith("edge://", StringComparison.OrdinalIgnoreCase))
                    return value;
            }
            return null;
        }
        catch
        {
            return null; // 任何 UIA 异常都视为不可读
        }
    }

    private static bool IsWebUrl(string v)
        => Uri.TryCreate(v, UriKind.Absolute, out var u)
           && (u.Scheme == Uri.UriSchemeHttp || u.Scheme == Uri.UriSchemeHttps);

    /// <summary>判断某顶层窗口句柄是否属于 Edge(用于确认可读)。</summary>
    public static bool IsEdgeProcessName(string processNameOrExe)
        => processNameOrExe.Equals("msedge", StringComparison.OrdinalIgnoreCase)
           || processNameOrExe.Equals("msedge.exe", StringComparison.OrdinalIgnoreCase);
}
