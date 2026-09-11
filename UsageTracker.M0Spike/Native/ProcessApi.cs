using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace UsageTracker.Spike.Native;

/// <summary>user32 / ntdll 少量 P/Invoke(传统 DllImport,兼容 StringBuilder)。</summary>
internal static class NativeMethods
{
    // ---------- user32 ----------
    [DllImport("user32.dll")]
    internal static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    internal static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int maxCount);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetForegroundWindow(IntPtr hWnd);

    // ---------- ntdll ----------
    // NTSTATUS: STATUS_SUCCESS=0, STATUS_INFO_LENGTH_MISMATCH=0xC0000004
    internal const int StatusInfoLengthMismatch = unchecked((int)0xC0000004);
    internal const int SystemProcessInformation = 5;

    [DllImport("ntdll.dll")]
    internal static extern int NtQuerySystemInformation(int informationClass, IntPtr buffer, int length, out int returnLength);
}

/// <summary>UNICODE_STRING(64 位:USHORT Length + USHORT MaximumLength + 对齐 + PVOID Buffer = 16 字节)。</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct UnicodeString
{
    public ushort Length;
    public ushort MaximumLength;
    public IntPtr Buffer;
}

/// <summary>
/// SYSTEM_PROCESS_INFORMATION 头部(仅取方案所需字段;x64 布局)。
/// 顺序:NextEntryOffset(0) NumberOfThreads(4) WorkingSetPrivateSize(8,8B)
/// HardFaultCount(16) NumberOfThreadsHighWatermark(20) CycleTime(24,8B)
/// CreateTime(32,8B) UserTime(40) KernelTime(48) ImageName(56,16B)
/// BasePriority(72) pad UniqueProcessId(80,8B) InheritedFrom(88,8B)
/// HandleCount(96) SessionId(100) UniqueProcessKey(104,8B)
/// </summary>
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

/// <summary>快照行(解析后)。CreateTime 为 FILETIME(100ns),转 DateTime 用 FromFileTimeUtc。</summary>
internal sealed class ProcessRow
{
    public long Pid;
    public long CreateTime;      // FILETIME ticks
    public string Name = "";     // 映像名(尽力含路径)
    public long ParentPid;
    public uint SessionId;
    public (long Pid, long Ct) Key => (Pid, CreateTime);
    public override string ToString() => $"{Name} (pid={Pid})";
}

internal static class ProcessSnapshot
{
    /// <summary>调用 NtQuerySystemInformation 抓全系统进程快照(无需管理员)。</summary>
    public static List<ProcessRow> Query()
    {
        int size = 4 << 20; // 4MB 起步,进程多时自动放大
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
                    CreateTime = native.CreateTime,
                    ParentPid = native.InheritedFromUniqueProcessId.ToInt64(),
                    SessionId = native.SessionId,
                };
                if (native.ImageName.Buffer != IntPtr.Zero && native.ImageName.Length > 0)
                {
                    int n = native.ImageName.Length;
                    byte[] raw = new byte[n];
                    Marshal.Copy(native.ImageName.Buffer, raw, 0, n);
                    row.Name = Encoding.Unicode.GetString(raw);
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

    /// <summary>显示名兜底:由快照里的映像名取文件名,失败时退回 .NET Process 查询。</summary>
    public static string ResolveDisplayName(long pid, string? snapshotName)
    {
        string? n = snapshotName;
        if (string.IsNullOrWhiteSpace(n))
        {
            try
            {
                using var p = System.Diagnostics.Process.GetProcessById((int)pid);
                n = p.ProcessName + ".exe";
            }
            catch { n = null; }
        }
        n ??= $"(pid {pid})";
        var file = Path.GetFileName(n.TrimEnd('\0'));
        return file.Length > 0 ? file : n;
    }
}
