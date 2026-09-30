namespace WindowFilter;

/// <summary>
/// ファイルへログを書き出すロガー。書き込みのたびにファイルを開閉し、
/// サイズ上限を超えたら世代ローテーションする。複数プロセス／スレッドからの
/// 同時書き込みに備えて名前付き Mutex で直列化する。
/// （BrowserGuard の Logger.cs を参考に作成）
/// </summary>
internal sealed class Logger
{
    private const int MaxGeneration = 10;

    private const long DefaultMaxLogSize = 10 * 1024 * 1024;

    private const string LogFileNameBase = "WindowFilter";

    // 書き込みの瞬間だけファイルを開くため、ローテーションが他の書き手の
    // 途中で起きないよう Mutex で守る（同一マシンの複数インスタンス対策）。
    private static readonly Mutex FileMutex = new(false, @"Local\WindowFilter.Logger");

    private static readonly TimeSpan MutexTimeout = TimeSpan.FromSeconds(5);

    private readonly long _maxLogSize;

    public string FilePath { get; } = "";

    private string LogDirectory { get; } = "";

    private bool EnableLogging { get; }

    // アプリ全体で共有する既定のロガー。静的に呼び出せる入口。
    private static readonly Logger Shared = new();

    public static void Line(string message) => Shared.Log(message);

    public static void Line(Exception e) => Shared.Log(e);

    public void Log(string message) => NoException(() => LogImpl(message));

    public void Log(Exception e) => NoException(() => LogImpl(e));

    public Logger() : this(DefaultDirectory()) { }

    // ディレクトリとサイズを引数にできるのは、実ログに書かずにローテーションを試すため。
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
            // ログ出力できないが、全体の処理は続行する。
        }
    }

    private static string DefaultDirectory() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "WindowFilter");

    private static void NoException(Action func)
    {
        try { func(); } catch { }
    }

    private void LogImpl(string message)
    {
        if (!EnableLogging)
        {
            return;
        }
        Write($"{GetTimestamp()} : {message}");
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
                // 保持していたプロセスが落ちた場合。ファイル自体は無事。
                held = true;
            }

            RotateIfNeeded();
            // ファイルを共有で開くことで、他インスタンスも書き続けられる。
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

    // 世代ファイルはログと同じ場所に置く。別の場所から見ると移動対象が無く、
    // ログを切り詰めるだけになってしまう。
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
