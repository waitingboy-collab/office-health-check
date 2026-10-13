namespace OfficeHealthCheck.Core;

/// <summary>
/// До кога всяка версия на Windows получава обновления за сигурност.
/// Източник: Microsoft Lifecycle. Обнови таблицата, когато излезе нова версия.
/// </summary>
public static class WindowsLifecycle
{
    public sealed record Release(int Build, string Name, DateTime HomeProEnd, DateTime EnterpriseEnd);

    public static readonly IReadOnlyList<Release> Releases = new[]
    {
        new Release(19045, "Windows 10 22H2", new DateTime(2025, 10, 14), new DateTime(2025, 10, 14)),
        new Release(22000, "Windows 11 21H2", new DateTime(2023, 10, 10), new DateTime(2024, 10, 8)),
        new Release(22621, "Windows 11 22H2", new DateTime(2024, 10, 8), new DateTime(2025, 10, 14)),
        new Release(22631, "Windows 11 23H2", new DateTime(2025, 11, 11), new DateTime(2026, 11, 10)),
        new Release(26100, "Windows 11 24H2", new DateTime(2026, 10, 13), new DateTime(2027, 10, 12)),
        new Release(26200, "Windows 11 25H2", new DateTime(2027, 10, 12), new DateTime(2028, 10, 10)),
    };

    /// <summary>Платените/безплатните ESU обновления за Windows 10 22H2 (за домашни и малки офиси).</summary>
    public static readonly DateTime Windows10EsuEnd = new(2027, 10, 12);

    public static bool IsEnterpriseLike(string? editionId) =>
        editionId is not null &&
        (editionId.StartsWith("Enterprise", StringComparison.OrdinalIgnoreCase) ||
         editionId.StartsWith("Education", StringComparison.OrdinalIgnoreCase) ||
         editionId.StartsWith("IoTEnterprise", StringComparison.OrdinalIgnoreCase));

    public static CheckResult Evaluate(int build, string? editionId, DateTime now)
    {
        const string category = "Windows";
        const string title = "Поддръжка на Windows";

        if (build < 19045)
        {
            return new CheckResult(category, title, CheckStatus.Fail,
                $"Стара версия на Windows (build {build}). Не получава обновления за сигурност.",
                "Обнови до Windows 11 или поне до Windows 10 22H2.");
        }

        var release = Releases.FirstOrDefault(r => r.Build == build);
        if (release is null)
        {
            string which = build > Releases.Max(r => r.Build)
                ? "По-нова версия от известните на програмата"
                : "Непозната версия (напр. Insider)";
            return new CheckResult(category, title, CheckStatus.Info,
                $"{which} (build {build}). Програмата не знае до кога се поддържа.");
        }

        if (release.Build == 19045)
        {
            return new CheckResult(category, title, CheckStatus.Warning,
                $"Windows 10 22H2. Основната поддръжка свърши на {Format.Date(release.HomeProEnd)}.",
                $"Получава обновления само ако е записан в ESU (до {Format.Date(Windows10EsuEnd)}). " +
                "Виж проверката „Последно обновление“. Планирай преминаване към Windows 11.");
        }

        var end = IsEnterpriseLike(editionId) ? release.EnterpriseEnd : release.HomeProEnd;
        int daysLeft = (end.Date - now.Date).Days;

        if (daysLeft < 0)
        {
            return new CheckResult(category, title, CheckStatus.Fail,
                $"{release.Name} вече не получава обновления (от {Format.Date(end)}).",
                "Пусни Windows Update и инсталирай най-новата версия на Windows 11.");
        }

        if (daysLeft <= 90)
        {
            return new CheckResult(category, title, CheckStatus.Warning,
                $"{release.Name} спира да получава обновления на {Format.Date(end)} " +
                (daysLeft == 0 ? "(днес е последният ден)." : $"(след {Format.Days(daysLeft)})."),
                "Обнови до по-нова версия през Windows Update.");
        }

        return new CheckResult(category, title, CheckStatus.Ok,
            $"{release.Name}, поддържа се до {Format.Date(end)}.");
    }
}
