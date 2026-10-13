namespace OfficeHealthCheck.Core;

/// <summary>
/// Правилата за всяка проверка. Чисти функции без достъп до Windows,
/// за да се тестват лесно. Програмата събира данните и ги подава тук.
/// </summary>
public static class Rules
{
    private const long GB = 1024L * 1024 * 1024;

    // ---------- Дискове ----------

    public static CheckResult DiskSpace(string drive, long freeBytes, long totalBytes)
    {
        double pct = totalBytes > 0 ? freeBytes * 100.0 / totalBytes : 0;
        string summary = $"Свободни {Format.Bytes(freeBytes)} от {Format.Bytes(totalBytes)} ({Format.Percent(pct)}).";
        string title = $"Свободно място на {drive}";

        if (pct < 10 || freeBytes < 5 * GB)
            return new("Дискове", title, CheckStatus.Fail, summary,
                "Изчисти временните файлове (Disk Cleanup) и премести стари файлове на друг диск.");
        if (pct < 20 || freeBytes < 15 * GB)
            return new("Дискове", title, CheckStatus.Warning, summary,
                "Мястото свършва. Изчисти временните файлове и кошчето.");
        return new("Дискове", title, CheckStatus.Ok, summary);
    }

    /// <summary>HealthStatus от MSFT_PhysicalDisk: 0 = Healthy, 1 = Warning, 2 = Unhealthy.</summary>
    public static CheckResult DiskHealth(string model, int healthStatus)
    {
        string title = $"Състояние на диска {model}";
        return healthStatus switch
        {
            0 => new("Дискове", title, CheckStatus.Ok, "Дискът съобщава, че е здрав."),
            1 => new("Дискове", title, CheckStatus.Warning, "Дискът съобщава за предупреждение.",
                "Направи архив на важните файлове и провери диска с програмата на производителя."),
            2 => new("Дискове", title, CheckStatus.Fail, "Дискът съобщава, че е повреден.",
                "Направи архив веднага и смени диска."),
            _ => new("Дискове", title, CheckStatus.Info, "Дискът не съобщава състоянието си."),
        };
    }

    // ---------- Обновления и рестарт ----------

    public static CheckResult LastUpdate(DateTime? lastInstalled, DateTime now)
    {
        const string title = "Последно обновление на Windows";
        if (lastInstalled is null)
            return new("Windows", title, CheckStatus.Warning, "Не може да се определи кога е последното обновление.",
                "Отвори Windows Update и провери ръчно.");

        int days = (now.Date - lastInstalled.Value.Date).Days;
        string summary = $"Последно обновление: {Format.Date(lastInstalled.Value)} (преди {Format.Days(days)}).";

        // Microsoft пуска обновления всеки месец — над 35 дни значи пропуснат месец.
        if (days > 60)
            return new("Windows", title, CheckStatus.Fail, summary,
                "Компютърът не се обновява. Пусни Windows Update и провери за грешки.");
        if (days > 35)
            return new("Windows", title, CheckStatus.Warning, summary, "Пусни Windows Update.");
        return new("Windows", title, CheckStatus.Ok, summary);
    }

    public static CheckResult PendingReboot(bool pending) => pending
        ? new("Windows", "Чака рестарт", CheckStatus.Warning,
            "Windows е инсталирал обновления и чака рестарт.", "Рестартирай компютъра в края на деня.")
        : new("Windows", "Чака рестарт", CheckStatus.Ok, "Няма обновления, които чакат рестарт.");

    public static CheckResult Uptime(TimeSpan uptime)
    {
        int days = (int)uptime.TotalDays;
        string summary = days == 0 ? "Рестартиран днес." : $"Работи без рестарт от {Format.Days(days)}.";
        return days > 14
            ? new("Windows", "Време без рестарт", CheckStatus.Warning, summary,
                "Рестартирай (Рестартиране, не Изключване — бързото стартиране не изчиства системата).")
            : new("Windows", "Време без рестарт", CheckStatus.Ok, summary);
    }

    /// <summary>LicenseStatus от SoftwareLicensingProduct: 1 = активиран.</summary>
    public static CheckResult Activation(int? licenseStatus) => licenseStatus switch
    {
        null => new("Windows", "Активация", CheckStatus.Skipped, "Не може да се провери."),
        1 => new("Windows", "Активация", CheckStatus.Ok, "Windows е активиран."),
        _ => new("Windows", "Активация", CheckStatus.Warning, "Windows не е активиран.",
            "Провери лиценза в Настройки → Система → Активиране."),
    };

    // ---------- Защита ----------

    public sealed record AntivirusProduct(string Name, int ProductState);

    /// <summary>productState от SecurityCenter2: втората шестнадесетична двойка 0x10 = включен, третата 0x00 = актуален.</summary>
    public static (bool Enabled, bool UpToDate) DecodeProductState(int state) =>
        (((state >> 12) & 0xF) == 1, ((state >> 4) & 0xF) == 0);

    public static CheckResult Antivirus(IReadOnlyList<AntivirusProduct>? products)
    {
        const string title = "Антивирусна защита";
        if (products is null)
            return new("Защита", title, CheckStatus.Skipped, "Не може да се провери (напр. Windows Server).");
        if (products.Count == 0)
            return new("Защита", title, CheckStatus.Fail, "Няма антивирусна програма.",
                "Включи Microsoft Defender от Защита на Windows.");

        var decoded = products.Select(p => (p.Name, State: DecodeProductState(p.ProductState))).ToList();
        var details = decoded.Select(d =>
            $"{d.Name}: {(d.State.Enabled ? "включен" : "изключен")}, {(d.State.UpToDate ? "актуален" : "остарели дефиниции")}").ToList();

        var active = decoded.Where(d => d.State.Enabled).ToList();
        if (active.Count == 0)
            return new("Защита", title, CheckStatus.Fail, "Антивирусната защита е изключена.",
                "Включи защитата в реално време.", details);
        if (active.Any(d => d.State.UpToDate))
            return new("Защита", title, CheckStatus.Ok, $"{active.First(d => d.State.UpToDate).Name} е включен и актуален.", null, details);
        return new("Защита", title, CheckStatus.Warning, "Антивирусната програма е с остарели дефиниции.",
            "Обнови дефинициите от програмата или Windows Update.", details);
    }

    /// <summary>profiles: име → включен ли е (null = неизвестно).</summary>
    public static CheckResult Firewall(IReadOnlyDictionary<string, bool?> profiles, bool thirdPartyEnabled)
    {
        const string title = "Защитна стена";
        if (thirdPartyEnabled)
            return new("Защита", title, CheckStatus.Ok, "Работи защитна стена на друга програма.");

        var off = profiles.Where(p => p.Value == false).Select(p => p.Key).ToList();
        if (off.Count == 0)
            return new("Защита", title, CheckStatus.Ok, "Защитната стена на Windows е включена.");

        var status = off.Contains("Public") ? CheckStatus.Fail : CheckStatus.Warning;
        return new("Защита", title, status,
            $"Изключена за: {string.Join(", ", off.Select(ProfileName))}.",
            "Включи я от Защита на Windows → Защитна стена и мрежова защита.");
    }

    private static string ProfileName(string profile) => profile switch
    {
        "Domain" => "домейн мрежа",
        "Private" => "частна мрежа",
        "Public" => "обществена мрежа",
        _ => profile,
    };

    // ---------- Ресурси ----------

    public static CheckResult Memory(long totalBytes, long availableBytes)
    {
        double usedPct = totalBytes > 0 ? (totalBytes - availableBytes) * 100.0 / totalBytes : 0;
        string summary = $"Използвани {Format.Percent(usedPct)} от {Format.Bytes(totalBytes)} RAM.";

        if (usedPct > 90)
            return new("Ресурси", "Оперативна памет", CheckStatus.Warning, summary,
                "Затвори излишните програми и разделите в браузъра.");
        if (totalBytes < 8 * GB)
            return new("Ресурси", "Оперативна памет", CheckStatus.Info, summary,
                "Под 8 GB RAM е малко за Windows 11 с браузър и офис програми.");
        return new("Ресурси", "Оперативна памет", CheckStatus.Ok, summary);
    }

    public static CheckResult TempFiles(long bytes) => bytes > 3 * GB
        ? new("Ресурси", "Временни файлове", CheckStatus.Warning, $"Временните файлове заемат {Format.Bytes(bytes)}.",
            "Изчисти ги с Disk Cleanup.")
        : new("Ресурси", "Временни файлове", CheckStatus.Ok, $"Временните файлове заемат {Format.Bytes(bytes)}.");

    public static CheckResult Startup(IReadOnlyList<string> programs) => programs.Count > 15
        ? new("Ресурси", "Програми при стартиране", CheckStatus.Warning,
            $"{programs.Count} програми се пускат с Windows.",
            "Изключи излишните от Диспечер на задачите → Стартиране.", programs)
        : new("Ресурси", "Програми при стартиране", CheckStatus.Ok,
            $"{programs.Count} програми се пускат с Windows.", null, programs);

    // ---------- Принтери ----------

    public sealed record PrinterInfo(string Name, bool IsDefault, bool Offline, bool HasError, int Jobs);

    public static CheckResult Printers(bool spoolerRunning, IReadOnlyList<PrinterInfo> printers)
    {
        const string title = "Принтери";
        if (!spoolerRunning)
            return new("Принтери", title, CheckStatus.Fail, "Услугата за печат (Print Spooler) е спряна.",
                "Пусни я от Услуги или с Install Helper → Рестарт на спулера.");

        var physical = printers.Where(p => !IsVirtual(p.Name)).ToList();
        var details = printers.Select(p =>
            $"{p.Name}{(p.IsDefault ? " (по подразбиране)" : "")}: " +
            (p.Offline ? "офлайн" : p.HasError ? "грешка" : "готов") +
            (p.Jobs > 0 ? $", {p.Jobs} документа в опашката" : "")).ToList();

        if (physical.Count == 0)
            return new("Принтери", title, CheckStatus.Info, "Няма инсталирани принтери.", null, details);

        var broken = physical.Where(p => p.Offline || p.HasError).ToList();
        var stuck = physical.Where(p => p.Jobs >= 5).ToList();

        if (broken.Any(p => p.IsDefault))
            return new("Принтери", title, CheckStatus.Fail,
                $"Принтерът по подразбиране ({broken.First(p => p.IsDefault).Name}) не е готов.",
                "Провери дали е включен и свързан. Изтрий заседналите документи от опашката.", details);
        int problems = broken.Union(stuck).Count();
        if (problems > 0)
            return new("Принтери", title, CheckStatus.Warning,
                problems == 1 ? "1 принтер има проблем." : $"{problems} принтера имат проблем.",
                "Провери кабела/мрежата и опашката за печат.", details);
        return new("Принтери", title, CheckStatus.Ok,
            physical.Count == 1 ? "Принтерът е готов." : $"{physical.Count} принтера са готови.", null, details);
    }

    public static bool IsVirtual(string printerName) =>
        printerName.Contains("PDF", StringComparison.OrdinalIgnoreCase) ||
        printerName.Contains("XPS", StringComparison.OrdinalIgnoreCase) ||
        printerName.Contains("OneNote", StringComparison.OrdinalIgnoreCase) ||
        printerName.Equals("Fax", StringComparison.OrdinalIgnoreCase);

    // ---------- Архив и мрежа ----------

    /// <summary>folder = настроената папка за архив; newestFile = датата на най-новия файл в нея.</summary>
    public static CheckResult Backup(string? folder, bool folderExists, DateTime? newestFile, DateTime now)
    {
        const string title = "Архив";
        if (string.IsNullOrWhiteSpace(folder))
            return new("Архив", title, CheckStatus.Info, "Не е настроена папка за архив.",
                "Посочи папката (USB диск или мрежова папка), където се правят архивите, за да се проверява.");
        if (!folderExists)
            return new("Архив", title, CheckStatus.Fail, $"Папката за архив ({folder}) не е достъпна.",
                "Провери дали USB дискът е включен или мрежата работи.");
        if (newestFile is null)
            return new("Архив", title, CheckStatus.Fail, $"Папката за архив ({folder}) е празна.",
                "Направи архив.");

        int days = (now.Date - newestFile.Value.Date).Days;
        string summary = $"Последен архив: {Format.Date(newestFile.Value)} (преди {Format.Days(days)}).";
        if (days > 30)
            return new("Архив", title, CheckStatus.Fail, summary, "Архивът е стар. Направи нов веднага.");
        if (days > 7)
            return new("Архив", title, CheckStatus.Warning, summary, "Направи нов архив.");
        return new("Архив", title, CheckStatus.Ok, summary);
    }

    public static CheckResult Internet(bool dnsWorks, bool pingWorks, long? pingMs)
    {
        const string title = "Интернет";
        if (dnsWorks && pingWorks)
            return new("Мрежа", title, CheckStatus.Ok, $"Връзката работи ({pingMs} ms).");
        if (dnsWorks)
            return new("Мрежа", title, CheckStatus.Ok, "Връзката работи (ping е блокиран от мрежата).");
        if (pingWorks)
            return new("Мрежа", title, CheckStatus.Warning, "Има връзка, но имената на сайтове не се разпознават (DNS).",
                "Рестартирай рутера или провери DNS настройките.");
        return new("Мрежа", title, CheckStatus.Fail, "Няма интернет.", "Провери кабела, Wi-Fi и рутера.");
    }
}
