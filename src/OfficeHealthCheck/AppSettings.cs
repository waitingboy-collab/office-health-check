using System.Text.Json;

namespace OfficeHealthCheck;

/// <summary>Настройки в %AppData%\OfficeHealthCheck\settings.json.</summary>
public sealed class AppSettings
{
    /// <summary>Папката, в която се правят архивите (USB диск или мрежова папка).</summary>
    public string? BackupFolder { get; set; }

    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "OfficeHealthCheck", "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
        }
        catch
        {
            // Повреден файл — започваме отначало.
        }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // Не е критично.
        }
    }
}
