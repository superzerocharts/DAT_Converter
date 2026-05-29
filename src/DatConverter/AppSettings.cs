namespace DatConverter;

public sealed class AppSettings
{
    public string OutputDestinationMode { get; set; } = nameof(DatConverter.OutputDestinationMode.SameFolderAsSource);

    public string LastChosenOutputFolder { get; set; } = string.Empty;

    public string OutputFormat { get; set; } = "MP4";

    public string ConversionMode { get; set; } = "Remux";

    public string Fps { get; set; } = "Auto-detect";

    public int WindowWidth { get; set; } = 1080;

    public int WindowHeight { get; set; } = 980;
}
