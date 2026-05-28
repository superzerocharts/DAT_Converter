namespace DatConverter;

public sealed class FolderImportPlanItem
{
    public FolderImportPlanItemKind Kind { get; init; }

    public SpotterSplitExportPlan? SplitExportPlan { get; init; }

    public IReadOnlyList<string> DatPaths { get; init; } = Array.Empty<string>();

    public string FolderPath { get; init; } = "";

    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
}
