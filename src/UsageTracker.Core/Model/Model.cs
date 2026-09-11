namespace UsageTracker.Core.Model;

/// <summary>一条已结束的进程运行会话(写入时使用)。</summary>
public sealed class SessionCloseInfo
{
    public long Pid;
    public long PidBornMs;          // 进程创建时刻(epoch ms)
    public string AppName = "";
    public uint SessionId;
    public long ParentPid;
    public long StartedAtMs;        // epoch ms
    public long EndedAtMs;          // epoch ms
    public int ExitKind;            // 0 未知 1 正常 2 异常/被终止
}

/// <summary>一条前台片段(写入时使用;domain 可为 null)。</summary>
public sealed class FgSegmentInfo
{
    public long TsFromMs;
    public long TsToMs;
    public long? Pid;
    public long? PidBornMs;
    public string AppName = "";
    public string? Domain;
}

public static class TimeUtil
{
    public static long UtcNowMs() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    /// <summary>本地“日”键 yyyy-MM-dd。</summary>
    public static string LocalDayKey(long epochMsUtc)
    {
        try { return DateTimeOffset.FromUnixTimeMilliseconds(epochMsUtc).ToLocalTime().ToString("yyyy-MM-dd"); }
        catch { return "1970-01-01"; }
    }
}
