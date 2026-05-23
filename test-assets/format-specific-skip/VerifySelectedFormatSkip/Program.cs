using DatConverter;

var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
var temp = Path.Combine(root, "verify-output");
if (Directory.Exists(temp)) Directory.Delete(temp, true);
Directory.CreateDirectory(temp);

CheckMp4DoesNotBlockMkv(temp);
CheckMkvConflictForMkv(temp);
CheckMkvDoesNotBlockMp4(temp);

static void CheckMp4DoesNotBlockMkv(string temp)
{
    var input = Path.Combine(temp, "caseA.dat");
    File.WriteAllText(input, "dat");
    File.WriteAllText(Path.Combine(temp, "caseA.mp4"), "existing mp4");
    var mkvDirect = OutputPathService.GetDirectOutputPath(input, temp, OutputFormat.Mkv)!;
    var mkvPlanned = OutputPathService.PlanOutputPath(input, temp, OutputFormat.Mkv)!;
    Console.WriteLine($"Case A MKV direct exists: {File.Exists(mkvDirect)}; planned: {Path.GetFileName(mkvPlanned)}");
    if (File.Exists(mkvDirect) || Path.GetFileName(mkvPlanned) != "caseA.mkv") Environment.Exit(10);
}

static void CheckMkvConflictForMkv(string temp)
{
    var input = Path.Combine(temp, "caseB.dat");
    File.WriteAllText(input, "dat");
    File.WriteAllText(Path.Combine(temp, "caseB.mkv"), "existing mkv");
    var mkvDirect = OutputPathService.GetDirectOutputPath(input, temp, OutputFormat.Mkv)!;
    var mkvPlanned = OutputPathService.PlanOutputPath(input, temp, OutputFormat.Mkv)!;
    Console.WriteLine($"Case B MKV direct exists: {File.Exists(mkvDirect)}; planned: {Path.GetFileName(mkvPlanned)}");
    if (!File.Exists(mkvDirect) || Path.GetFileName(mkvPlanned) != "caseB_converted.mkv") Environment.Exit(11);
}

static void CheckMkvDoesNotBlockMp4(string temp)
{
    var input = Path.Combine(temp, "caseD.dat");
    File.WriteAllText(input, "dat");
    File.WriteAllText(Path.Combine(temp, "caseD.mkv"), "existing mkv");
    var mp4Direct = OutputPathService.GetDirectOutputPath(input, temp, OutputFormat.Mp4)!;
    var mp4Planned = OutputPathService.PlanOutputPath(input, temp, OutputFormat.Mp4)!;
    Console.WriteLine($"Case D MP4 direct exists: {File.Exists(mp4Direct)}; planned: {Path.GetFileName(mp4Planned)}");
    if (File.Exists(mp4Direct) || Path.GetFileName(mp4Planned) != "caseD.mp4") Environment.Exit(12);
}
