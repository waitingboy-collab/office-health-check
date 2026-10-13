namespace OfficeHealthCheck.Core;

/// <summary>Резултат от една проверка. Подредени от най-лошо към най-добро.</summary>
public enum CheckStatus
{
    Fail,
    Warning,
    Skipped,
    Info,
    Ok,
}

public sealed record CheckResult(
    string Category,
    string Title,
    CheckStatus Status,
    string Summary,
    string? Advice = null,
    IReadOnlyList<string>? Details = null);

public sealed record HealthReport(
    string ComputerName,
    string UserName,
    string OsDescription,
    DateTime GeneratedAt,
    IReadOnlyList<CheckResult> Results)
{
    /// <summary>Общото състояние е най-лошото от проверките, които дават оценка.</summary>
    public CheckStatus Overall =>
        Results.Where(r => r.Status is CheckStatus.Fail or CheckStatus.Warning or CheckStatus.Ok)
               .Select(r => r.Status)
               .DefaultIfEmpty(CheckStatus.Ok)
               .Min();

    public int Count(CheckStatus status) => Results.Count(r => r.Status == status);
}

public static class StatusText
{
    public static string Label(CheckStatus status) => status switch
    {
        CheckStatus.Ok => "Наред",
        CheckStatus.Warning => "Внимание",
        CheckStatus.Fail => "Проблем",
        CheckStatus.Info => "Информация",
        CheckStatus.Skipped => "Не е проверено",
        _ => status.ToString(),
    };

    public static string OverallHeadline(CheckStatus overall) => overall switch
    {
        CheckStatus.Fail => "Има проблеми, които трябва да се оправят",
        CheckStatus.Warning => "Работи, но има неща за наглеждане",
        _ => "Компютърът е в добро състояние",
    };
}
