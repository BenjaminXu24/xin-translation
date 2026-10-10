using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Suiyi.Core;

namespace Suiyi.App;

public sealed class AppSettings
{
    public string Endpoint { get; set; } = "https://api.openai.com/v1/chat/completions";
    public string Model { get; set; } = "";
    public int TimeoutSeconds { get; set; } = 45;
    public string Shortcut { get; set; } = "Ctrl+Alt+T";
    public bool RememberApiKey { get; set; }
    public List<string> AcceptedServiceEndpoints { get; set; } = new();
    public List<GlossaryEntry> Glossary { get; set; } = new()
    {
        new("商户", "merchant"), new("结算", "settlement"), new("订单号", "order ID"),
        new("回调", "callback"), new("API", "API"), new("PHP", "PHP")
    };
}

public static class SettingsStore
{
    public static string FilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Suiyi", "settings.json");
    public static AppSettings Load(out string warning)
        => Load(FilePath, out warning);

    public static AppSettings Load(string path, out string warning)
    {
        warning = "";
        try
        {
            if (!File.Exists(path)) return new AppSettings();
            var value = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path));
            if (value is null || value.Glossary is null || value.Glossary.Count > 100 || value.AcceptedServiceEndpoints is null ||
                value.AcceptedServiceEndpoints.Count > 256 || string.IsNullOrWhiteSpace(value.Shortcut) || value.TimeoutSeconds is < 5 or > 180) throw new InvalidDataException();
            return value;
        }
        catch
        {
            warning = "设置文件无法读取，已使用默认设置。";
            return new AppSettings();
        }
    }
    public static bool Save(AppSettings value)
        => Save(value, FilePath);

    public static bool Save(AppSettings value, string path)
    {
        try
        {
            path = Path.GetFullPath(path);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            string temporary = path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporary, path, true);
            return true;
        }
        catch { return false; }
    }
}
