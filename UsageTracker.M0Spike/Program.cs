using System.Text;
using UsageTracker.Spike.Probes;

namespace UsageTracker.Spike;

/// <summary>M0 spike 入口。CLI 分派到各个探针。</summary>
internal static class Program
{
    private const string Help = """
        UsageTracker.M0Spike — M0 可行性验证(全部普通权限,无管理员)

        用法:
          probe-process [秒数]            ① 进程快照差分:观察 启动/退出 事件(默认 45s,自动开/关子进程验证)
          probe-edge-dump [url]          ② Edge UIA:列出每个 Edge 窗口地址栏候选与 URL(可传 url 自动开 Edge)
          probe-edge-watch [秒数]        ② Edge UIA:持续观测(默认 40s,需 Edge 在前台)
          probe-db [db路径]              ③ SQLite 骨架自检:建表/写入/查询/integrity_check
          probe-tray [秒数]              ③ 托盘 NotifyIcon 冒烟(默认 25s 后自动退出)
          child <毫秒>                   辅助子进程(供 probe-process 观测启停)
        """;

    [STAThread]
    private static int Main(string[] args)
    {
        try { Console.OutputEncoding = Encoding.UTF8; }
        catch { /* 重定向时编码可能受限,忽略 */ }

        var mode = args.Length > 0 ? args[0].ToLowerInvariant() : "help";
        return mode switch
        {
            "probe-process"   => ProcessLifecycleProbe.Run(args),
            "probe-edge-dump" => EdgeUiaProbe.RunDump(args),
            "probe-edge-watch"=> EdgeUiaProbe.RunWatch(args),
            "probe-db"        => StoreProbe.Run(args),
            "probe-tray"      => TrayProbe.Run(args),
            "child"           => ChildProbe.Run(args),
            "help" or "-h" or "--help" => Print(Help, 0),
            _ => Print(Help, 2),
        };
    }

    private static int Print(string s, int code) { Console.WriteLine(s); return code; }
}
