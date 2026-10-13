using System.Collections.Concurrent;
using System.IO;

namespace TrikiMusicController;

public static class TrikiLogger
{
    private static readonly string LogFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "TrikiMusicController",
        "triki_debug.log");

    private static readonly BlockingCollection<string> LogQueue = new();
    private static readonly Thread? WriterThread;

    static TrikiLogger()
    {
        try
        {
            var dir = Path.GetDirectoryName(LogFilePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            // Start background writer thread
            WriterThread = new Thread(ProcessLogQueue)
            {
                IsBackground = true,
                Name = "TrikiLoggerThread"
            };
            WriterThread.Start();

            Log("=== Triki Music Controller Запущен ===");
        }
        catch { }
    }

    public static string FilePath => LogFilePath;

    public static void Log(string message)
    {
        var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}";
        LogQueue.TryAdd(line);
    }

    public static void LogMetric(string category, double ax, double ay, double az, double gx, double gy, double gz, double mag, string details = "")
    {
        var line = $"[{DateTime.Now:HH:mm:ss.fff}] [{category}] Acc=({ax:F2}, {ay:F2}, {az:F2}) |A|={mag:F2}G | Gyro=({gx:F0}, {gy:F0}, {gz:F0}) {details}";
        LogQueue.TryAdd(line);
    }

    private static void ProcessLogQueue()
    {
        try
        {
            using var stream = new FileStream(LogFilePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
            using var writer = new StreamWriter(stream) { AutoFlush = true };

            foreach (var item in LogQueue.GetConsumingEnumerable())
            {
                writer.WriteLine(item);
            }
        }
        catch { }
    }
}
