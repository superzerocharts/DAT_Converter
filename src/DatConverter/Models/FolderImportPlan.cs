namespace DatConverter;

public sealed class FolderImportPlan
{
    public IReadOnlyList<FolderImportPlanItem> Items { get; init; } = Array.Empty<FolderImportPlanItem>();

    public IReadOnlyList<string> AllDatPaths { get; init; } = Array.Empty<string>();

    public int SplitRecordingCount => Items.Count(item => item.Kind == FolderImportPlanItemKind.SplitRecording);

    public int SplitRecordingPartCount => Items
        .Where(item => item.Kind == FolderImportPlanItemKind.SplitRecording)
        .Sum(item => item.SplitExportPlan?.SegmentCount ?? item.DatPaths.Count);

    public int SingleDatCount => Items.Count(item => item.Kind == FolderImportPlanItemKind.SingleDat);

    public int AmbiguousItemCount => Items.Count(item => item.Kind == FolderImportPlanItemKind.AmbiguousGroup);

    public IReadOnlyList<string> RecommendedSingleDatPaths => Items
        .Where(item => item.Kind is FolderImportPlanItemKind.SingleDat or FolderImportPlanItemKind.AmbiguousGroup)
        .SelectMany(item => item.DatPaths)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
        .ToList();

    public IReadOnlyList<SpotterSplitExportPlan> RecommendedSplitPlans => Items
        .Where(item => item.Kind == FolderImportPlanItemKind.SplitRecording && item.SplitExportPlan is not null)
        .Select(item => item.SplitExportPlan!)
        .ToList();
}
