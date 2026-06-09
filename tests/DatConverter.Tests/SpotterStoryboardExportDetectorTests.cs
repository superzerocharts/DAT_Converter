using System.Buffers.Binary;
using System.Text;

namespace DatConverter.Tests;

public sealed class SpotterStoryboardExportDetectorTests
{
    [Fact]
    public void Detect_StoryboardDataParsesSixClipsInStoryboardOrder()
    {
        using var temp = new TempDirectory();
        WriteStoryboardFixture(temp.Path);

        var plan = new SpotterStoryboardExportDetector().Detect(temp.Path);

        Assert.True(plan.IsStoryboardExport);
        Assert.True(plan.IsStrongConfidence);
        Assert.Equal(6, plan.ClipCount);
        Assert.Equal(Enumerable.Range(1, 6), plan.Clips.Select(clip => clip.ClipNumber));
        Assert.Equal("Clip 1", plan.Clips[0].ClipName);
        Assert.Equal("dvrfile00000001.dat", plan.Clips[0].DatFileName);
        Assert.Equal("dvrfile00000006.dat", plan.Clips[5].DatFileName);
    }

    [Fact]
    public void Detect_StoryboardDataMapsLayoutChannelsAndDecodesCameraNames()
    {
        using var temp = new TempDirectory();
        WriteStoryboardFixture(temp.Path);

        var plan = new SpotterStoryboardExportDetector().Detect(temp.Path);

        Assert.Equal(24, plan.Clips[0].ChannelId);
        Assert.Equal("2161 Empire 1A", plan.Clips[0].CameraDisplayName);
        Assert.Equal(77, plan.Clips[1].ChannelId);
        Assert.Equal("2164 Empire Elev Lobby 1st", plan.Clips[1].CameraDisplayName);
        Assert.Equal(10, plan.Clips[5].ChannelId);
        Assert.Equal("1822 Lost & Found Podium PTZ", plan.Clips[5].CameraDisplayName);
    }

    [Fact]
    public void Detect_StoryboardDataUsesMaterialFolderIndexTimingWhenAvailable()
    {
        using var temp = new TempDirectory();
        WriteStoryboardFixture(temp.Path);

        var plan = new SpotterStoryboardExportDetector().Detect(temp.Path);

        Assert.Equal(new DateTime(2026, 4, 11, 3, 21, 9, 596), plan.Clips[0].EffectiveStartTime);
        Assert.Equal(new DateTime(2026, 4, 11, 4, 0, 56, 987), plan.Clips[5].EffectiveEndTime);
    }

    [Fact]
    public void Detect_NonStoryboardSidecarReturnsNoStoryboard()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(Path.Combine(temp.Path, "export.sef2"), "<archive2><files /></archive2>");

        var plan = new SpotterStoryboardExportDetector().Detect(temp.Path);

        Assert.False(plan.IsStoryboardExport);
        Assert.Empty(plan.Clips);
    }

    [Fact]
    public void FolderImportPlanner_StrongStoryboardBecomesStoryboardItemNotSplitRecording()
    {
        using var temp = new TempDirectory();
        WriteStoryboardFixture(temp.Path);
        var datPaths = Directory.EnumerateFiles(temp.Path, "*.dat")
            .Where(path => !Path.GetFileName(path).Equals("MaterialFolderIndex.dat", StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var planner = new FolderImportPlanner(_ => new SpotterSplitExportPlan { ExportFolder = temp.Path, Confidence = "Strong" });

        var plan = planner.Build(datPaths);

        Assert.Equal(1, plan.StoryboardExportCount);
        Assert.Equal(6, plan.StoryboardClipCount);
        Assert.Empty(plan.RecommendedSplitPlans);
        Assert.Empty(plan.RecommendedSingleDatPaths);
    }

    internal static void WriteStoryboardFixture(string folder)
    {
        Directory.CreateDirectory(folder);
        for (var index = 1; index <= 6; index++)
        {
            File.WriteAllText(Path.Combine(folder, $"dvrfile{index:00000000}.dat"), "not relevant");
        }

        File.WriteAllText(Path.Combine(folder, "Storyboard.sef2"), BuildStoryboardXml());
        WriteMaterialFolderIndex(Path.Combine(folder, "MaterialFolderIndex.dat"));
    }

    private static string BuildStoryboardXml()
    {
        var nodes = new[]
        {
            ("Clip 1", "2026-04-11 03:21:10.000Z", "2026-04-11 03:22:25.000Z", "dd54d89c-ac1a-4d9b-b403-e1d71f381c34", 24, "2161 Empire 1A"),
            ("Clip 2", "2026-04-11 03:22:20.000Z", "2026-04-11 03:22:29.000Z", "c8fc519f-18ac-4d83-a5c7-017730d9af8b", 77, "2164 Empire Elev Lobby 1st"),
            ("Clip 3", "2026-04-11 03:22:26.000Z", "2026-04-11 03:27:26.000Z", "9766027f-7f99-4e55-8cb9-f37e7c2610f0", 114, "2610 Pool Door PTZ"),
            ("Clip 4", "2026-04-11 03:24:14.000Z", "2026-04-11 03:25:50.000Z", "076fef5d-f466-4f36-a83e-5417dfecfa2b", 79, "2602 Front Desk OV 2"),
            ("Clip 5", "2026-04-11 03:27:19.000Z", "2026-04-11 03:40:30.000Z", "99518659-3457-45ec-9ed9-85948a6ad13a", 152, "2657 Lobby Front Desk Ov Multi"),
            ("Clip 6", "2026-04-11 03:44:10.000Z", "2026-04-11 04:00:57.000Z", "a24a5788-10a7-4415-82a8-7b0319494e64", 10, "1822 Lost & Found Podium PTZ")
        };

        var builder = new StringBuilder();
        builder.AppendLine("<archive2>");
        builder.AppendLine("  <StoryboardData><storyboard name=\"Storyboard\" description=\"\"><clips>");
        foreach (var node in nodes)
        {
            builder.AppendLine($"    <clip name=\"{node.Item1}\" description=\"\"><time start=\"{node.Item2}\" end=\"{node.Item3}\" /><nodes><node path=\"{node.Item4}\" /></nodes></clip>");
        }

        builder.AppendLine("  </clips></storyboard></StoryboardData>");
        builder.AppendLine("  <LayoutData><layoutdata>");
        foreach (var node in nodes)
        {
            builder.AppendLine($"    <node id=\"Camera{node.Item5}\" data=\"&lt;Video ProfileNodeId=&quot;{node.Item4}&quot; /&gt;\" />");
        }

        builder.AppendLine("  </layoutdata></LayoutData>");
        builder.AppendLine("  <files>");
        for (var index = 1; index <= 6; index++)
        {
            builder.AppendLine($"    <file name=\"dvrfile{index:00000000}.dat\" hash=\"{index}\" />");
        }

        builder.AppendLine("  </files>");
        builder.AppendLine("  <channels>");
        foreach (var node in nodes)
        {
            builder.AppendLine($"    <channel dataType=\"Video\" channelId=\"{node.Item5}\" channelType=\"Material\" name=\"{Convert.ToBase64String(Encoding.UTF8.GetBytes(node.Item6))}\" />");
        }

        builder.AppendLine("  </channels>");
        builder.AppendLine("</archive2>");
        return builder.ToString();
    }

    private static void WriteMaterialFolderIndex(string path)
    {
        var records = new[]
        {
            (1, new DateTime(2026, 4, 11, 3, 21, 9, 596), new DateTime(2026, 4, 11, 3, 22, 25, 207)),
            (2, new DateTime(2026, 4, 11, 3, 22, 19, 487), new DateTime(2026, 4, 11, 3, 22, 29, 219)),
            (3, new DateTime(2026, 4, 11, 3, 22, 25, 307), new DateTime(2026, 4, 11, 3, 27, 26, 205)),
            (4, new DateTime(2026, 4, 11, 3, 24, 13, 897), new DateTime(2026, 4, 11, 3, 25, 50, 228)),
            (5, new DateTime(2026, 4, 11, 3, 27, 18, 344), new DateTime(2026, 4, 11, 3, 40, 30, 205)),
            (6, new DateTime(2026, 4, 11, 3, 44, 9, 487), new DateTime(2026, 4, 11, 4, 0, 56, 987))
        };
        var bytes = new byte[21 + (records.Length * 44) + 16];
        for (var index = 0; index < records.Length; index++)
        {
            var offset = 21 + (index * 44);
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset + 11, 4), records[index].Item1);
            BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(offset + 15, 8), records[index].Item2.Ticks);
            BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(offset + 23, 8), records[index].Item3.Ticks);
        }

        File.WriteAllBytes(path, bytes);
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
