using OfficeHealthCheck.Core;
using Xunit;
using static OfficeHealthCheck.Core.Rules;

namespace OfficeHealthCheck.Tests;

public class DiskTests
{
    private const long GB = 1024L * 1024 * 1024;

    [Theory]
    [InlineData(100, 500, CheckStatus.Ok)]        // 20%, 100 GB
    [InlineData(99, 500, CheckStatus.Warning)]    // под 20%
    [InlineData(49, 500, CheckStatus.Fail)]       // под 10%
    [InlineData(14, 50, CheckStatus.Warning)]     // 28%, но под 15 GB
    [InlineData(4, 20, CheckStatus.Fail)]         // 20%, но под 5 GB
    public void Disk_space_thresholds(long freeGb, long totalGb, CheckStatus expected)
    {
        Assert.Equal(expected, DiskSpace("C:", freeGb * GB, totalGb * GB).Status);
    }

    [Fact]
    public void Zero_size_disk_does_not_crash()
    {
        Assert.Equal(CheckStatus.Fail, DiskSpace("X:", 0, 0).Status);
    }

    [Theory]
    [InlineData(0, CheckStatus.Ok)]
    [InlineData(1, CheckStatus.Warning)]
    [InlineData(2, CheckStatus.Fail)]
    [InlineData(5, CheckStatus.Info)]
    public void Disk_health_maps_windows_status(int health, CheckStatus expected)
    {
        Assert.Equal(expected, DiskHealth("Samsung SSD", health).Status);
    }
}

public class WindowsTests
{
    private static readonly DateTime Now = new(2026, 10, 13);

    [Theory]
    [InlineData(0, CheckStatus.Ok)]
    [InlineData(35, CheckStatus.Ok)]
    [InlineData(36, CheckStatus.Warning)]
    [InlineData(60, CheckStatus.Warning)]
    [InlineData(61, CheckStatus.Fail)]
    public void Last_update_age(int daysAgo, CheckStatus expected)
    {
        Assert.Equal(expected, LastUpdate(Now.AddDays(-daysAgo), Now).Status);
    }

    [Fact]
    public void Unknown_last_update_is_a_warning()
    {
        Assert.Equal(CheckStatus.Warning, LastUpdate(null, Now).Status);
    }

    [Theory]
    [InlineData(0, CheckStatus.Ok)]
    [InlineData(14, CheckStatus.Ok)]
    [InlineData(15, CheckStatus.Warning)]
    public void Uptime_over_two_weeks_needs_restart(int days, CheckStatus expected)
    {
        Assert.Equal(expected, Uptime(TimeSpan.FromDays(days) + TimeSpan.FromHours(3)).Status);
    }

    [Theory]
    [InlineData(true, CheckStatus.Warning)]
    [InlineData(false, CheckStatus.Ok)]
    public void Pending_reboot(bool pending, CheckStatus expected)
    {
        Assert.Equal(expected, PendingReboot(pending).Status);
    }

    [Theory]
    [InlineData(1, CheckStatus.Ok)]
    [InlineData(0, CheckStatus.Warning)]
    [InlineData(5, CheckStatus.Warning)]
    [InlineData(null, CheckStatus.Skipped)]
    public void Activation_status(int? licenseStatus, CheckStatus expected)
    {
        Assert.Equal(expected, Activation(licenseStatus).Status);
    }
}

public class LifecycleTests
{
    [Theory]
    // 24H2 Home/Pro: последен ден 13.10.2026
    [InlineData(26100, "Professional", "2026-10-13", CheckStatus.Warning)]
    [InlineData(26100, "Professional", "2026-10-14", CheckStatus.Fail)]
    [InlineData(26100, "Core", "2026-07-01", CheckStatus.Ok)]
    // 24H2 Enterprise: година повече
    [InlineData(26100, "Enterprise", "2026-10-14", CheckStatus.Ok)]
    // 23H2 Home/Pro приключи на 11.11.2025
    [InlineData(22631, "Professional", "2026-10-13", CheckStatus.Fail)]
    // 25H2 се поддържа до 12.10.2027
    [InlineData(26200, "Professional", "2026-10-13", CheckStatus.Ok)]
    // Windows 10 22H2 — винаги предупреждение (зависи от ESU)
    [InlineData(19045, "Professional", "2026-10-13", CheckStatus.Warning)]
    // Стар Windows 10
    [InlineData(19044, "Professional", "2026-10-13", CheckStatus.Fail)]
    [InlineData(17763, "Professional", "2026-10-13", CheckStatus.Fail)]
    // Непознати версии
    [InlineData(27000, "Professional", "2026-10-13", CheckStatus.Info)]
    [InlineData(22635, "Professional", "2026-10-13", CheckStatus.Info)]
    public void Windows_support_status(int build, string edition, string date, CheckStatus expected)
    {
        var result = WindowsLifecycle.Evaluate(build, edition, DateTime.Parse(date));
        Assert.Equal(expected, result.Status);
    }

    [Fact]
    public void Warning_window_is_exactly_90_days()
    {
        var end = new DateTime(2026, 10, 13);
        Assert.Equal(CheckStatus.Warning, WindowsLifecycle.Evaluate(26100, "Core", end.AddDays(-90)).Status);
        Assert.Equal(CheckStatus.Ok, WindowsLifecycle.Evaluate(26100, "Core", end.AddDays(-91)).Status);
    }

    [Fact]
    public void Releases_are_sorted_and_unique()
    {
        var builds = WindowsLifecycle.Releases.Select(r => r.Build).ToList();
        Assert.Equal(builds.OrderBy(b => b).ToList(), builds);
        Assert.Equal(builds.Count, builds.Distinct().Count());
        Assert.True(WindowsLifecycle.Releases.All(r => r.EnterpriseEnd >= r.HomeProEnd));
    }

    [Theory]
    [InlineData("Enterprise", true)]
    [InlineData("EnterpriseS", true)]
    [InlineData("Education", true)]
    [InlineData("Professional", false)]
    [InlineData("Core", false)]
    [InlineData(null, false)]
    public void Enterprise_detection(string? edition, bool expected)
    {
        Assert.Equal(expected, WindowsLifecycle.IsEnterpriseLike(edition));
    }
}

public class ProtectionTests
{
    [Theory]
    [InlineData(397568, true, true)]    // 0x061100 Defender включен, актуален
    [InlineData(393472, false, true)]   // 0x060100 Defender изключен
    [InlineData(397584, true, false)]   // 0x061110 включен, остарял
    [InlineData(266240, true, true)]    // 0x041000 друга антивирусна, включена
    [InlineData(262144, false, true)]   // 0x040000 изключена
    public void Decodes_security_center_state(int state, bool enabled, bool upToDate)
    {
        Assert.Equal((enabled, upToDate), DecodeProductState(state));
    }

    [Fact]
    public void No_antivirus_is_a_fail()
    {
        Assert.Equal(CheckStatus.Fail, Antivirus(Array.Empty<AntivirusProduct>()).Status);
    }

    [Fact]
    public void Missing_security_center_is_skipped()
    {
        Assert.Equal(CheckStatus.Skipped, Antivirus(null).Status);
    }

    [Fact]
    public void One_good_product_is_enough()
    {
        var result = Antivirus(new[]
        {
            new AntivirusProduct("Windows Defender", 393472), // изключен, защото има друга
            new AntivirusProduct("ESET", 266240),
        });
        Assert.Equal(CheckStatus.Ok, result.Status);
        Assert.Equal(2, result.Details!.Count);
    }

    [Fact]
    public void Enabled_but_outdated_is_a_warning()
    {
        Assert.Equal(CheckStatus.Warning, Antivirus(new[] { new AntivirusProduct("Defender", 397584) }).Status);
    }

    [Fact]
    public void All_disabled_is_a_fail()
    {
        Assert.Equal(CheckStatus.Fail, Antivirus(new[] { new AntivirusProduct("Defender", 393472) }).Status);
    }

    [Fact]
    public void Firewall_public_off_is_a_fail_private_off_is_a_warning()
    {
        var publicOff = new Dictionary<string, bool?> { ["Domain"] = true, ["Private"] = true, ["Public"] = false };
        var privateOff = new Dictionary<string, bool?> { ["Domain"] = true, ["Private"] = false, ["Public"] = true };
        var allOn = new Dictionary<string, bool?> { ["Domain"] = true, ["Private"] = true, ["Public"] = true };

        Assert.Equal(CheckStatus.Fail, Firewall(publicOff, thirdPartyEnabled: false).Status);
        Assert.Equal(CheckStatus.Warning, Firewall(privateOff, thirdPartyEnabled: false).Status);
        Assert.Equal(CheckStatus.Ok, Firewall(allOn, thirdPartyEnabled: false).Status);
        Assert.Equal(CheckStatus.Ok, Firewall(publicOff, thirdPartyEnabled: true).Status);
    }
}

public class ResourceTests
{
    private const long GB = 1024L * 1024 * 1024;

    [Theory]
    [InlineData(16, 8, CheckStatus.Ok)]
    [InlineData(16, 1, CheckStatus.Warning)]   // 94% заети
    [InlineData(4, 2, CheckStatus.Info)]       // малко RAM
    public void Memory_usage(long totalGb, long availableGb, CheckStatus expected)
    {
        Assert.Equal(expected, Memory(totalGb * GB, availableGb * GB).Status);
    }

    [Theory]
    [InlineData(1, CheckStatus.Ok)]
    [InlineData(3, CheckStatus.Ok)]
    [InlineData(4, CheckStatus.Warning)]
    public void Temp_files(long gb, CheckStatus expected)
    {
        Assert.Equal(expected, TempFiles(gb * GB).Status);
    }

    [Theory]
    [InlineData(15, CheckStatus.Ok)]
    [InlineData(16, CheckStatus.Warning)]
    public void Startup_programs(int count, CheckStatus expected)
    {
        var programs = Enumerable.Range(1, count).Select(i => $"App{i}").ToList();
        Assert.Equal(expected, Startup(programs).Status);
    }
}

public class PrinterTests
{
    [Fact]
    public void Stopped_spooler_is_a_fail()
    {
        Assert.Equal(CheckStatus.Fail, Printers(false, Array.Empty<PrinterInfo>()).Status);
    }

    [Fact]
    public void Only_virtual_printers_counts_as_none()
    {
        var printers = new[]
        {
            new PrinterInfo("Microsoft Print to PDF", true, false, false, 0),
            new PrinterInfo("Microsoft XPS Document Writer", false, false, false, 0),
        };
        Assert.Equal(CheckStatus.Info, Printers(true, printers).Status);
    }

    [Fact]
    public void Offline_default_printer_is_a_fail()
    {
        var printers = new[] { new PrinterInfo("HP LaserJet", true, true, false, 0) };
        Assert.Equal(CheckStatus.Fail, Printers(true, printers).Status);
    }

    [Fact]
    public void Broken_secondary_printer_or_stuck_queue_is_a_warning()
    {
        Assert.Equal(CheckStatus.Warning, Printers(true, new[]
        {
            new PrinterInfo("HP LaserJet", true, false, false, 0),
            new PrinterInfo("Canon", false, false, true, 0),
        }).Status);

        Assert.Equal(CheckStatus.Warning, Printers(true, new[]
        {
            new PrinterInfo("HP LaserJet", true, false, false, 7),
        }).Status);
    }

    [Fact]
    public void Ready_printers_are_ok()
    {
        var result = Printers(true, new[] { new PrinterInfo("HP LaserJet", true, false, false, 1) });
        Assert.Equal(CheckStatus.Ok, result.Status);
        Assert.Contains("1 документа", result.Details![0]);
    }
}

public class BackupAndNetworkTests
{
    private static readonly DateTime Now = new(2026, 10, 13);

    [Theory]
    [InlineData(0, CheckStatus.Ok)]
    [InlineData(7, CheckStatus.Ok)]
    [InlineData(8, CheckStatus.Warning)]
    [InlineData(30, CheckStatus.Warning)]
    [InlineData(31, CheckStatus.Fail)]
    public void Backup_age(int daysAgo, CheckStatus expected)
    {
        Assert.Equal(expected, Backup(@"E:\Архив", true, Now.AddDays(-daysAgo), Now).Status);
    }

    [Fact]
    public void Backup_states_without_files()
    {
        Assert.Equal(CheckStatus.Info, Backup(null, false, null, Now).Status);
        Assert.Equal(CheckStatus.Fail, Backup(@"E:\Архив", false, null, Now).Status);
        Assert.Equal(CheckStatus.Fail, Backup(@"E:\Архив", true, null, Now).Status);
    }

    [Theory]
    [InlineData(true, true, CheckStatus.Ok)]
    [InlineData(true, false, CheckStatus.Ok)]
    [InlineData(false, true, CheckStatus.Warning)]
    [InlineData(false, false, CheckStatus.Fail)]
    public void Internet(bool dns, bool ping, CheckStatus expected)
    {
        Assert.Equal(expected, Rules.Internet(dns, ping, ping ? 20 : null).Status);
    }
}

public class ReportTests
{
    private static HealthReport Make(params CheckStatus[] statuses) => new(
        "PC-01", "user", "Windows 11 Pro", new DateTime(2026, 10, 13, 9, 30, 0),
        statuses.Select((s, i) => new CheckResult("Тест", $"Проверка {i}", s, "резюме")).ToList());

    [Fact]
    public void Overall_is_the_worst_scored_status()
    {
        Assert.Equal(CheckStatus.Ok, Make(CheckStatus.Ok, CheckStatus.Info, CheckStatus.Skipped).Overall);
        Assert.Equal(CheckStatus.Warning, Make(CheckStatus.Ok, CheckStatus.Warning).Overall);
        Assert.Equal(CheckStatus.Fail, Make(CheckStatus.Warning, CheckStatus.Fail, CheckStatus.Ok).Overall);
        Assert.Equal(CheckStatus.Ok, Make().Overall);
    }

    [Fact]
    public void Html_report_escapes_text_and_lists_every_check()
    {
        var report = new HealthReport("PC-<01>", "user", "Windows 11", new DateTime(2026, 10, 13),
            new[]
            {
                new CheckResult("Принтери", "Принтери", CheckStatus.Warning, "HP \"офис\" & <склад>", "Провери кабела",
                    new[] { "HP: офлайн" }),
                new CheckResult("Дискове", "Свободно място на C:", CheckStatus.Ok, "Свободни 100 GB"),
            });

        var html = HtmlReport.Render(report);

        Assert.Contains("PC-&lt;01&gt;", html);
        Assert.Contains("HP &quot;офис&quot; &amp; &lt;склад&gt;", html);
        Assert.DoesNotContain("<склад>", html);
        Assert.Contains("Свободно място на C:", html);
        Assert.Contains("HP: офлайн", html);
        Assert.Contains("Внимание: 1", html);
        Assert.Contains("lang=\"bg\"", html);
    }
}
