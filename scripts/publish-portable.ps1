param(
    # Optional path under the repository publish folder. Defaults to publish\DAT_Converter_Portable.
    [string]$OutputPath = ""
)

$ErrorActionPreference = "Stop"

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Split-Path -Parent $scriptRoot
$projectPath = Join-Path $repoRoot "src\DatConverter\DatConverter.csproj"
$publishRoot = Join-Path $repoRoot "publish"
$stagingPath = Join-Path $publishRoot "_DatConverter_AppPublish"
$portablePath = if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    Join-Path $publishRoot "DAT_Converter_Portable"
} else {
    if ([System.IO.Path]::IsPathRooted($OutputPath)) { $OutputPath } else { Join-Path $repoRoot $OutputPath }
}

$dotnetPath = Join-Path $repoRoot ".dotnet\dotnet.exe"
if (!(Test-Path -LiteralPath $dotnetPath)) {
    $dotnetPath = "dotnet"
}

if (!(Test-Path -LiteralPath $projectPath)) {
    throw "Project file not found: $projectPath"
}

$resolvedPublishRoot = [System.IO.Path]::GetFullPath($publishRoot)
$resolvedStagingPath = [System.IO.Path]::GetFullPath($stagingPath)
$resolvedPortablePath = [System.IO.Path]::GetFullPath($portablePath)
$portableArchiveName = (Split-Path -Leaf $resolvedPortablePath) + ".zip"
$resolvedArchivePath = [System.IO.Path]::GetFullPath((Join-Path $resolvedPublishRoot $portableArchiveName))
$resolvedChecksumsPath = [System.IO.Path]::GetFullPath((Join-Path $resolvedPublishRoot "SHA256SUMS.txt"))
if (!$resolvedPortablePath.StartsWith($resolvedPublishRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to clean output outside publish folder: $resolvedPortablePath"
}
if (!$resolvedStagingPath.StartsWith($resolvedPublishRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to clean staging output outside publish folder: $resolvedStagingPath"
}
if (!$resolvedArchivePath.StartsWith($resolvedPublishRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to create archive outside publish folder: $resolvedArchivePath"
}
if (!$resolvedChecksumsPath.StartsWith($resolvedPublishRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to create checksum file outside publish folder: $resolvedChecksumsPath"
}

if (Test-Path -LiteralPath $resolvedStagingPath) {
    Remove-Item -LiteralPath $resolvedStagingPath -Recurse -Force
}

if (Test-Path -LiteralPath $resolvedPortablePath) {
    $runningFromPortable = Get-Process -ErrorAction SilentlyContinue |
        Where-Object {
            try {
                ![string]::IsNullOrWhiteSpace($_.Path) -and
                    [System.IO.Path]::GetFullPath($_.Path).StartsWith($resolvedPortablePath, [System.StringComparison]::OrdinalIgnoreCase)
            } catch {
                $false
            }
        } |
        Select-Object -First 1

    if ($null -ne $runningFromPortable) {
        throw "Cannot replace portable folder because DAT Converter is currently running from it. Close DatConverter.exe and rerun this script. PID: $($runningFromPortable.Id); Path: $($runningFromPortable.Path)"
    }
}

New-Item -ItemType Directory -Path $resolvedStagingPath -Force | Out-Null

& $dotnetPath publish $projectPath `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -o $resolvedStagingPath `
    /p:PublishSingleFile=true `
    /p:PublishTrimmed=false `
    /p:EnableCompressionInSingleFile=true `
    /p:DebugType=none `
    /p:DebugSymbols=false

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

if (Test-Path -LiteralPath $resolvedPortablePath) {
    Remove-Item -LiteralPath $resolvedPortablePath -Recurse -Force
}
if (Test-Path -LiteralPath $resolvedArchivePath) {
    Remove-Item -LiteralPath $resolvedArchivePath -Force
}
if (Test-Path -LiteralPath $resolvedChecksumsPath) {
    Remove-Item -LiteralPath $resolvedChecksumsPath -Force
}

New-Item -ItemType Directory -Path $resolvedPortablePath -Force | Out-Null
Get-ChildItem -LiteralPath $resolvedStagingPath -Recurse -File |
    Where-Object {
        $_.Extension -ne ".dat" -and
        $_.FullName -notmatch "\\test-assets\\"
    } |
    ForEach-Object {
        $sourceFullPath = [System.IO.Path]::GetFullPath($_.FullName)
        $relativePath = $sourceFullPath.Substring($resolvedStagingPath.Length).TrimStart('\')
        $destinationPath = Join-Path $resolvedPortablePath $relativePath
        $destinationDirectory = Split-Path -Parent $destinationPath
        New-Item -ItemType Directory -Path $destinationDirectory -Force | Out-Null
        Copy-Item -LiteralPath $_.FullName -Destination $destinationPath -Force
    }

$portableToolsPath = Join-Path $resolvedPortablePath "tools\ffmpeg"
New-Item -ItemType Directory -Path $portableToolsPath -Force | Out-Null

$repoToolsPath = Join-Path $repoRoot "tools\ffmpeg"
if (Test-Path -LiteralPath $repoToolsPath) {
    $repoToolsFullPath = [System.IO.Path]::GetFullPath($repoToolsPath).TrimEnd('\')
    Get-ChildItem -LiteralPath $repoToolsPath -Recurse -File |
        Where-Object { $_.Extension -ne ".dat" } |
        ForEach-Object {
            $sourceFullPath = [System.IO.Path]::GetFullPath($_.FullName)
            $relativePath = $sourceFullPath.Substring($repoToolsFullPath.Length).TrimStart('\')
            $destinationPath = Join-Path $portableToolsPath $relativePath
            $destinationDirectory = Split-Path -Parent $destinationPath
            New-Item -ItemType Directory -Path $destinationDirectory -Force | Out-Null
            Copy-Item -LiteralPath $_.FullName -Destination $destinationPath -Force
        }
}

$endUserReadmePath = Join-Path $repoRoot "README.txt"
$noticesPath = Join-Path $repoRoot "THIRD_PARTY_NOTICES.md"
$docsPath = Join-Path $resolvedPortablePath "docs"
New-Item -ItemType Directory -Path $docsPath -Force | Out-Null
if (Test-Path -LiteralPath $endUserReadmePath) {
    Copy-Item -LiteralPath $endUserReadmePath -Destination (Join-Path $docsPath "README.txt") -Force
}
if (Test-Path -LiteralPath $noticesPath) {
    Copy-Item -LiteralPath $noticesPath -Destination (Join-Path $docsPath "THIRD_PARTY_NOTICES.md") -Force
}

Compress-Archive -Path (Join-Path $resolvedPortablePath "*") -DestinationPath $resolvedArchivePath -Force
$archiveHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $resolvedArchivePath).Hash
Set-Content -LiteralPath $resolvedChecksumsPath -Value "$archiveHash  $portableArchiveName"

$ffmpegPath = Join-Path $portableToolsPath "ffmpeg.exe"
$ffprobePath = Join-Path $portableToolsPath "ffprobe.exe"
$hasFfmpeg = Test-Path -LiteralPath $ffmpegPath
$hasFfprobe = Test-Path -LiteralPath $ffprobePath

Write-Host ""
Write-Host "DAT Converter portable publish complete."
Write-Host "Output: $resolvedPortablePath"
Write-Host "Executable: $(Join-Path $resolvedPortablePath "DatConverter.exe")"
Write-Host "Archive: $resolvedArchivePath"
Write-Host "Checksums: $resolvedChecksumsPath"
Write-Host "App publish mode: compressed self-contained single-file executable, with external FFmpeg tools"

if ($hasFfmpeg -and $hasFfprobe) {
    Write-Host "FFmpeg tools: present"
} else {
    Write-Warning "FFmpeg tools are missing. Place ffmpeg.exe and ffprobe.exe under tools\ffmpeg before distribution."
    Write-Warning "Expected: $ffmpegPath"
    Write-Warning "Expected: $ffprobePath"
}

if (Test-Path -LiteralPath $resolvedStagingPath) {
    Remove-Item -LiteralPath $resolvedStagingPath -Recurse -Force
}

Write-Host "Excluded by design: test-assets, .dat samples, .dotnet, bin/obj, and source-control metadata."
