using System.Text.Json;

namespace DatConverter;

public sealed class AppSettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly bool nvencAvailable;

    public AppSettingsService()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DAT Converter",
            "settings.json"))
    {
    }

    public AppSettingsService(bool nvencAvailable)
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DAT Converter",
            "settings.json"),
            nvencAvailable)
    {
    }

    public AppSettingsService(string settingsPath, bool nvencAvailable = false)
    {
        SettingsPath = settingsPath;
        this.nvencAvailable = nvencAvailable;
    }

    public string SettingsPath { get; }

    public AppSettings Load(out string logMessage)
    {
        try
        {
            if (!File.Exists(SettingsPath))
            {
                logMessage = $"Settings file not found. Using defaults. Path: {SettingsPath}";
                return CreateDefault();
            }

            var json = File.ReadAllText(SettingsPath);
            var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? CreateDefault();
            settings = Normalize(settings, nvencAvailable);
            ApplyStartupControlDefaults(settings);
            logMessage = $"Settings loaded. Path: {SettingsPath}";
            return settings;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            logMessage = $"Settings could not be loaded. Using defaults. Path: {SettingsPath}; Error: {ex.Message}";
            return CreateDefault();
        }
    }

    public bool Save(AppSettings settings, out string? errorMessage)
    {
        try
        {
            var normalized = Normalize(settings, nvencAvailable);
            var directory = Path.GetDirectoryName(SettingsPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(normalized, JsonOptions));
            errorMessage = null;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            errorMessage = $"Settings could not be saved. Path: {SettingsPath}; Error: {ex.Message}";
            return false;
        }
    }

    public static AppSettings CreateDefault()
    {
        return new AppSettings();
    }

    public static AppSettings Normalize(AppSettings settings)
    {
        return Normalize(settings, nvencAvailable: false);
    }

    public static AppSettings Normalize(AppSettings settings, bool nvencAvailable)
    {
        settings.OutputDestinationMode = Enum.TryParse<OutputDestinationMode>(settings.OutputDestinationMode, out var mode)
            ? mode.ToString()
            : OutputDestinationMode.SameFolderAsSource.ToString();

        settings.OutputFormat = string.Equals(settings.OutputFormat, "MKV", StringComparison.OrdinalIgnoreCase) ? "MKV" : "MP4";
        settings.ConversionMode = ConversionModes.ParseDisplay(settings.ConversionMode);
        if (string.Equals(settings.ConversionMode, ConversionModes.EncodeNvenc, StringComparison.OrdinalIgnoreCase) && !nvencAvailable)
        {
            settings.ConversionMode = ConversionModes.Encode;
        }
        settings.Fps = IsAutoDetectFps(settings.Fps) ? "Auto-detect" : FpsOption.FromLabel(settings.Fps).Label;

        if (settings.WindowWidth < 960)
        {
            settings.WindowWidth = 960;
        }

        if (settings.WindowHeight < 760)
        {
            settings.WindowHeight = 760;
        }

        return settings;
    }

    private static void ApplyStartupControlDefaults(AppSettings settings)
    {
        settings.OutputDestinationMode = OutputDestinationMode.SameFolderAsSource.ToString();
        settings.OutputFormat = "MP4";
        settings.ConversionMode = ConversionModes.Remux;
        settings.Fps = "Auto-detect";
    }

    private static bool IsAutoDetectFps(string? value)
    {
        return string.Equals(value, "Auto-detect", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, "Auto", StringComparison.OrdinalIgnoreCase);
    }
}
