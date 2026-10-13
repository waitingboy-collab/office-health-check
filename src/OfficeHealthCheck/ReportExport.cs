using System.Diagnostics;
using Microsoft.Win32;
using OfficeHealthCheck.Core;

namespace OfficeHealthCheck;

/// <summary>
/// Записва отчета като HTML или PDF. PDF се прави с Microsoft Edge в скрит режим —
/// Edge е във всеки Windows 10/11, така че не трябват допълнителни библиотеки.
/// </summary>
internal static class ReportExport
{
    /// <summary>Записва по разширението на файла. Връща пътя на реално записания файл.</summary>
    public static async Task<string> SaveAsync(HealthReport report, string path)
    {
        string html = HtmlReport.Render(report);

        if (!path.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
        {
            await File.WriteAllTextAsync(path, html);
            return path;
        }

        string temp = Path.Combine(Path.GetTempPath(), $"ohc-{Guid.NewGuid():N}.html");
        await File.WriteAllTextAsync(temp, html);
        try
        {
            if (await TryEdgePdfAsync(temp, path)) return path;

            // Няма Edge или не успя — записваме HTML до PDF-а.
            string fallback = Path.ChangeExtension(path, ".html");
            File.Copy(temp, fallback, overwrite: true);
            return fallback;
        }
        finally
        {
            try { File.Delete(temp); } catch { }
        }
    }

    public static string DefaultFileName(HealthReport report) =>
        $"Проверка_{report.ComputerName}_{report.GeneratedAt:yyyy-MM-dd}.pdf";

    private static async Task<bool> TryEdgePdfAsync(string htmlPath, string pdfPath)
    {
        string? edge = FindEdge();
        if (edge is null) return false;

        string profile = Path.Combine(Path.GetTempPath(), "ohc-edge-profile");
        var psi = new ProcessStartInfo(edge)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var arg in new[]
                 {
                     "--headless", "--disable-gpu", "--no-first-run", "--no-default-browser-check",
                     "--no-pdf-header-footer", $"--user-data-dir={profile}",
                     $"--print-to-pdf={pdfPath}", new Uri(htmlPath).AbsoluteUri,
                 })
        {
            psi.ArgumentList.Add(arg);
        }

        if (File.Exists(pdfPath)) File.Delete(pdfPath);

        using var process = Process.Start(psi);
        if (process is null) return false;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        try
        {
            await process.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            return false;
        }

        return File.Exists(pdfPath) && new FileInfo(pdfPath).Length > 0;
    }

    private static string? FindEdge()
    {
        using (var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\msedge.exe"))
        {
            if (key?.GetValue(null) is string registered && File.Exists(registered)) return registered;
        }

        foreach (var root in new[] { Environment.SpecialFolder.ProgramFilesX86, Environment.SpecialFolder.ProgramFiles })
        {
            string path = Path.Combine(Environment.GetFolderPath(root), @"Microsoft\Edge\Application\msedge.exe");
            if (File.Exists(path)) return path;
        }
        return null;
    }
}
