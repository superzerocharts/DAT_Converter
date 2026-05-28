param(
    [Parameter(Mandatory = $true)]
    [string]$InputDat,

    [Parameter(Mandatory = $true)]
    [string]$OutputFolder,

    [string]$Fps = "30",

    [ValidateRange(1, 100)]
    [int]$Iterations = 1,

    [switch]$DeleteOutputs,

    [string]$CameraName = "Benchmark Camera",

    [string]$BurnDate = "05/28/26",

    [string]$BurnTime = "12:00:00",

    [ValidateRange(1, 86400)]
    [int]$MaxDurationSeconds = 300,

    [string]$DeviceId = $env:COMPUTERNAME,

    [string]$ResultsFolder = "",

    [switch]$IncludeNvenc
)

$ErrorActionPreference = "Stop"

function Resolve-FpsValue {
    param([string]$Value)

    if ($Value -eq "29.97") {
        return "30000/1001"
    }

    return $Value
}

function Resolve-FpsExpression {
    param([string]$Value)

    if ($Value -eq "30000/1001") {
        return "($Value)"
    }

    return $Value
}

function Escape-DrawText {
    param([string]$Value)

    if ([string]::IsNullOrWhiteSpace($Value)) {
        return "Benchmark"
    }

    return $Value.Trim().
        Replace("\", "\\").
        Replace(":", "\:").
        Replace("'", "\'").
        Replace("%", "\%")
}

function Escape-FontFile {
    param([string]$Value)

    return $Value.Replace("\", "/").Replace(":", "\:").Replace("'", "\'")
}

function Resolve-FontFileOption {
    $candidates = @(
        "C:\Windows\Fonts\consolab.ttf",
        "C:\Windows\Fonts\consola.ttf",
        "C:\Windows\Fonts\arialbd.ttf"
    )

    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath $candidate -PathType Leaf) {
            return "fontfile='$(Escape-FontFile $candidate)':"
        }
    }

    return ""
}

function Get-FirstMatchingLine {
    param(
        [string[]]$Lines,
        [string]$Pattern
    )

    foreach ($line in $Lines) {
        if ($line -match $Pattern) {
            return $line.Trim()
        }
    }

    return ""
}

function Get-WarningSummary {
    param([string[]]$Lines)

    $matches = @($Lines | Where-Object {
        $_ -match "(?i)(warning|error|invalid|corrupt|failed|deprecated)"
    } | Select-Object -First 8)

    return ($matches -join " | ")
}

function Quote-ProcessArgument {
    param([string]$Value)

    if ($null -eq $Value) {
        return '""'
    }

    if ($Value -notmatch '[\s"]') {
        return $Value
    }

    return '"' + $Value.Replace('"', '\"') + '"'
}

function Get-FileLength {
    param([string]$Path)

    if (Test-Path -LiteralPath $Path -PathType Leaf) {
        return (Get-Item -LiteralPath $Path).Length
    }

    return $null
}

function Convert-ProgressTime {
    param(
        [string]$OutTimeUs,
        [string]$OutTimeMs,
        [string]$OutTime
    )

    $raw = if (-not [string]::IsNullOrWhiteSpace($OutTimeUs)) {
        $OutTimeUs
    } elseif (-not [string]::IsNullOrWhiteSpace($OutTimeMs)) {
        $OutTimeMs
    } else {
        ""
    }

    $longValue = 0L
    if ([long]::TryParse($raw, [ref]$longValue) -and $longValue -ge 0) {
        return [TimeSpan]::FromMilliseconds($longValue / 1000.0)
    }

    $timeValue = [TimeSpan]::Zero
    if ([TimeSpan]::TryParse($OutTime, [ref]$timeValue)) {
        return $timeValue
    }

    return $null
}

function Invoke-BenchmarkCase {
    param(
        [string]$FfmpegPath,
        [string]$InputPath,
        [string]$OutputPath,
        [string]$FpsLabel,
        [string]$FpsValue,
        [string]$CaseName,
        [bool]$BurnIn,
        [string]$Encoder,
        [string]$Preset,
        [string]$Crf,
        [string]$Camera,
        [string]$DateText,
        [string]$TimeText,
        [int]$MaxDurationSeconds
    )

    $fpsExpression = Resolve-FpsExpression $FpsValue
    $filter = "setpts=N/($fpsExpression*TB),fps=$FpsValue,format=yuv420p"
    if ($BurnIn) {
        $fontFileOption = Resolve-FontFileOption
        $filter += ",drawtext=${fontFileOption}text='$(Escape-DrawText $Camera)':x=20:y=20:fontcolor=white:fontsize=28"
        $filter += ",drawtext=${fontFileOption}text='$(Escape-DrawText $DateText)':x=20:y=54:fontcolor=white:fontsize=28"
        $filter += ",drawtext=${fontFileOption}text='$(Escape-DrawText $TimeText)':x=20:y=88:fontcolor=white:fontsize=28"
    }

    $encoderArguments = if ($Encoder -eq "h264_nvenc") {
        @("-c:v", $Encoder, "-preset", $Preset, "-cq", $Crf, "-b:v", "0")
    } else {
        @("-c:v", $Encoder, "-preset", $Preset, "-crf", $Crf)
    }

    $arguments = @(
        "-n",
        "-nostats",
        "-progress", "pipe:1",
        "-fflags", "+genpts+discardcorrupt",
        "-err_detect", "ignore_err",
        "-f", "h264",
        "-r", $FpsValue,
        "-i", $InputPath,
        "-t", $MaxDurationSeconds.ToString([System.Globalization.CultureInfo]::InvariantCulture),
        "-vf", $filter,
        "-an"
    ) + $encoderArguments + @(
        "-movflags", "+faststart",
        $OutputPath
    )

    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $FfmpegPath
    $startInfo.UseShellExecute = $false
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.CreateNoWindow = $true
    $startInfo.Arguments = ($arguments | ForEach-Object { Quote-ProcessArgument $_ }) -join " "

    $process = [System.Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    $progress = @{}
    $stopwatch = [System.Diagnostics.Stopwatch]::StartNew()

    [void]$process.Start()
    $stdoutTask = $process.StandardOutput.ReadToEndAsync()
    $stderrTask = $process.StandardError.ReadToEndAsync()
    $process.WaitForExit()
    $stdoutTask.Wait()
    $stderrTask.Wait()
    $stopwatch.Stop()

    $stdout = @($stdoutTask.Result -split "\r?\n" | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    $stderr = @($stderrTask.Result -split "\r?\n" | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    foreach ($line in $stdout) {
        $separator = $line.IndexOf("=")
        if ($separator -gt 0) {
            $progress[$line.Substring(0, $separator).Trim()] = $line.Substring($separator + 1).Trim()
        }
    }

    $inputSize = Get-FileLength $InputPath
    $outputSize = Get-FileLength $OutputPath
    $outTime = Convert-ProgressTime $progress["out_time_us"] $progress["out_time_ms"] $progress["out_time"]
    $outSeconds = if ($null -ne $outTime -and $outTime.TotalSeconds -gt 0) { $outTime.TotalSeconds } else { $null }
    $averageSpeed = if ($null -ne $outSeconds -and $stopwatch.Elapsed.TotalSeconds -gt 0) {
        $outSeconds / $stopwatch.Elapsed.TotalSeconds
    } else {
        $null
    }
    $compressionRatio = if ($null -ne $inputSize -and $inputSize -gt 0 -and $null -ne $outputSize) {
        $outputSize / [double]$inputSize
    } else {
        $null
    }

    $stderrLines = @($stderr)
    [pscustomobject]@{
        Timestamp = (Get-Date).ToString("o")
        CaseName = $CaseName
        BurnInEnabled = $BurnIn
        Encoder = $Encoder
        Preset = $Preset
        Crf = $Crf
        InputFilePath = $InputPath
        InputFileSizeBytes = $inputSize
        OutputFilePath = $OutputPath
        OutputFileSizeBytes = $outputSize
        CompressionRatio = $compressionRatio
        OutputContainer = "MP4"
        SelectedFpsLabel = $FpsLabel
        SelectedFfmpegFpsValue = $FpsValue
        ElapsedSeconds = $stopwatch.Elapsed.TotalSeconds
        ExitCode = $process.ExitCode
        Success = ($process.ExitCode -eq 0 -and $null -ne $outputSize -and $outputSize -gt 0)
        FinalReportedSpeed = $progress["speed"]
        FinalReportedFps = $progress["fps"]
        FinalFrameCount = $progress["frame"]
        FinalBitrate = $progress["bitrate"]
        FinalTotalSize = $progress["total_size"]
        DupFrames = $progress["dup_frames"]
        DropFrames = $progress["drop_frames"]
        FinalOutTime = if ($null -ne $outTime) { $outTime.ToString() } else { "" }
        FinalOutTimeUs = $progress["out_time_us"]
        FinalOutTimeMs = $progress["out_time_ms"]
        AverageEncodeSpeed = $averageSpeed
        StderrWarningErrorSummary = Get-WarningSummary $stderrLines
        FfmpegVersionLine = Get-FirstMatchingLine $stderrLines "^ffmpeg version "
        FfmpegBuildConfigLine = Get-FirstMatchingLine $stderrLines "^configuration:"
        CommandLine = "`"$FfmpegPath`" " + (($arguments | ForEach-Object { if ($_ -match "\s") { "`"$_`"" } else { $_ } }) -join " ")
    }
}

function Test-NvencAvailable {
    param([string]$FfmpegPath)

    $previousErrorActionPreference = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    try {
        $encoders = & $FfmpegPath -hide_banner -encoders 2>&1
        if (($encoders -join "`n") -notmatch "h264_nvenc") {
            return $false
        }

        $runtime = & $FfmpegPath -hide_banner -f lavfi -i "color=size=16x16:rate=1:duration=0.1" -frames:v 1 -c:v h264_nvenc -preset p1 -cq 23 -b:v 0 -f null NUL 2>&1
        return $LASTEXITCODE -eq 0
    }
    finally {
        $ErrorActionPreference = $previousErrorActionPreference
    }
}

function New-SkippedBenchmarkRow {
    param(
        [hashtable]$Case,
        [int]$Iteration,
        [string]$InputPath,
        [string]$OutputPath,
        [string]$FpsLabel,
        [string]$FpsValue,
        [string]$Reason
    )

    [pscustomobject]@{
        Timestamp = (Get-Date).ToString("o")
        CaseName = $Case["CaseName"]
        BurnInEnabled = [bool]$Case["BurnIn"]
        Encoder = $Case["Encoder"]
        Preset = $Case["Preset"]
        Crf = $Case["Crf"]
        InputFilePath = $InputPath
        InputFileSizeBytes = Get-FileLength $InputPath
        OutputFilePath = $OutputPath
        OutputFileSizeBytes = $null
        CompressionRatio = $null
        OutputContainer = "MP4"
        SelectedFpsLabel = $FpsLabel
        SelectedFfmpegFpsValue = $FpsValue
        ElapsedSeconds = $null
        ExitCode = $null
        Success = $false
        FinalReportedSpeed = ""
        FinalReportedFps = ""
        FinalFrameCount = ""
        FinalBitrate = ""
        FinalTotalSize = ""
        DupFrames = ""
        DropFrames = ""
        FinalOutTime = ""
        FinalOutTimeUs = ""
        FinalOutTimeMs = ""
        AverageEncodeSpeed = $null
        StderrWarningErrorSummary = $Reason
        FfmpegVersionLine = ""
        FfmpegBuildConfigLine = ""
        CommandLine = "Skipped: $Reason"
    }
}

function Convert-ToCsvValue {
    param($Value)

    if ($null -eq $Value) {
        return ""
    }

    return [string]$Value
}

function Write-MarkdownSummary {
    param(
        [string]$Path,
        [object[]]$Rows,
        [hashtable]$MachineInfo
    )

    $lines = [System.Collections.Generic.List[string]]::new()
    $lines.Add("# Full Encode Benchmark Results")
    $lines.Add("")
    $lines.Add("Generated: $(Get-Date -Format o)")
    $lines.Add("")
    $lines.Add("## Machine")
    $lines.Add("")
    $lines.Add("- Device ID: $($MachineInfo.DeviceId)")
    $lines.Add("- Machine: $($MachineInfo.MachineName)")
    $lines.Add("- OS: $($MachineInfo.OsVersion)")
    $lines.Add("- CPU: $($MachineInfo.CpuName)")
    $lines.Add("- Logical processors: $($MachineInfo.LogicalProcessorCount)")
    $lines.Add("- GPU: $($MachineInfo.GpuNames)")
    $lines.Add("- FFmpeg: $($MachineInfo.FfmpegPath)")
    $lines.Add("- FFmpeg version: $($MachineInfo.FfmpegVersionLine)")
    $lines.Add("")
    $lines.Add("## Comparison")
    $lines.Add("")
    $lines.Add("| Device | Case | Iteration | Burn-in | Preset | Success | Elapsed s | Avg speed | FFmpeg speed | FPS | Frames | Bitrate | Output bytes | Ratio | Dup | Drop |")
    $lines.Add("|---|---|---:|---|---|---|---:|---:|---|---:|---:|---|---:|---:|---:|---:|")

    foreach ($row in $Rows) {
        $lines.Add("| $($row.DeviceId) | $($row.CaseName) | $($row.Iteration) | $($row.BurnInEnabled) | $($row.Preset) | $($row.Success) | $("{0:0.###}" -f $row.ElapsedSeconds) | $("{0:0.###}" -f $row.AverageEncodeSpeed) | $($row.FinalReportedSpeed) | $($row.FinalReportedFps) | $($row.FinalFrameCount) | $($row.FinalBitrate) | $($row.OutputFileSizeBytes) | $("{0:0.###}" -f $row.CompressionRatio) | $($row.DupFrames) | $($row.DropFrames) |")
    }

    $lines.Add("")
    $lines.Add("## Notes")
    $lines.Add("")
    $lines.Add("- This developer-only runner uses fixed burn-in text by default. It measures drawtext cost, not recording metadata lookup.")
    $lines.Add("- Production app Full mode is not invoked or modified by this script.")
    $lines.Add("- Outputs use unique benchmark names and are not overwritten.")

    Set-Content -LiteralPath $Path -Value $lines -Encoding UTF8
}

function Write-AggregateMarkdownSummary {
    param(
        [string]$Path,
        [object[]]$Rows
    )

    $lines = [System.Collections.Generic.List[string]]::new()
    $lines.Add("# Full Encode Benchmark Results")
    $lines.Add("")
    $lines.Add("Generated: $(Get-Date -Format o)")
    $lines.Add("")
    $lines.Add('This file is regenerated from `benchmark_results.csv`. Keep generated MP4 outputs out of Git.')
    $lines.Add("")
    $lines.Add("## Comparison")
    $lines.Add("")
    $lines.Add("| Device | Timestamp | Case | Iteration | Burn-in | Preset | Success | Elapsed s | Avg speed | FFmpeg speed | FPS | Frames | Bitrate | Output bytes | Ratio | Dup | Drop |")
    $lines.Add("|---|---|---|---:|---|---|---|---:|---:|---|---:|---:|---|---:|---:|---:|---:|")

    foreach ($row in $Rows) {
        $lines.Add("| $($row.DeviceId) | $($row.Timestamp) | $($row.CaseName) | $($row.Iteration) | $($row.BurnInEnabled) | $($row.Preset) | $($row.Success) | $(Format-NumberForMarkdown $row.ElapsedSeconds) | $(Format-NumberForMarkdown $row.AverageEncodeSpeed) | $($row.FinalReportedSpeed) | $($row.FinalReportedFps) | $($row.FinalFrameCount) | $($row.FinalBitrate) | $($row.OutputFileSizeBytes) | $(Format-NumberForMarkdown $row.CompressionRatio) | $($row.DupFrames) | $($row.DropFrames) |")
    }

    Set-Content -LiteralPath $Path -Value $lines -Encoding UTF8
}

function Format-NumberForMarkdown {
    param($Value)

    $number = 0.0
    if ($null -ne $Value -and [double]::TryParse([string]$Value, [ref]$number)) {
        return $number.ToString("0.###", [System.Globalization.CultureInfo]::InvariantCulture)
    }

    return ""
}

function Append-CsvRows {
    param(
        [string]$Path,
        [object[]]$Rows
    )

    if (Test-Path -LiteralPath $Path -PathType Leaf) {
        $Rows | ConvertTo-Csv -NoTypeInformation | Select-Object -Skip 1 | Add-Content -LiteralPath $Path -Encoding UTF8
    }
    else {
        $Rows | Export-Csv -LiteralPath $Path -NoTypeInformation -Encoding UTF8
    }
}

$resolvedInput = (Resolve-Path -LiteralPath $InputDat).Path
if (-not (Test-Path -LiteralPath $resolvedInput -PathType Leaf)) {
    throw "Input .dat file does not exist: $InputDat"
}

if ([System.IO.Path]::GetExtension($resolvedInput) -ne ".dat") {
    throw "Input must be a .dat file: $resolvedInput"
}

$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot "..\..")).Path
$ffmpegPath = Join-Path $repoRoot "tools\ffmpeg\ffmpeg.exe"
if (-not (Test-Path -LiteralPath $ffmpegPath -PathType Leaf)) {
    throw "Bundled FFmpeg was not found: $ffmpegPath"
}

if (-not (Test-Path -LiteralPath $OutputFolder -PathType Container)) {
    [void](New-Item -ItemType Directory -Path $OutputFolder)
}

$resolvedOutputFolder = (Resolve-Path -LiteralPath $OutputFolder).Path
if ([string]::IsNullOrWhiteSpace($ResultsFolder)) {
    $ResultsFolder = Join-Path $repoRoot "docs\benchmarks"
}

if (-not (Test-Path -LiteralPath $ResultsFolder -PathType Container)) {
    [void](New-Item -ItemType Directory -Path $ResultsFolder)
}

$resolvedResultsFolder = (Resolve-Path -LiteralPath $ResultsFolder).Path
$fpsValue = Resolve-FpsValue $Fps
$runId = Get-Date -Format "yyyyMMdd-HHmmss"
$baseName = [System.IO.Path]::GetFileNameWithoutExtension($resolvedInput)
$createdOutputs = [System.Collections.Generic.List[string]]::new()

$ffmpegVersionOutput = & $ffmpegPath -version 2>&1
$cpu = Get-CimInstance Win32_Processor | Select-Object -First 1
$gpus = @(Get-CimInstance Win32_VideoController | ForEach-Object { $_.Name } | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
$machineInfo = @{
    DeviceId = $DeviceId
    MachineName = $env:COMPUTERNAME
    OsVersion = [System.Environment]::OSVersion.VersionString
    CpuName = if ($cpu) { $cpu.Name } else { "" }
    LogicalProcessorCount = [System.Environment]::ProcessorCount
    GpuNames = if ($gpus.Count -gt 0) { $gpus -join "; " } else { "" }
    FfmpegPath = $ffmpegPath
    FfmpegVersionLine = ($ffmpegVersionOutput | Select-Object -First 1)
    FfmpegBuildConfigLine = (($ffmpegVersionOutput | Where-Object { $_ -match "^configuration:" }) | Select-Object -First 1)
}

$cases = @(
    @{ CaseName = "Full MP4 baseline"; BurnIn = $false; Encoder = "libx264"; Preset = "veryfast"; Crf = "22" },
    @{ CaseName = "Full MP4 burn-in baseline"; BurnIn = $true; Encoder = "libx264"; Preset = "veryfast"; Crf = "22" },
    @{ CaseName = "Full MP4 burn-in superfast"; BurnIn = $true; Encoder = "libx264"; Preset = "superfast"; Crf = "22" },
    @{ CaseName = "Full MP4 burn-in ultrafast"; BurnIn = $true; Encoder = "libx264"; Preset = "ultrafast"; Crf = "22" }
)

$nvencAvailable = $false
if ($IncludeNvenc) {
    $nvencAvailable = Test-NvencAvailable $ffmpegPath
    $cases += @{ CaseName = "Full MP4 burn-in NVENC"; BurnIn = $true; Encoder = "h264_nvenc"; Preset = "p1"; Crf = "23" }
}

$rows = [System.Collections.Generic.List[object]]::new()
for ($iteration = 1; $iteration -le $Iterations; $iteration++) {
    foreach ($case in $cases) {
        $caseName = $case["CaseName"]
        $burnIn = [bool]$case["BurnIn"]
        $encoder = $case["Encoder"]
        $preset = $case["Preset"]
        $crf = $case["Crf"]
        $safeCaseName = ($caseName -replace "[^A-Za-z0-9]+", "-").Trim("-").ToLowerInvariant()
        $outputPath = Join-Path $resolvedOutputFolder "$baseName.$runId.iter$iteration.$safeCaseName.mp4"
        $result = if ($encoder -eq "h264_nvenc" -and -not $nvencAvailable) {
            New-SkippedBenchmarkRow -Case $case -Iteration $iteration -InputPath $resolvedInput -OutputPath $outputPath -FpsLabel $Fps -FpsValue $fpsValue -Reason "NVENC unavailable on this machine."
        } else {
            Invoke-BenchmarkCase `
                -FfmpegPath $ffmpegPath `
                -InputPath $resolvedInput `
                -OutputPath $outputPath `
                -FpsLabel $Fps `
                -FpsValue $fpsValue `
                -CaseName $caseName `
                -BurnIn $burnIn `
                -Encoder $encoder `
                -Preset $preset `
                -Crf $crf `
                -Camera $CameraName `
                -DateText $BurnDate `
                -TimeText $BurnTime `
                -MaxDurationSeconds $MaxDurationSeconds
        }

        $result | Add-Member -NotePropertyName Iteration -NotePropertyValue $iteration
        $result | Add-Member -NotePropertyName DeviceId -NotePropertyValue $machineInfo.DeviceId
        $result | Add-Member -NotePropertyName MachineName -NotePropertyValue $machineInfo.MachineName
        $result | Add-Member -NotePropertyName OsVersion -NotePropertyValue $machineInfo.OsVersion
        $result | Add-Member -NotePropertyName CpuName -NotePropertyValue $machineInfo.CpuName
        $result | Add-Member -NotePropertyName LogicalProcessorCount -NotePropertyValue $machineInfo.LogicalProcessorCount
        $result | Add-Member -NotePropertyName GpuNames -NotePropertyValue $machineInfo.GpuNames
        $result | Add-Member -NotePropertyName FfmpegPath -NotePropertyValue $machineInfo.FfmpegPath
        if ([string]::IsNullOrWhiteSpace($result.FfmpegVersionLine)) {
            $result.FfmpegVersionLine = $machineInfo.FfmpegVersionLine
        }
        if ([string]::IsNullOrWhiteSpace($result.FfmpegBuildConfigLine)) {
            $result.FfmpegBuildConfigLine = $machineInfo.FfmpegBuildConfigLine
        }

        $rows.Add($result)
        if (Test-Path -LiteralPath $outputPath -PathType Leaf) {
            $createdOutputs.Add($outputPath)
        }

        Write-Host ("[{0}/{1}] {2}: success={3}, elapsed={4:0.###}s, speed={5}" -f $rows.Count, ($Iterations * $cases.Count), $caseName, $result.Success, $result.ElapsedSeconds, $result.FinalReportedSpeed)
    }
}

$csvPath = Join-Path $resolvedOutputFolder "benchmark_results.csv"
$mdPath = Join-Path $resolvedOutputFolder "benchmark_results.md"
$rows | Export-Csv -LiteralPath $csvPath -NoTypeInformation -Encoding UTF8
Write-MarkdownSummary -Path $mdPath -Rows $rows.ToArray() -MachineInfo $machineInfo

$aggregateCsvPath = Join-Path $resolvedResultsFolder "benchmark_results.csv"
$aggregateMdPath = Join-Path $resolvedResultsFolder "benchmark_results.md"
Append-CsvRows -Path $aggregateCsvPath -Rows $rows.ToArray()
$aggregateRows = @(Import-Csv -LiteralPath $aggregateCsvPath)
Write-AggregateMarkdownSummary -Path $aggregateMdPath -Rows $aggregateRows

if ($DeleteOutputs) {
    foreach ($path in $createdOutputs) {
        if ((Test-Path -LiteralPath $path -PathType Leaf) -and ([System.IO.Path]::GetFileName($path) -like "$baseName.$runId.iter*.mp4")) {
            Remove-Item -LiteralPath $path -Force
        }
    }
}

Write-Host "Benchmark CSV: $csvPath"
Write-Host "Benchmark Markdown: $mdPath"
Write-Host "Tracked aggregate CSV: $aggregateCsvPath"
Write-Host "Tracked aggregate Markdown: $aggregateMdPath"
