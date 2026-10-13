using OfficeHealthCheck.Core;

namespace OfficeHealthCheck;

internal static class Program
{
    /// <summary>
    /// Без аргументи отваря прозореца.
    /// С --report &lt;файл.pdf|файл.html&gt; прави проверката без прозорец и записва отчета —
    /// удобно за Планировчика на задачи (напр. всеки понеделник в мрежова папка).
    /// Код на изход: 0 = наред, 1 = внимание, 2 = проблем, 3 = грешка.
    /// </summary>
    [STAThread]
    private static int Main(string[] args)
    {
        int reportIndex = Array.FindIndex(args, a => a.Equals("--report", StringComparison.OrdinalIgnoreCase));
        if (reportIndex >= 0)
        {
            if (reportIndex + 1 >= args.Length) return 3;
            return RunHeadless(args[reportIndex + 1]);
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
        return 0;
    }

    private static int RunHeadless(string path)
    {
        try
        {
            var report = SystemInfo.CollectAsync(AppSettings.Load()).GetAwaiter().GetResult();

            string full = Path.GetFullPath(Environment.ExpandEnvironmentVariables(path));
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            ReportExport.SaveAsync(report, full).GetAwaiter().GetResult();

            return report.Overall switch
            {
                CheckStatus.Fail => 2,
                CheckStatus.Warning => 1,
                _ => 0,
            };
        }
        catch
        {
            return 3;
        }
    }
}
