namespace DatConverter.Tests;

public sealed class FolderImportPlannerTests
{
    [Fact]
    public void Build_StrongSplitExportFolder_BecomesOneRecommendedSplitItem()
    {
        using var temp = new TempDirectory();
        var files = CreateDatFiles(temp.Path, 4);
        var planner = new FolderImportPlanner(_ => CreatePlan(temp.Path, files, "Strong"));

        var plan = planner.Build(files);

        var split = Assert.Single(plan.RecommendedSplitPlans);
        Assert.Equal(4, split.SegmentCount);
        Assert.Empty(plan.RecommendedSingleDatPaths);
        Assert.Equal(1, plan.SplitRecordingCount);
        Assert.Equal(4, plan.SplitRecordingPartCount);
    }

    [Fact]
    public void Build_StandaloneDat_BecomesSingleItem()
    {
        using var temp = new TempDirectory();
        var datPath = Path.Combine(temp.Path, "single.dat");
        File.WriteAllText(datPath, "not relevant");
        var planner = new FolderImportPlanner(_ => new SpotterSplitExportPlan { ExportFolder = temp.Path, Confidence = "None" });

        var plan = planner.Build(new[] { datPath });

        Assert.Empty(plan.RecommendedSplitPlans);
        Assert.Equal(new[] { datPath }, plan.RecommendedSingleDatPaths);
        Assert.Equal(1, plan.SingleDatCount);
    }

    [Fact]
    public void Build_WeakSplitPlan_ImportsSeparatelyNotCombined()
    {
        using var temp = new TempDirectory();
        var files = CreateDatFiles(temp.Path, 2);
        File.WriteAllText(Path.Combine(temp.Path, "export.sef2"), "<archive2 />");
        var planner = new FolderImportPlanner(_ => CreatePlan(temp.Path, files, "Weak", "Timing was contradictory."));

        var plan = planner.Build(files);

        Assert.Empty(plan.RecommendedSplitPlans);
        Assert.Equal(files, plan.RecommendedSingleDatPaths);
        Assert.Equal(1, plan.AmbiguousItemCount);
    }

    [Fact]
    public void Build_MultipleSubfolders_ProducesSeparateGroups()
    {
        using var temp = new TempDirectory();
        var firstFolder = Path.Combine(temp.Path, "Export A");
        var secondFolder = Path.Combine(temp.Path, "Export B");
        Directory.CreateDirectory(firstFolder);
        Directory.CreateDirectory(secondFolder);
        var firstFiles = CreateDatFiles(firstFolder, 2);
        var secondFiles = CreateDatFiles(secondFolder, 3);
        var planner = new FolderImportPlanner(folder =>
            string.Equals(folder, firstFolder, StringComparison.OrdinalIgnoreCase)
                ? CreatePlan(firstFolder, firstFiles, "Strong")
                : CreatePlan(secondFolder, secondFiles, "Strong"));

        var plan = planner.Build(firstFiles.Concat(secondFiles).ToList());

        Assert.Equal(2, plan.RecommendedSplitPlans.Count);
        Assert.Contains(plan.RecommendedSplitPlans, split => split.ExportFolder == firstFolder && split.SegmentCount == 2);
        Assert.Contains(plan.RecommendedSplitPlans, split => split.ExportFolder == secondFolder && split.SegmentCount == 3);
    }

    [Fact]
    public void Build_AllDatPathsPreservesEveryDatForSeparateImport()
    {
        using var temp = new TempDirectory();
        var files = CreateDatFiles(temp.Path, 3);
        var planner = new FolderImportPlanner(_ => CreatePlan(temp.Path, files, "Strong"));

        var plan = planner.Build(files);

        Assert.Equal(files, plan.AllDatPaths);
    }

    [Fact]
    public void Build_KnownMetadataDatFile_IsNotQueuedAsVideoInput()
    {
        using var temp = new TempDirectory();
        var files = CreateDatFiles(temp.Path, 4);
        var indexPath = Path.Combine(temp.Path, "MaterialFolderIndex.dat");
        File.WriteAllText(indexPath, "metadata");
        var allPaths = files.Concat(new[] { indexPath }).ToList();
        var planner = new FolderImportPlanner(_ => CreatePlan(temp.Path, files, "Strong"));

        var plan = planner.Build(allPaths);

        Assert.Equal(files, plan.AllDatPaths);
        Assert.DoesNotContain(indexPath, plan.RecommendedSingleDatPaths);
        Assert.Single(plan.RecommendedSplitPlans);
    }

    private static List<string> CreateDatFiles(string folder, int count)
    {
        var paths = new List<string>();
        for (var index = 1; index <= count; index++)
        {
            var path = Path.Combine(folder, $"dvrfile{index:00000000}.dat");
            File.WriteAllText(path, "not relevant");
            paths.Add(path);
        }

        return paths;
    }

    private static SpotterSplitExportPlan CreatePlan(string folder, IReadOnlyList<string> files, string confidence, params string[] warnings)
    {
        return new SpotterSplitExportPlan
        {
            ExportFolder = folder,
            SelectedSourcePath = files[0],
            SelectedSegmentNumber = 1,
            Confidence = confidence,
            Warnings = warnings,
            Segments = files.Select((path, index) => new SpotterSplitExportSegment
            {
                SegmentNumber = index + 1,
                FileName = Path.GetFileName(path),
                FilePath = path,
                StartTime = DateTime.Today.AddSeconds(index * 10),
                EndTime = DateTime.Today.AddSeconds(index * 10 + 9),
                GapFromPrevious = index == 0 ? null : TimeSpan.FromMilliseconds(34)
            }).ToList()
        };
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "DatConverter.Tests." + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch
            {
            }
        }
    }
}
