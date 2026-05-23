using DatConverter;

var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
var temp = Path.Combine(root, "verify-output");
if (Directory.Exists(temp)) Directory.Delete(temp, true);
Directory.CreateDirectory(temp);
var input = Path.Combine(temp, "clip.dat");
File.WriteAllText(input, "dat");
File.WriteAllText(Path.Combine(temp, "clip.mkv"), "existing mkv");

var mkvPlanned = OutputPathService.PlanOutputPath(input, temp, OutputFormat.Mkv)!;
var mp4Planned = OutputPathService.PlanOutputPath(input, temp, OutputFormat.Mp4)!;
Console.WriteLine($"With existing MKV, selected MKV plans: {Path.GetFileName(mkvPlanned)}");
Console.WriteLine($"After switching to MP4, plans: {Path.GetFileName(mp4Planned)}");
if (Path.GetFileName(mkvPlanned) != "clip_converted.mkv") Environment.Exit(20);
if (Path.GetFileName(mp4Planned) != "clip.mp4") Environment.Exit(21);
