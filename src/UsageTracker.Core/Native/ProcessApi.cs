using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace UsageTracker.Core.Native;

/// <summary>user32 / ntdll 少量 P/Invoke。</summary>
public static class NativeMethods
{
    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int maxCount);

    // NTSTATUS: 0=OK, 0xC0000004=缓冲区不够
    public const int StatusInfoLengthMismatch = unchecked((int)0xC0000004);
    public const int SystemProcessInformation = 5;

    [DllImport("ntdll.dll")]
    public static extern int NtQuerySystemInformation(int informationClass, IntPtr buffer, int length, out int returnLength);
}

[StructLayout(LayoutKind.Sequential)]
internal struct UnicodeString
{
    public ushort Length;
    public ushort MaximumLength;
    public IntPtr Buffer;
}

/// <summary>SYSTEM_PROCESS_INFORMATION 头部(x64 布局,见 M0 spike 注释)。</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct SystemProcessInfoNative
{
    public uint NextEntryOffset;
    public uint NumberOfThreads;
    public long WorkingSetPrivateSize;
    public uint HardFaultCount;
    public uint NumberOfThreadsHighWatermark;
    public long CycleTime;
    public long CreateTime;
    public long UserTime;
    public long KernelTime;
    public UnicodeString ImageName;
    public int BasePriority;
    public IntPtr UniqueProcessId;
    public IntPtr InheritedFromUniqueProcessId;
    public uint HandleCount;
    public uint SessionId;
    public IntPtr UniqueProcessKey;
}

/// <summary>一次进程快照的一行。</summary>
public sealed class ProcessRow
{
    public long Pid;
    public long CreateTimeFileTime;   // FILETIME(100ns)
    public string ImagePath = "";     // 映像名/路径
    public long ParentPid;
    public uint SessionId;

    public string FileName => Path.GetFileName(ImagePath.TrimEnd('\0'));
    public (long Pid, long Ct) Key => (Pid, CreateTimeFileTime);
    public DateTime CreatedUtc
    {
        get { try { return DateTime.FromFileTimeUtc(CreateTimeFileTime); } catch { return DateTime.MinValue; } }
    }
    public long CreatedMsUtc
    {
        get { try { return new DateTimeOffset(CreatedUtc).ToUnixTimeMilliseconds(); } catch { return 0; } }
    }
}

/// <summary>抓取全系统进程快照(无需管理员)。</summary>
public static class ProcessSnapshot
{
    public static List<ProcessRow> Query()
    {
        int size = 4 << 20;
        IntPtr buf = IntPtr.Zero;
        try
        {
            while (true)
            {
                buf = Marshal.AllocHGlobal(size);
                int needed = 0;
                int status = NativeMethods.NtQuerySystemInformation(NativeMethods.SystemProcessInformation, buf, size, out needed);
                if (status == 0) break;
                if (status == NativeMethods.StatusInfoLengthMismatch)
                {
                    Marshal.FreeHGlobal(buf);
                    buf = IntPtr.Zero;
                    size = Math.Max(size * 2, needed + (1 << 20));
                    continue;
                }
                throw new InvalidOperationException($"NtQuerySystemInformation failed, status=0x{status:X8}");
            }

            var result = new List<ProcessRow>(1024);
            long baseAddr = buf.ToInt64();
            long offset = 0;
            while (true)
            {
                var native = Marshal.PtrToStructure<SystemProcessInfoNative>(new IntPtr(baseAddr + offset));
                var row = new ProcessRow
                {
                    Pid = native.UniqueProcessId.ToInt64(),
                    CreateTimeFileTime = native.CreateTime,
                    ParentPid = native.InheritedFromUniqueProcessId.ToInt64(),
                    SessionId = native.SessionId,
                };
                if (native.ImageName.Buffer != IntPtr.Zero && native.ImageName.Length > 0)
                {
                    byte[] raw = new byte[native.ImageName.Length];
                    Marshal.Copy(native.ImageName.Buffer, raw, 0, raw.Length);
                    row.ImagePath = Encoding.Unicode.GetString(raw);
                }
                result.Add(row);
                if (native.NextEntryOffset == 0) break;
                offset += native.NextEntryOffset;
            }
            return result;
        }
        finally
        {
            if (buf != IntPtr.Zero) Marshal.FreeHGlobal(buf);
        }
    }
}

/// <summary>进程显示名与跟踪策略(系统进程/辅助进程噪声过滤)。</summary>
public static class TrackingPolicy
{
    // 永不入库/永不显示的系统或辅助进程(子进程树规则在 M2 细化)
    private static readonly HashSet<string> ExcludedExe = new(StringComparer.OrdinalIgnoreCase)
    {
        "System", "Registry", "Memory Compression", "Secure System", "MemCompression",
        "svchost.exe", "csrss.exe", "wininit.exe", "services.exe", "lsass.exe",
        "fontdrvhost.exe", "dwm.exe", "conhost.exe", "dllhost.exe", "sihost.exe",
        "taskhostw.exe", "ctfmon.exe", "SearchHost.exe", "SearchProtocolHost.exe",
        "SearchFilterHost.exe", "RuntimeBroker.exe", "ShellExperienceHost.exe",
        "StartMenuExperienceHost.exe", "Widgets.exe", "WidgetService.exe",
        "TextInputHost.exe", "backgroundTaskHost.exe", "ApplicationFrameHost.exe",
        "prevhost.exe", "smartscreen.exe", "unsecapp.exe", "GameBarPresenceWriter.exe",
        "msedgewebview2.exe", "WebView2", "extension-host.exe", "crashpad_handler.exe",
        "steamwebhelper.exe", "UnityCrashHandler64.exe", "WerFault.exe",
        "LogonUI.exe", "lockapp.exe", "consent.exe", "ShellExperienceHost.exe",
        // Windows 自身的会话/外壳基础设施(2026-09-11 追加:用户要求系统进程不入库 —— winlogon 曾被当成“应用”记了 8h+)
        "winlogon.exe", "userinit.exe", "smss.exe", "taskhost.exe", "audiodg.exe",
        "MHostProcess.exe", "ShellHost.exe", "rdpclip.exe", "WUDFHost.exe",
        "SecurityHealthService.exe", "SecurityHealthSystray.exe",
        "MoUsoCoreWorker.exe", "usocoreworker.exe", "MusNotification.exe", "MusNotificationUx.exe",
        "rundll32.exe",
    };

    /// <summary>用户自定义排除名单(数据目录 tracking-exclude.txt,每行一个 exe 名,# 开头为注释)。</summary>
    private static readonly HashSet<string> UserExcluded = new(StringComparer.OrdinalIgnoreCase);

    public static int UserExcludedCount => UserExcluded.Count;

    /// <summary>加载用户排除名单(文件不存在则忽略)。返回加载到的条数。</summary>
    public static int LoadUserExcluded(string path)
    {
        UserExcluded.Clear();
        try
        {
            if (!File.Exists(path)) return 0;
            foreach (var raw in File.ReadAllLines(path))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith('#')) continue;
                UserExcluded.Add(line);
            }
        }
        catch { }
        return UserExcluded.Count;
    }

    public static string DisplayName(string exeFileName)
    {
        string f = exeFileName.TrimEnd('\0');
        string lower = f.ToLowerInvariant();
        return lower switch
        {
            "msedge.exe" => "Microsoft Edge",
            "explorer.exe" => "文件资源管理器",
            "notepad.exe" => "记事本",
            "code.exe" => "Code",
            "chrome.exe" => "Chrome",
            _ => Path.GetFileNameWithoutExtension(f),
        };
    }

    /// <summary>该进程是否应记录为“运行会话”(普通权限下交互会话、非系统噪声)。按 PID 排除引擎自身。</summary>
    public static bool ShouldTrack(ProcessRow r, long selfPid)
    {
        if (r.SessionId == 0) return false;
        if (r.Pid <= 4 || r.Pid == selfPid) return false;
        string f = r.FileName;
        if (f.Length == 0) return false;
        return !ExcludedExe.Contains(f) && !UserExcluded.Contains(f);
    }
}
