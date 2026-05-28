namespace DatConverter;

public sealed class FolderImportPlanner
{
    private readonly Func<string, SpotterSplitExportPlan> buildSplitPlan;

    public FolderImportPlanner()
        : this(path => new SpotterSplitExportPlanBuilder().Build(path))
    {
    }

    public FolderImportPlanner(Func<string, SpotterSplitExportPlan> buildSplitPlan)
    {
        this.buildSplitPlan = buildSplitPlan;
    }

    public FolderImportPlan Build(IReadOnlyCollection<string> datPaths)
    {
        var normalizedPaths = datPaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(Path.GetFullPath)
            .Where(path => !IsKnownMetadataDatFile(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var items = new List<FolderImportPlanItem>();
        foreach (var group in normalizedPaths.GroupBy(path => Path.GetDirectoryName(path) ?? "", StringComparer.OrdinalIgnoreCase))
        {
            var folder = group.Key;
            var groupPaths = group.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToList();
            var splitPlan = buildSplitPlan(folder);
            if (splitPlan.IsStrongConfidence && splitPlan.SegmentCount > 1)
            {
                var splitPaths = splitPlan.Segments
                    .Select(segment => Path.GetFullPath(segment.FilePath))
                    .ToList();
                items.Add(new FolderImportPlanItem
                {
                    Kind = FolderImportPlanItemKind.SplitRecording,
                    SplitExportPlan = splitPlan,
                    DatPaths = splitPaths,
                    FolderPath = folder
                });

                foreach (var remainingPath in groupPaths.Except(splitPaths, StringComparer.OrdinalIgnoreCase))
                {
                    items.Add(CreateSingleItem(folder, remainingPath));
                }

                continue;
            }

            if (groupPaths.Count > 1 && (splitPlan.Warnings.Count > 0 || splitPlan.SegmentCount > 1 || HasExportMetadata(folder)))
            {
                items.Add(new FolderImportPlanItem
                {
                    Kind = FolderImportPlanItemKind.AmbiguousGroup,
                    SplitExportPlan = splitPlan,
                    DatPaths = groupPaths,
                    FolderPath = folder,
                    Warnings = splitPlan.Warnings.Count > 0
                        ? splitPlan.Warnings
                        : new[] { "The folder could not be verified as one contiguous split recording." }
                });
                continue;
            }

            foreach (var path in groupPaths)
            {
                items.Add(CreateSingleItem(folder, path));
            }
        }

        return new FolderImportPlan
        {
            Items = items,
            AllDatPaths = normalizedPaths
        };
    }

    private static FolderImportPlanItem CreateSingleItem(string folder, string path)
    {
        return new FolderImportPlanItem
        {
            Kind = FolderImportPlanItemKind.SingleDat,
            DatPaths = new[] { path },
            FolderPath = folder
        };
    }

    private static bool HasExportMetadata(string folder)
    {
        try
        {
            return Directory.EnumerateFiles(folder, "*.sef").Any() ||
                   Directory.EnumerateFiles(folder, "*.sef2").Any() ||
                   File.Exists(Path.Combine(folder, "MaterialFolderIndex.dat"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return true;
        }
    }

    private static bool IsKnownMetadataDatFile(string path)
    {
        return string.Equals(Path.GetFileName(path), "MaterialFolderIndex.dat", StringComparison.OrdinalIgnoreCase);
    }
}
