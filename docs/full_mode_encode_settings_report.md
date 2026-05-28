# Full Mode Encode Settings Report

## 1. Summary

In the UI, `Full` maps internally to conversion mode `Encode`. The mapping is centralized in `MainForm.ParseConversionModeDisplay`, where `Full` and `Encode` both become `Encode`; display formatting maps `Encode` back to `Full` (`src/DatConverter/MainForm.cs:4681`, `src/DatConverter/MainForm.cs:4689`).

All direct Full-mode re-encode paths use `FfmpegCommandBuilder` and the same core x264 settings: `-c:v libx264`, `-preset veryfast`, `-crf 22`, `-an`, selected input FPS via `-r`, and `format=yuv420p` in the video filter (`src/DatConverter/FfmpegCommandBuilder.cs:62`, `src/DatConverter/FfmpegCommandBuilder.cs:110`). MP4 adds `-movflags +faststart`; MKV does not.

Normal Full and trimmed Full differ in trim source handling and `setpts` behavior:

- Normal Full encodes the original DAT/raw H.264 input directly and uses `setpts=N/(<fps>*TB),fps=<fps>,format=yuv420p`.
- Trimmed Full first extracts a temporary clean `.h264` range, then encodes that temp file with `-ss <preRoll>` and `-t <duration>` placed after `-i`, and uses `setpts=PTS-STARTPTS,fps=<fps>,format=yuv420p`.
- Timestamp burn-in is an optional Full-only filter extension. It appends three `drawtext` filters after the base Full or trimmed Full filter.
- Combined split recordings are a special case: non-trimmed split Full without burn-in is currently blocked as unsupported, but split trimmed Full and split burn-timestamp Full use `EncodeTrimmedSplitAsync` and therefore the trimmed encode argument pattern (`src/DatConverter/MainForm.cs:3605`, `src/DatConverter/MainForm.cs:3657`, `src/DatConverter/MainForm.cs:3693`).

## 2. Exact FFmpeg Commands/Arguments

The app passes arguments through `ProcessStartInfo.ArgumentList`, so quoting is handled by .NET process startup. The patterns below show argument order and placeholders.

### Full MP4

Built by `FfmpegCommandBuilder.BuildEncodeArguments` and called by `ConversionService.EncodeAsync` (`src/DatConverter/FfmpegCommandBuilder.cs:62`, `src/DatConverter/ConversionService.cs:86`).

```text
-n
-nostats
-progress pipe:1
-fflags +genpts+discardcorrupt
-err_detect ignore_err
-f h264
-r <fps.FfmpegValue>
-i <inputPath>
-vf setpts=N/(<fpsExpression>*TB),fps=<fps.FfmpegValue>,format=yuv420p[,...drawtext filters if burnTimestamp]
-an
-c:v libx264
-preset veryfast
-crf 22
-movflags +faststart
[-metadata creation_time=<value>]
[-metadata title=<value>]
[-metadata comment=<value>]
<outputPath>
```

FPS handling:

- Manual `29.97` maps to `30000/1001` in `FpsOption.FromLabel` (`src/DatConverter/FpsOption.cs:5`).
- Auto-detected NTSC 29.97 also maps to `30000/1001` in `FpsDecisionPolicy` (`src/DatConverter/Services/FpsDecisionPolicy.cs:7`).
- For normal Full only, the `setpts` expression wraps `30000/1001` as `(30000/1001)`, producing `setpts=N/((30000/1001)*TB)` (`src/DatConverter/FfmpegCommandBuilder.cs:164`).

Overwrite/temp behavior:

- `-n` prevents FFmpeg overwrite.
- Output path guards block writing over the source and existing outputs before FFmpeg starts (`src/DatConverter/ConversionService.cs:810`).
- On failed non-canceled conversion, partial output is renamed to `.partial`; on cancellation, active output is deleted when safe (`src/DatConverter/ConversionService.cs:744`, `src/DatConverter/PartialOutputService.cs`).

### Full MKV

Same as Full MP4 except there is no MP4-only `-movflags +faststart`.

```text
-n
-nostats
-progress pipe:1
-fflags +genpts+discardcorrupt
-err_detect ignore_err
-f h264
-r <fps.FfmpegValue>
-i <inputPath>
-vf setpts=N/(<fpsExpression>*TB),fps=<fps.FfmpegValue>,format=yuv420p[,...drawtext filters if burnTimestamp]
-an
-c:v libx264
-preset veryfast
-crf 22
[-metadata creation_time=<value>]
[-metadata title=<value>]
[-metadata comment=<value>]
<outputPath>
```

MKV has no container-specific encode flags in the current Full path beyond omitting MP4 `faststart`.

### Full Trimmed MP4/MKV

Built by `FfmpegCommandBuilder.BuildTrimEncodeArguments` and called by `EncodeTrimmedAsync` and `EncodeTrimmedSplitAsync` (`src/DatConverter/FfmpegCommandBuilder.cs:110`, `src/DatConverter/ConversionService.cs:326`, `src/DatConverter/ConversionService.cs:419`).

Before FFmpeg starts, the app extracts a temporary clean H.264 file:

- Single DAT trim temp path purpose: `trim-encode` (`src/DatConverter/ConversionService.cs:354`).
- Split trim combined temp path purpose: `trimmed-split-encode`, with per-segment temps `trimmed-encode-segment-###` (`src/DatConverter/ConversionService.cs:453`, `src/DatConverter/ConversionService.cs:466`).
- Temp files are deleted in `finally`.

FFmpeg argument pattern:

```text
-n
-nostats
-progress pipe:1
-fflags +genpts+discardcorrupt
-err_detect ignore_err
-f h264
-r <fps.FfmpegValue>
-i <tempTrimmedH264Path>
-ss <preRollSeconds>
-t <trimDurationSeconds>
-vf setpts=PTS-STARTPTS,fps=<fps.FfmpegValue>,format=yuv420p[,...drawtext filters if burnTimestamp]
-an
-c:v libx264
-preset veryfast
-crf 22
[-movflags +faststart for MP4 only]
[-metadata creation_time=<value>]
[-metadata title=<value>]
[-metadata comment=<value>]
<outputPath>
```

Trim argument placement:

- `-ss` and `-t` are output-side options placed after `-i`.
- `preRoll` is the difference between the selected trim start and the extracted keyframe start; if no earlier keyframe was used, it is zero (`src/DatConverter/ConversionService.cs:1038`).
- Seconds are formatted as invariant `0.###` and clamped at zero (`src/DatConverter/FfmpegCommandBuilder.cs:234`).

### Full Timestamp/Burn-In MP4/MKV

Burn timestamp is available only when the queue item requests it and Full/`Encode` mode is active (`src/DatConverter/Services/BurnTimestampMetadataBuilder.cs:10`, `src/DatConverter/Services/BurnTimestampMetadataBuilder.cs:28`). It is currently a queue-item feature, not used by the single-file Convert button path.

Burn-in uses the same normal Full or trimmed Full command pattern, with this filter suffix appended after `format=yuv420p`:

```text
,drawtext=<fontfile option>text='<cameraName>':x=20:y=20:fontcolor=white:fontsize=28
,drawtext=<fontfile option>text='%{pts\:localtime\:<epochSeconds>\:%m/%d/%y}':x=20:y=54:fontcolor=white:fontsize=28
,drawtext=<fontfile option>text='%{pts\:localtime\:<epochSeconds>\:%H\\\:%M\\\:%S}':x=20:y=88:fontcolor=white:fontsize=28
```

Font handling:

- Preferred font files are `C:\Windows\Fonts\consolab.ttf`, then `consola.ttf`, then `arialbd.ttf` (`src/DatConverter/Services/BurnTimestampFontResolver.cs:3`).
- If a font is found, `fontfile='<escaped path>':` is prepended inside each `drawtext`.
- Backslashes become `/`, drive-colon is escaped, and single quotes are escaped (`src/DatConverter/FfmpegCommandBuilder.cs:201`).
- If no preferred font exists, `fontfile=` is omitted and a warning is prepended to the result stderr text.

Burn timestamp timing:

- Non-trim Full uses the recording start time.
- Trimmed Full uses recording start plus trim start (`src/DatConverter/Services/BurnTimestampMetadataBuilder.cs:16`).
- Split burn timestamp with no explicit trim is implemented by creating a full-range `TrimRange(0, timeline.TotalDuration)` and running `EncodeTrimmedSplitAsync`, so it uses the trimmed encode command pattern (`src/DatConverter/MainForm.cs:3693`).

## 3. Encoding Settings Table

| Setting | Current value | Defined in code | Full-mode paths | Performance impact | Quality/compatibility impact | User-configurable |
|---|---|---|---|---|---|---|
| Internal mode | `Encode` for UI `Full` | `MainForm.ParseConversionModeDisplay`, `AppSettingsService` | All Full paths | None | Keeps UI label independent of service mode | UI label selectable; internal name not user-configurable |
| `-c:v` | `libx264` | `FfmpegCommandBuilder.BuildEncodeArguments`, `BuildTrimEncodeArguments` | Normal Full, trimmed Full, split trimmed Full, burn-in Full | CPU encode cost | Produces broadly compatible H.264 | No |
| Encoder | `libx264` | Same as above | Same as above | Slower than stream copy | Robust rebuild, better timing recovery than remux | No |
| `-preset` | `veryfast` | Same as above | Same as above | Speed/size tradeoff; faster than default x264 presets | Larger output than slower presets at same CRF | No |
| `-crf` | `22` | Same as above | Same as above | CRF affects bitrate more than encode speed | Reasonable quality/size target; not lossless | No |
| `-vf` normal | `setpts=N/(<fps>*TB),fps=<fps>,format=yuv420p` | `BuildEncodeVideoFilter` | Normal Full MP4/MKV, normal burn-in base | Adds filter graph work | Rebuilds timestamps, enforces selected FPS and yuv420p | FPS part is configurable through Source FPS |
| `-vf` trimmed | `setpts=PTS-STARTPTS,fps=<fps>,format=yuv420p` | `BuildTrimEncodeVideoFilter` | Trimmed Full MP4/MKV, split trimmed Full, split burn full-range | Adds filter graph work | Starts trimmed timeline at zero; enforces selected FPS and yuv420p | FPS part is configurable |
| `setpts` | Normal: `N/(fps*TB)`; trimmed: `PTS-STARTPTS` | `BuildEncodeVideoFilter`, `BuildTrimEncodeVideoFilter` | See above | Minor filter cost | Normal path regenerates timing from frame number; trim path resets extracted timeline | No |
| `fps` filter | `<fps.FfmpegValue>` | Same filter builders | All Full encode paths | Can duplicate/drop frames | Forces selected constant output FPS | Source FPS selection/auto-detect |
| `format=yuv420p` | Always included in Full filters | Same filter builders | All Full encode paths | Pixel format conversion cost if needed | Improves player compatibility | No |
| `-an` | Always present | Full encode builders | All Full encode paths | Avoids audio encode/mux work | Drops audio; source is treated as raw video-only H.264 | No |
| `-movflags` | `+faststart` for MP4 only | Full encode builders | MP4 Full and MP4 trimmed/burn-in Full | Extra MP4 relocation/finalization work | Better progressive playback/startup compatibility | No |
| MKV flags | No encode-specific MKV flags | Full encode builders | MKV Full paths | No extra faststart cost | Container differs only by omitted MP4 flags | Output format user-selectable |
| Selected FPS | `15`, `20`, `24`, `25`, `30000/1001`, or `30` | `FpsOption`, `FpsDecisionPolicy`, queue FPS resolver | All conversion paths | Higher FPS increases frames to encode | Wrong FPS changes playback speed/timing | Yes, manual or Auto-detect |
| `29.97` | `30000/1001` | `FpsOption.FromLabel`, `FpsDecisionPolicy` | All Full paths using 29.97 | Same as selected FPS | Rational NTSC value avoids decimal inconsistency | Yes |
| `-fflags` | `+genpts+discardcorrupt` | Full encode builders | All Full encode paths | May discard corrupt packets | Generates timestamps and ignores corrupt input where possible | No |
| `-err_detect` | `ignore_err` | Full encode builders | All Full encode paths | May continue through errors | More tolerant damaged input handling | No |
| `-f` | `h264` | Full encode builders | All Full encode paths | Raw demux assumptions | Treats DAT/temp as raw H.264 payload | No |
| `-r` | `<fps.FfmpegValue>` before `-i` | Full encode builders | All Full encode paths | Defines input timing before encode | Critical for raw H.264 timing | Source FPS selection/auto-detect |
| Trim flags | `-ss <preRoll>` and `-t <duration>` after `-i` | `BuildTrimEncodeArguments` | Trimmed Full, split trimmed Full, split burn full-range | Output-side trim may decode skipped pre-roll | Exact trim after keyframe-aligned extraction | Trim range user-configurable |
| Timestamp flags | Three `drawtext` filters | `AppendBurnTimestampFilter` | Burn-in Full paths only | Adds text rendering cost per frame | Permanently burns camera/date/time into pixels | Checkbox when recording time exists and Full mode active |
| Font option | Optional `fontfile='<escaped path>':` | `BuildFontFileOption`, `BurnTimestampFontResolver` | Burn-in Full paths | Negligible | Stable text appearance when preferred fonts exist | No |
| `-progress` | `pipe:1` | Full encode builders | All Full encode paths | Minimal | Enables progress parsing | No |
| `-nostats` | Present | Full encode builders | All Full encode paths | Minimal | Keeps stdout progress machine-readable | No |
| Overwrite | `-n`, plus preflight existing-output guard | Full encode builders, `BuildOutputGuardResult` | All Full encode paths | Prevents accidental work/overwrite | Safer output handling | No |
| Temp output/input | Trim encode uses temp `.h264` extraction; failed output renamed `.partial`; canceled output deleted | `ConversionService.BuildTempH264Path`, `PartialOutputService` | Trimmed Full and failed/canceled Full outputs | Extra disk IO for trim extraction | Avoids mutating source and preserves failed outputs for troubleshooting | No |

## 4. Performance Data Currently Available

Currently captured or available from existing app logs/results:

- FFmpeg/ffprobe command line used for conversion and probe (`ConversionResult.CommandLine`, technical log).
- FFmpeg/ffprobe stdout and stderr are captured up to 200,000 characters per stream by `FfmpegProcessRunner`; conversion stdout contains `-progress pipe:1` lines (`src/DatConverter/FfmpegProcessRunner.cs:8`).
- FFmpeg version/build line, libx264 version/config line, stderr warnings/errors, duplicate/drop info, and x264 summary lines can be present in captured stderr when FFmpeg emits them. The UI only appends full stdout/stderr automatically for failures, cancellations, or timeouts (`src/DatConverter/MainForm.cs:4980`).
- Input file size is logged during input validation for single-file selection and exists in Spotter clean extraction results.
- Detected codec, profile, width, height, pixel format, stream frame rate, average frame rate, and duration are captured by `ProbeResult` when ffprobe succeeds (`src/DatConverter/ProbeService.cs:28`, `src/DatConverter/ProbeResult.cs:8`).
- Selected FPS label and FFmpeg FPS value are stored on `QueueItem`, `ConversionResult`, probe logs, and conversion details.
- Duration availability and determinate/indeterminate progress mode are recorded in `ConversionResult`.
- Frame count availability exists in two limited forms: FFmpeg `-progress` `frame` is parsed for live progress, and Spotter extraction records frame-record counts in extraction technical details. ffprobe frame count is not requested.
- Elapsed conversion time is stored as `ConversionResult.ProcessingTime`.
- FFmpeg reported speed is parsed from `-progress` into `ConversionProgress.Speed`.
- FFmpeg reported fps is captured in stdout if emitted by `-progress`, but `ConversionProgressParser` does not currently expose the `fps` key.
- Progress `out_time_us`, `out_time_ms`, or `out_time` is parsed into `ConversionProgress.OutputTime`; despite the `out_time_ms` key name, FFmpeg reports microseconds and the code divides by 1000 to get milliseconds (`src/DatConverter/ConversionProgressParser.cs:24`).
- Output size can be inferred from the filesystem after conversion, but `ConversionResult` does not store it.
- Exit code, timeout, cancellation state, stdout, stderr, and partial-output handling messages are stored in `ConversionResult`.

## 5. Performance Data Missing But Useful

Useful data not currently captured as structured fields:

- CPU model, physical core count, logical thread count, and current power/performance mode.
- Full FFmpeg build details as structured metadata rather than incidental stderr text.
- libx264 encoder settings and summary parsed from stderr.
- Average encode speed across the whole conversion.
- Encode speed grouped by resolution and selected FPS.
- Output bitrate.
- Compression ratio: output size divided by input size or extracted payload size.
- Structured dropped/duplicated frame counts from FFmpeg progress/stderr.
- Filter cost indicators, especially `fps`, `format`, and `drawtext`.
- Whether timestamp burn-in materially slows encoding on common source resolutions.
- Whether current trim placement after `-i` affects speed compared with input-side seek after clean extraction.
- Whether changing `preset` improves practical throughput enough to justify size/quality tradeoffs.
- Whether CRF changes materially affect output size, speed, or evidence-review quality.
- Whether a hardware encoding path should be considered later. This is out of current scope.

## 6. Future Optimization Ideas Only

These are not implemented by this audit.

- x264 preset changes:
  - `ultrafast`: much faster, much larger files, lower compression efficiency.
  - `superfast`: faster than `veryfast`, likely larger files.
  - `veryfast`: current baseline.
  - `faster`: slower than current, smaller files at same CRF, likely similar visual quality.
- CRF changes:
  - `20`: higher quality, larger outputs, may be slightly slower.
  - `22`: current baseline.
  - `23`: common x264 default-ish quality target, smaller than 22.
  - `24`: smaller output, more visible loss risk.
  - `26`: much smaller, more quality loss risk for surveillance details.
- Remove unnecessary filters where safe: if some inputs already have stable timing and yuv420p, skipping filters might help, but could weaken Full mode's repair behavior.
- Filter order: current order is timestamp reset, FPS normalization, pixel format, then optional drawtext. Changing order should be benchmarked because drawtext before/after format conversion may affect cost and output.
- Trim argument placement: current `-ss`/`-t` after `-i` favors exact output trimming after keyframe-aligned extraction. Input-side placement may be faster but must be validated for accuracy.
- Timestamp burn-in cost: `drawtext` runs three overlays per frame and likely has measurable cost on long/high-resolution clips.
- MP4 faststart cost: `+faststart` can add finalization time, especially for large MP4s, but improves playback compatibility.
- Thread settings: explicit x264/FFmpeg thread control might help on some CPUs or reduce system impact, but should be measured first.
- MKV vs MP4 performance: MKV avoids MP4 faststart relocation; any difference should be measured with identical encode settings.
- Hardware encoding: a future NVENC/QSV/AMF path could improve speed, but would change quality, compatibility, dependencies, and GPU requirements. It should remain out of scope unless explicitly requested.

## 7. Risks / Inconsistencies

- Full-mode encode settings are duplicated across `BuildEncodeArguments` and `BuildTrimEncodeArguments`. They currently match for codec, preset, CRF, audio, raw H.264 input options, progress, and MP4 faststart, but duplication creates drift risk.
- Normal Full and trimmed Full intentionally differ in `setpts`: normal uses `N/(fps*TB)` while trimmed uses `PTS-STARTPTS`. This is expected but should remain covered by tests.
- Trimmed Full places `-ss` and `-t` after `-i`. That is behaviorally important and could affect speed.
- Timestamp burn-in changes the filter chain and uses recording-time metadata. It also changes split non-trim Full behavior: split burn-in Full runs through a full-range trimmed split encode path, while split non-burn Full is blocked.
- FPS handling for `29.97` is consistently converted to `30000/1001` in manual FPS, auto-detect decisions, `-r`, and filters based on the current tests.
- MP4 and MKV Full differ only by MP4 `-movflags +faststart` in encode command generation. Metadata can be present for both depending on queue/single-file context.
- Progress parsing exposes frame, speed, and output time but not FFmpeg progress `fps`, even if FFmpeg writes it.
- The UI logs full FFmpeg stdout/stderr only for failed/canceled/timed-out conversions, so successful x264 summary lines may be captured in memory but not normally visible in the technical log.
- Tests cover the core generated Full and trimmed Full FFmpeg arguments, 29.97 mapping, MP4/MKV faststart differences, burn-in drawtext, missing-font handling, and trimmed temp/pre-roll behavior. They do not appear to assert the entire exact Full encode argument list the same way the MP4 remux test does.
- `docs/TECHNICAL_NOTES.md` matches the broad behavior but does not document trim/burn-in Full argument differences in detail.

## 8. Recommended Next Step

Recommended next Codex task: add narrowly scoped tests that assert the complete exact argument lists for Full MP4, Full MKV, trimmed Full MP4, trimmed Full MKV, and burn-in variants, then separately decide which performance metrics to capture before changing any encode settings.
