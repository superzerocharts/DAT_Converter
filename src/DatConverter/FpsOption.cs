namespace DatConverter;

public sealed record FpsOption(string Label, string FfmpegValue)
{
    public static FpsOption FromLabel(string? label)
    {
        return label switch
        {
            "15" => new FpsOption("15", "15"),
            "20" => new FpsOption("20", "20"),
            "24" => new FpsOption("24", "24"),
            "25" => new FpsOption("25", "25"),
            "29.97" => new FpsOption("29.97", "30000/1001"),
            _ => new FpsOption("30", "30")
        };
    }
}
