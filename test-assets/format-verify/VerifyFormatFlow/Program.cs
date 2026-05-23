using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using DatConverter;

var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
var temp = Path.Combine(root, "verify-output");
Directory.CreateDirectory(temp);
var input = Path.Combine(temp, "sample.dat");
File.WriteAllText(input, "dummy");
var fps = FpsOption.FromLabel("30");

Check("Remux MP4", OutputFormat.Mp4, FfmpegCommandBuilder.BuildRemuxArguments(input, Path.Combine(temp, "sample.mp4"), OutputFormat.Mp4, fps));
Check("Remux MKV", OutputFormat.Mkv, FfmpegCommandBuilder.BuildRemuxArguments(input, Path.Combine(temp, "sample.mkv"), OutputFormat.Mkv, fps));
Check("Encode MP4", OutputFormat.Mp4, FfmpegCommandBuilder.BuildEncodeArguments(input, Path.Combine(temp, "sample.mp4"), OutputFormat.Mp4, fps));
Check("Encode MKV", OutputFormat.Mkv, FfmpegCommandBuilder.BuildEncodeArguments(input, Path.Combine(temp, "sample.mkv"), OutputFormat.Mkv, fps));

Console.WriteLine($"MP4 direct: {Path.GetFileName(OutputPathService.GetDirectOutputPath(input, temp, OutputFormat.Mp4))}");
Console.WriteLine($"MKV direct: {Path.GetFileName(OutputPathService.GetDirectOutputPath(input, temp, OutputFormat.Mkv))}");
File.WriteAllText(Path.Combine(temp, "sample.mkv"), "conflict");
Console.WriteLine($"MKV conflict: {Path.GetFileName(OutputPathService.PlanOutputPath(input, temp, OutputFormat.Mkv))}");

static void Check(string label, OutputFormat format, IReadOnlyList<string> args)
{
    var joined = string.Join(" ", args);
    var hasFaststart = args.Contains("+faststart");
    var output = args[^1];
    Console.WriteLine($"{label}: output={Path.GetExtension(output)}, faststart={hasFaststart}");
    if (format == OutputFormat.Mkv && (Path.GetExtension(output) != ".mkv" || hasFaststart)) Environment.Exit(2);
    if (format == OutputFormat.Mp4 && (Path.GetExtension(output) != ".mp4" || !hasFaststart)) Environment.Exit(3);
}



