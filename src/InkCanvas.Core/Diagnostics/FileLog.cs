using System.Text.Json;

namespace InkCanvas.Core.Diagnostics;

public enum LogType { Info, Event, Trace, Error }

/// <summary>
/// 1:1 port of <c>Helpers/LogHelper.cs</c>. Same file name, same line format
/// (<c>{ISO-8601} [{Type}] {message}</c>), same append-and-swallow behaviour.
/// <para>
/// The original wrote next to the executable (<c>App.RootPath</c>); this port defaults to the
/// per-user app-data folder so it works from a read-only install directory.
/// </para>
/// <para>
/// <b>未验证</b>：按要求跳过行为验证（无单元测试、无实机验证）。
/// </para>
/// </summary>
public static class FileLog
{
    public static string LogFile { get; set; } = "Log.txt";

    /// <summary>Directory holding the log. Defaults to %APPDATA%\InkCanvas.</summary>
    public static string RootPath { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "InkCanvas");

    /// <summary>Master switch, driven by <c>Advanced.IsLogEnabled</c>.</summary>
    public static bool Enabled { get; set; } = true;

    public static void NewLog(string message) => WriteLogToFile(message, LogType.Info);

    /// <summary>The original's NewLog(Exception) was an empty method; kept as a no-op.</summary>
    public static void NewLog(Exception exception) { }

    public static void WriteLogToFile(string message, LogType logType = LogType.Info)
    {
        if (!Enabled) return;
        try
        {
            if (!Directory.Exists(RootPath)) Directory.CreateDirectory(RootPath);
            string line = $"{DateTime.Now:O} [{logType}] {message}";
            File.AppendAllText(Path.Combine(RootPath, LogFile), line + Environment.NewLine);
        }
        catch
        {
            // 与原实现一致：日志失败绝不影响主流程。
        }
    }

    public static void WriteObjectLogToFile(object value, LogType logType = LogType.Info)
    {
        if (!Enabled) return;
        try
        {
            WriteLogToFile(JsonSerializer.Serialize(value), logType);
        }
        catch
        {
            // 序列化失败同样忽略。
        }
    }
}