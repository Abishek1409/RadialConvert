using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RadialConvert.Settings;

public enum ModifierKey { Shift, Ctrl, Alt }

/// <summary>User settings persisted as JSON in %APPDATA%\RadialConvert\settings.json.</summary>
public sealed class AppSettings
{
    public ModifierKey Modifier { get; set; } = ModifierKey.Shift;

    /// <summary>Conversion ids the user switched off. Everything else is enabled.</summary>
    public HashSet<string> DisabledConversions { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    [JsonIgnore]
    public static string Folder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RadialConvert");

    [JsonIgnore]
    private static string FilePath => Path.Combine(Folder, "settings.json");

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public bool IsEnabled(string conversionId) => !DisabledConversions.Contains(conversionId);

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), JsonOpts);
                if (s != null)
                {
                    s.DisabledConversions = new HashSet<string>(s.DisabledConversions, StringComparer.OrdinalIgnoreCase);
                    return s;
                }
            }
        }
        catch
        {
            // Corrupt file: fall back to defaults rather than failing to start.
        }
        return new AppSettings();
    }

    public void Save()
    {
        Directory.CreateDirectory(Folder);
        var tmp = FilePath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(this, JsonOpts));
        File.Move(tmp, FilePath, overwrite: true);
    }
}
