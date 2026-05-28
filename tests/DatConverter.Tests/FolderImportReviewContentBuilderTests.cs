namespace DatConverter.Tests;

public sealed class FolderImportReviewContentBuilderTests
{
    [Fact]
    public void Build_OneSplitRecording_UsesSingularCombineWording()
    {
        var plan = new FolderImportPlan
        {
            Items =
            [
                new FolderImportPlanItem
                {
                    Kind = FolderImportPlanItemKind.SplitRecording,
                    SplitExportPlan = CreateSplitPlan(1)
                }
            ],
            AllDatPaths = new[] { @"C:\export\dvrfile00000001.dat", @"C:\export\dvrfile00000002.dat" }
        };

        var content = FolderImportReviewContentBuilder.Build(plan);

        Assert.True(content.ShowsCombineQuestion);
        Assert.Equal("Combine detected recording parts?", content.Title);
        Assert.Contains("found 1 multi-part recording", content.Text);
        Assert.Contains("Yes: combine each verified recording into one video.", content.Text);
        Assert.Contains("No: add every DAT file separately.", content.Text);
        Assert.Contains("Files from different recordings will not be combined together.", content.Text);
    }

    [Fact]
    public void Build_MultipleSplitRecordings_UsesPluralCombineWording()
    {
        var plan = new FolderImportPlan
        {
            Items =
            [
                new FolderImportPlanItem { Kind = FolderImportPlanItemKind.SplitRecording, SplitExportPlan = CreateSplitPlan(1) },
                new FolderImportPlanItem { Kind = FolderImportPlanItemKind.SplitRecording, SplitExportPlan = CreateSplitPlan(2) }
            ],
            AllDatPaths = new[] { @"C:\a\1.dat", @"C:\a\2.dat", @"C:\b\1.dat", @"C:\b\2.dat" }
        };

        var content = FolderImportReviewContentBuilder.Build(plan);

        Assert.True(content.ShowsCombineQuestion);
        Assert.Equal("Combine detected recording parts?", content.Title);
        Assert.Contains("found 2 multi-part recordings", content.Text);
    }

    [Fact]
    public void Build_NoSplitRecordings_AvoidsCombineWording()
    {
        var plan = new FolderImportPlan
        {
            Items =
            [
                new FolderImportPlanItem
                {
                    Kind = FolderImportPlanItemKind.SingleDat,
                    DatPaths = new[] { @"C:\export\single.dat" }
                }
            ],
            AllDatPaths = new[] { @"C:\export\single.dat" }
        };

        var content = FolderImportReviewContentBuilder.Build(plan);

        Assert.False(content.ShowsCombineQuestion);
        Assert.Equal("Add DAT files?", content.Title);
        Assert.Equal("DAT Converter found 1 DAT file.", content.Text);
        Assert.DoesNotContain("combine", content.Text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MapYes_WithCombineQuestion_UsesRecommendedCombinedImport()
    {
        Assert.Equal(FolderImportReviewChoice.UseRecommendedImport, FolderImportReviewContent.MapYes(showsCombineQuestion: true));
    }

    [Fact]
    public void MapYes_WithoutCombineQuestion_UsesSeparateImport()
    {
        Assert.Equal(FolderImportReviewChoice.ImportEveryDatSeparately, FolderImportReviewContent.MapYes(showsCombineQuestion: false));
    }

    private static SpotterSplitExportPlan CreateSplitPlan(int number)
    {
        return new SpotterSplitExportPlan
        {
            ExportFolder = $@"C:\export{number}",
            Confidence = "Strong",
            Segments =
            [
                new SpotterSplitExportSegment { SegmentNumber = 1, FileName = "dvrfile00000001.dat", FilePath = $@"C:\export{number}\dvrfile00000001.dat" },
                new SpotterSplitExportSegment { SegmentNumber = 2, FileName = "dvrfile00000002.dat", FilePath = $@"C:\export{number}\dvrfile00000002.dat" }
            ]
        };
    }
}
