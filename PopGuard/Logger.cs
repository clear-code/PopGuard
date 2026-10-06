using System.Diagnostics;

namespace PopGuard;

/// <summary>
/// Writes log lines to a file. Opens and closes the file for each write, and rotates by
/// generations once it exceeds the size limit. Serializes concurrent writes from multiple
/// processes/threads with a named mutex.
/// (Modeled on BrowserGuard's Logger.cs.)
/// </summary>
internal sealed class Logger
{
    private const int MaxGeneration = 10;

    private const long DefaultMaxLogSize = 10 * 1024 * 1024;

    private const string LogFileNameBase = "PopGuard";

    // The file is only open during a write, so guard rotation with a mutex so it does not happen
    // in the middle of another writer (handles multiple instances on the same machine).
    private static readonly Mutex FileMutex = new(false, @"Local\PopGuard.Logger");

    private static readonly TimeSpan MutexTimeout = TimeSpan.FromSeconds(5);

    private readonly long _maxLogSize;

    public string FilePath { get; } = "";

    private string LogDirectory { get; } = "";

    private bool EnableLogging { get; }

    // The default logger shared across the app. A static entry point.
    private static readonly Logger Shared = new();

    public static void Line(string message) => Shared.Log(message);

    public static void Line(Exception e) => Shared.Log(e);

    public void Log(string message) => NoException(() => LogImpl(message));

    public void Log(Exception e) => NoException(() => LogImpl(e));

    public Logger() : this(DefaultDirectory()) { }

    // Directory and size are arguments so rotation can be exercised without writing to the real log.
    public Logger(string directory, long maxLogSize = DefaultMaxLogSize)
    {
        _maxLogSize = maxLogSize;
        EnableLogging = false;
        try
        {
            Directory.CreateDirectory(directory);
            LogDirectory = directory;
            FilePath = Path.Combine(directory, $"{LogFileNameBase}.log");
            EnableLogging = true;
        }
        catch
        {
            // Cannot write logs, but keep the app running.
        }
    }

    private static string DefaultDirectory() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PopGuard");

    private static void NoException(Action func)
    {
        try { func(); } catch { }
    }

    private void LogImpl(string message)
    {
        string line = $"{GetTimestamp()} : {message}";
        Debug.WriteLine(line);

        if (!EnableLogging)
        {
            return;
        }
        Write(line);
    }

    private void LogImpl(Exception e)
    {
        if (!EnableLogging)
        {
            return;
        }
        LogImpl(e.ToString());
    }

    private void Write(string line)
    {
        var held = false;
        try
        {
            try
            {
                held = FileMutex.WaitOne(MutexTimeout);
            }
            catch (AbandonedMutexException)
            {
                // A process that held it died. The file itself is fine.
                held = true;
            }

            RotateIfNeeded();
            // Opening the file shared lets other instances keep writing too.
            using var stream = new FileStream(
                FilePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
            using var writer = new StreamWriter(stream);
            writer.WriteLine(line);
        }
        finally
        {
            if (held)
            {
                FileMutex.ReleaseMutex();
            }
        }
    }

    private static string GetTimestamp()
    {
        return DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
    }

    private void RotateIfNeeded()
    {
        var info = new FileInfo(FilePath);
        if (info.Exists && info.Length > _maxLogSize)
        {
            Rotate();
        }
    }

    // Generation files live next to the log. Looking elsewhere would find nothing to move
    // and would end up just truncating the log.
    private void Rotate()
    {
        var oldest = GenerationPath(MaxGeneration);
        if (File.Exists(oldest))
        {
            File.Delete(oldest);
        }

        for (var i = MaxGeneration - 1; i >= 0; i--)
        {
            var from = GenerationPath(i);
            if (!File.Exists(from))
            {
                continue;
            }
            File.Move(from, GenerationPath(i + 1), true);
        }
    }

    private string GenerationPath(int generation) =>
        Path.Combine(LogDirectory, generation == 0
            ? $"{LogFileNameBase}.log"
            : $"{LogFileNameBase}_{generation}.log");
}
