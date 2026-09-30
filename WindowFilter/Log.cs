using System.Diagnostics;
using System.Text;

namespace WindowFilter;

/// <summary>
/// ログ 1 行をファイルと（デバッガ実行時は）出力ウィンドウの両方へ書く。
/// Debug.WriteLine はデバッガをアタッチしていないと見えないため、ファイルにも残す。
/// </summary>
internal static class Log
{
    private static readonly object Gate = new();

    /// <summary>実行ファイルと同じディレクトリのログファイル。</summary>
    public static string FilePath => Path.Combine(AppContext.BaseDirectory, "WindowFilter.log");

    public static void Line(string text)
    {
        string line = $"{DateTimeOffset.Now:HH:mm:ss.fff} {text}";

        Debug.WriteLine(line);

        try
        {
            lock (Gate)
            {
                File.AppendAllText(FilePath, line + Environment.NewLine, new UTF8Encoding(false));
            }
        }
        catch
        {
            // ファイルに書けなくても監視は続ける。
        }
    }
}
