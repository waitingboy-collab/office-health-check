using System.Management;
using System.Net;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using OfficeHealthCheck.Core;
using static OfficeHealthCheck.Core.Rules;
using EnumerationOptions = System.IO.EnumerationOptions;

namespace OfficeHealthCheck;

/// <summary>
/// Събира данните от Windows и ги подава на правилата в Core.
/// Всяка проверка е отделна: ако една се провали, останалите продължават.
/// Нищо не се променя по компютъра — само се чете.
/// </summary>
internal static class SystemInfo
{
    public static async Task<HealthReport> CollectAsync(AppSettings settings, IProgress<string>? progress = null)
    {
        var now = DateTime.Now;
        var results = new List<CheckResult>();
        var os = ReadOs();

        void Step(string what) => progress?.Report(what);

        Step("Windows…");
        results.Add(Safe("Windows", "Поддръжка на Windows", () => WindowsLifecycle.Evaluate(os.Build, os.Edition, now)));
        results.Add(Safe("Windows", "Последно обновление на Windows", () => LastUpdate(ReadLastUpdate(), now)));
        results.Add(Safe("Windows", "Чака рестарт", () => PendingReboot(ReadPendingReboot())));
        results.Add(Safe("Windows", "Време без рестарт", () => Uptime(TimeSpan.FromMilliseconds(Environment.TickCount64))));
        results.Add(Safe("Windows", "Активация", () => Activation(ReadActivation())));

        Step("Защита…");
        results.Add(Safe("Защита", "Антивирусна защита", () => Antivirus(ReadAntivirus())));
        results.Add(Safe("Защита", "Защитна стена", () => Firewall(ReadFirewallProfiles(), ReadThirdPartyFirewall())));

        Step("Дискове…");
        foreach (var drive in DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed && d.IsReady))
        {
            var name = drive.Name.TrimEnd('\\');
            results.Add(Safe("Дискове", $"Свободно място на {name}", () => DiskSpace(name, drive.AvailableFreeSpace, drive.TotalSize)));
        }
        results.AddRange(SafeMany("Дискове", "Състояние на дисковете", ReadDiskHealth));

        Step("Ресурси…");
        results.Add(Safe("Ресурси", "Оперативна памет", () =>
        {
            var (total, available) = ReadMemory();
            return Memory(total, available);
        }));
        results.Add(Safe("Ресурси", "Временни файлове", () => TempFiles(ReadTempSize())));
        results.Add(Safe("Ресурси", "Програми при стартиране", () => Startup(ReadStartupPrograms())));

        Step("Принтери…");
        results.Add(Safe("Принтери", "Принтери", () => Printers(ReadSpoolerRunning(), ReadPrinters())));

        Step("Архив…");
        results.Add(Safe("Архив", "Архив", () =>
        {
            var folder = settings.BackupFolder;
            bool exists = !string.IsNullOrWhiteSpace(folder) && Directory.Exists(folder);
            return Backup(folder, exists, exists ? NewestEntry(folder!) : null, now);
        }));

        Step("Интернет…");
        results.Add(await SafeAsync("Мрежа", "Интернет", CheckInternetAsync));

        return new HealthReport(Environment.MachineName, Environment.UserName, os.Description, now, results);
    }

    // ---------- Обвивки, които не позволяват една проверка да спре всичко ----------

    private static CheckResult Safe(string category, string title, Func<CheckResult> check)
    {
        try { return check(); }
        catch (Exception ex) { return Skipped(category, title, ex); }
    }

    private static IEnumerable<CheckResult> SafeMany(string category, string title, Func<IEnumerable<CheckResult>> check)
    {
        try { return check().ToList(); }
        catch (Exception ex) { return new[] { Skipped(category, title, ex) }; }
    }

    private static async Task<CheckResult> SafeAsync(string category, string title, Func<Task<CheckResult>> check)
    {
        try { return await check(); }
        catch (Exception ex) { return Skipped(category, title, ex); }
    }

    private static CheckResult Skipped(string category, string title, Exception ex) =>
        new(category, title, CheckStatus.Skipped,
            ex is UnauthorizedAccessException or ManagementException { ErrorCode: ManagementStatus.AccessDenied }
                ? "Нужни са админ права за тази проверка."
                : $"Не може да се провери: {ex.Message}");

    // ---------- Windows ----------

    internal sealed record OsInfo(int Build, string? Edition, string Description);

    internal static OsInfo ReadOs()
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
        int build = int.TryParse(key?.GetValue("CurrentBuildNumber") as string, out var b) ? b : Environment.OSVersion.Version.Build;
        string? edition = key?.GetValue("EditionID") as string;
        string product = key?.GetValue("ProductName") as string ?? "Windows";
        string display = key?.GetValue("DisplayVersion") as string ?? "";
        object? ubr = key?.GetValue("UBR");

        // В регистъра Windows 11 все още пише „Windows 10“ в ProductName.
        if (build >= 22000) product = product.Replace("Windows 10", "Windows 11");

        string description = $"{product} {display} (build {build}{(ubr is int u ? "." + u : "")})".Replace("  ", " ");
        return new OsInfo(build, edition, description);
    }

    private static DateTime? ReadLastUpdate()
    {
        DateTime? newest = null;
        foreach (var hotfix in Query(@"root\cimv2", "SELECT InstalledOn FROM Win32_QuickFixEngineering"))
        {
            if (hotfix["InstalledOn"] is not string text || string.IsNullOrWhiteSpace(text)) continue;

            if (DateTime.TryParseExact(text, new[] { "M/d/yyyy", "MM/dd/yyyy", "yyyyMMdd" },
                    System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var date) ||
                DateTime.TryParse(text, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out date))
            {
                if (newest is null || date > newest) newest = date;
            }
        }
        return newest;
    }

    private static bool ReadPendingReboot()
    {
        string[] keys =
        {
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired",
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending",
        };
        return keys.Any(k =>
        {
            using var key = Registry.LocalMachine.OpenSubKey(k);
            return key is not null;
        });
    }

    private static int? ReadActivation()
    {
        const string windowsAppId = "55c92734-d682-4d71-983e-d6ec3f16059f";
        foreach (var product in Query(@"root\cimv2",
                     $"SELECT LicenseStatus FROM SoftwareLicensingProduct WHERE ApplicationID='{windowsAppId}' AND PartialProductKey IS NOT NULL"))
        {
            return Convert.ToInt32(product["LicenseStatus"]);
        }
        return null;
    }

    // ---------- Защита ----------

    private static IReadOnlyList<AntivirusProduct>? ReadAntivirus()
    {
        try
        {
            return Query(@"root\SecurityCenter2", "SELECT displayName, productState FROM AntiVirusProduct")
                .Select(p => new AntivirusProduct(p["displayName"] as string ?? "Антивирусна", Convert.ToInt32(p["productState"])))
                .ToList();
        }
        catch (ManagementException ex) when (ex.ErrorCode == ManagementStatus.InvalidNamespace)
        {
            return null; // Windows Server няма Security Center
        }
    }

    private static bool ReadThirdPartyFirewall()
    {
        try
        {
            return Query(@"root\SecurityCenter2", "SELECT productState FROM FirewallProduct")
                .Any(p => DecodeProductState(Convert.ToInt32(p["productState"])).Enabled);
        }
        catch (ManagementException)
        {
            return false;
        }
    }

    private static IReadOnlyDictionary<string, bool?> ReadFirewallProfiles()
    {
        // Груповите политики (GPO) са с предимство пред локалните настройки.
        var profiles = new Dictionary<string, (string Local, string Policy)>
        {
            ["Domain"] = ("DomainProfile", "DomainProfile"),
            ["Private"] = ("StandardProfile", "PrivateProfile"),
            ["Public"] = ("PublicProfile", "PublicProfile"),
        };

        var result = new Dictionary<string, bool?>();
        foreach (var (name, (local, policy)) in profiles)
        {
            bool? enabled = ReadDword(@$"SOFTWARE\Policies\Microsoft\WindowsFirewall\{policy}", "EnableFirewall")
                            ?? ReadDword(@$"SYSTEM\CurrentControlSet\Services\SharedAccess\Parameters\FirewallPolicy\{local}", "EnableFirewall");
            result[name] = enabled;
        }
        return result;
    }

    private static bool? ReadDword(string path, string name)
    {
        using var key = Registry.LocalMachine.OpenSubKey(path);
        return key?.GetValue(name) is int value ? value != 0 : null;
    }

    // ---------- Дискове ----------

    private static IEnumerable<CheckResult> ReadDiskHealth()
    {
        foreach (var disk in Query(@"root\Microsoft\Windows\Storage", "SELECT FriendlyName, HealthStatus FROM MSFT_PhysicalDisk"))
        {
            yield return DiskHealth(disk["FriendlyName"] as string ?? "диск", Convert.ToInt32(disk["HealthStatus"]));
        }
    }

    // ---------- Ресурси ----------

    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX buffer);

    private static (long Total, long Available) ReadMemory()
    {
        var status = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        if (!GlobalMemoryStatusEx(ref status)) throw new InvalidOperationException("GlobalMemoryStatusEx");
        return ((long)status.ullTotalPhys, (long)status.ullAvailPhys);
    }

    private static long ReadTempSize()
    {
        var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = 0 };
        long total = 0;
        foreach (var folder in new[] { Path.GetTempPath(), Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp") }
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!Directory.Exists(folder)) continue;
            foreach (var file in new DirectoryInfo(folder).EnumerateFiles("*", options))
            {
                try { total += file.Length; } catch (IOException) { }
            }
        }
        return total;
    }

    private static IReadOnlyList<string> ReadStartupPrograms()
    {
        var names = new List<string>();

        void FromRun(RegistryKey root, string path, string approvedPath)
        {
            using var key = root.OpenSubKey(path);
            using var approved = root.OpenSubKey(approvedPath);
            if (key is null) return;
            foreach (var name in key.GetValueNames())
            {
                // StartupApproved: нечетен първи байт = изключено от Диспечера на задачите
                if (approved?.GetValue(name) is byte[] flags && flags.Length > 0 && (flags[0] & 1) == 1) continue;
                names.Add(name);
            }
        }

        const string approvedRun = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
        FromRun(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Run", approvedRun);
        FromRun(Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", approvedRun);
        FromRun(Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run",
            @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run32");

        foreach (var folder in new[] { Environment.SpecialFolder.Startup, Environment.SpecialFolder.CommonStartup })
        {
            var path = Environment.GetFolderPath(folder);
            if (!Directory.Exists(path)) continue;
            names.AddRange(Directory.EnumerateFiles(path)
                .Where(f => !f.EndsWith("desktop.ini", StringComparison.OrdinalIgnoreCase))
                .Select(f => Path.GetFileNameWithoutExtension(f) ?? f));
        }

        return names.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(n => n).ToList();
    }

    // ---------- Принтери ----------

    private static bool ReadSpoolerRunning() =>
        Query(@"root\cimv2", "SELECT State FROM Win32_Service WHERE Name='Spooler'")
            .Any(s => string.Equals(s["State"] as string, "Running", StringComparison.OrdinalIgnoreCase));

    private static IReadOnlyList<PrinterInfo> ReadPrinters()
    {
        var jobs = Query(@"root\cimv2", "SELECT Name FROM Win32_PrintJob")
            .Select(j => (j["Name"] as string ?? "").Split(',')[0].Trim())
            .GroupBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

        var list = new List<PrinterInfo>();
        foreach (var p in Query(@"root\cimv2", "SELECT Name, Default, WorkOffline, PrinterStatus, DetectedErrorState FROM Win32_Printer"))
        {
            string name = p["Name"] as string ?? "Принтер";
            int status = Convert.ToInt32(p["PrinterStatus"] ?? 0);
            int error = Convert.ToInt32(p["DetectedErrorState"] ?? 0);
            bool offline = (p["WorkOffline"] as bool? ?? false) || status == 7 || error == 9;
            // 4 няма хартия, 6 няма тонер, 7 отворен капак, 8 заседнала хартия, 10 нужен сервиз, 11 пълна тава
            bool hasError = error is 4 or 6 or 7 or 8 or 10 or 11;
            list.Add(new PrinterInfo(name, p["Default"] as bool? ?? false, offline, hasError, jobs.GetValueOrDefault(name)));
        }
        return list;
    }

    // ---------- Архив ----------

    private static DateTime? NewestEntry(string folder)
    {
        var options = new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = 2, IgnoreInaccessible = true };
        DateTime? newest = null;
        foreach (var entry in new DirectoryInfo(folder).EnumerateFileSystemInfos("*", options))
        {
            if (newest is null || entry.LastWriteTime > newest) newest = entry.LastWriteTime;
        }
        return newest;
    }

    // ---------- Мрежа ----------

    private static async Task<CheckResult> CheckInternetAsync()
    {
        bool dns;
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(4));
            var addresses = await Dns.GetHostAddressesAsync("www.microsoft.com", cts.Token);
            dns = addresses.Length > 0;
        }
        catch
        {
            dns = false;
        }

        bool ping = false;
        long? ms = null;
        try
        {
            using var pinger = new Ping();
            var reply = await pinger.SendPingAsync(IPAddress.Parse("1.1.1.1"), 2000);
            ping = reply.Status == IPStatus.Success;
            if (ping) ms = reply.RoundtripTime;
        }
        catch
        {
            ping = false;
        }

        return Internet(dns, ping, ms);
    }

    // ---------- WMI ----------

    private static IEnumerable<ManagementBaseObject> Query(string scope, string wql)
    {
        using var searcher = new ManagementObjectSearcher(scope, wql);
        searcher.Options.Timeout = TimeSpan.FromSeconds(20);
        // Колекцията не се затваря тук, за да останат обектите четими.
        return searcher.Get().Cast<ManagementBaseObject>().ToList();
    }
}
