using System.IO;

namespace UsageTracker.Engine;

/// <summary>
/// 维护任务(引擎单写者线程内执行,每日一次):
///  1. 自动备份:VACUUM INTO 一致性副本 → backups\usage-YYYYMMDD-HHmmss.db,保留最近 30 份;
///  2. 90 天明细滚动清理 + 清理量大时 VACUUM。
/// </summary>
internal static class Maintenance
{
    public const int KeepBackups = 30;
    public const int RetentionDays = 90;

    /// <summary>执行一次维护;任何单步失败只记日志不中断。返回是否执行了备份。</summary>
    public static bool RunOnce(Core.Data.UsageDb db, string dataDir, EngineLog log)
    {
        bool backedUp = false;
        try
        {
            string integrity = db.Integrity();
            if (integrity != "ok")
            {
                log.Info($"[维护] 主库 integrity_check={integrity},跳过备份并告警");
                return false;
            }

            var backupsDir = Path.Combine(dataDir, "backups");
            Directory.CreateDirectory(backupsDir);
            string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            string target = Path.Combine(backupsDir, $"usage-{stamp}.db");
            db.VacuumInto(target);
            backedUp = true;
            log.Info($"[维护] 备份完成: {target}");

            // 滚动保留:删除超量旧备份
            var files = Directory.GetFiles(backupsDir, "usage-*.db")
                .OrderByDescending(f => f, StringComparer.OrdinalIgnoreCase).ToList();
            int remove = files.Count - KeepBackups;
            for (int i = 0; i < remove; i++)
            {
                try { File.Delete(files[^1]); files.RemoveAt(files.Count - 1); }
                catch (Exception ex) { log.Info($"[维护] 删除旧备份失败: {ex.Message}"); }
            }
            if (remove > 0) log.Info($"[维护] 滚动清理 {remove} 份旧备份(保留 {KeepBackups})");
        }
        catch (Exception ex)
        {
            log.Info($"[维护] 备份失败: {ex.Message}");
        }

        try
        {
            long cutoff = DateTimeOffset.UtcNow.AddDays(-RetentionDays).ToUnixTimeMilliseconds();
            int delSessions = db.DeleteOldSessions(cutoff);
            int delSegments = db.DeleteOldSegments(cutoff);
            log.Info($"[维护] 滚动清理明细:会话 {delSessions} 行、片段 {delSegments} 行(>{RetentionDays} 天)");
            if (delSessions + delSegments > 5000) db.Vacuum();
        }
        catch (Exception ex)
        {
            log.Info($"[维护] 明细清理失败: {ex.Message}");
        }
        return backedUp;
    }
}
