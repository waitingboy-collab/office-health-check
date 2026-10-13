using System.Globalization;

namespace OfficeHealthCheck.Core;

internal static class Format
{
    private static readonly CultureInfo Bg = CultureInfo.GetCultureInfo("bg-BG");

    public static string Bytes(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double value = bytes;
        int unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return value.ToString(unit >= 3 ? "0.0" : "0", Bg) + " " + units[unit];
    }

    public static string Date(DateTime date) => date.ToString("d.MM.yyyy", Bg);

    public static string Percent(double value) => value.ToString("0", Bg) + "%";

    public static string Days(int days) => days == 1 ? "1 ден" : $"{days} дни";
}
