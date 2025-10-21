using System;
using System.IO;
using System.Text.Json;

namespace CashBatch.Desktop.Services;

public interface IUserSettingsService
{
    ExportSettingsData Load();
    void Save(ExportSettingsData data);
}

public sealed class UserSettingsService : IUserSettingsService
{
    private static readonly string AppDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CashBatch");
    private static readonly string FilePath = Path.Combine(AppDir, "user-settings.json");

    public ExportSettingsData Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return new ExportSettingsData();
            var json = File.ReadAllText(FilePath);
            var data = JsonSerializer.Deserialize<ExportSettingsData>(json);
            return data ?? new ExportSettingsData();
        }
        catch
        {
            return new ExportSettingsData();
        }
    }

    public void Save(ExportSettingsData data)
    {
        Directory.CreateDirectory(AppDir);
        var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(FilePath, json);
    }
}

public class ExportSettingsData
{
    public int? FiscalYear { get; set; }
    public int? Period { get; set; }
    public string? BankNumber { get; set; }
    public string? GLBankAccountNumber { get; set; }
    public string? ARAccountNumber { get; set; }
    public string? TermsAccountNumber { get; set; }
    public string? AllowedAccountNumber { get; set; }
    public string? ExportDirectory { get; set; }
}
