# Release Checklist

Use this checklist for each DAT Converter portable release.

## Prepare Tools

- Place `ffmpeg.exe` under `tools\ffmpeg`.
- Place `ffprobe.exe` under `tools\ffmpeg`.
- Include matching FFmpeg `LICENSE`, `README`, notice, and build-note files from the same FFmpeg download.
- Do not rely on system `PATH`.

## Build and QA

```powershell
.\.dotnet\dotnet.exe build .\DatConverter.sln
```

- Confirm the build has 0 errors and ideally 0 warnings.
- Run tests:

```powershell
.\.dotnet\dotnet.exe test .\DatConverter.sln
```

- Run a live queue QA smoke test:
  - Add multiple `.dat` files.
  - Run Fast mode MP4/MKV.
  - Run Full mode MP4/MKV where practical.
  - Verify Add Files/Add Folder while running.
  - Verify Stop After Current.
  - Verify Cancel Current and partial output handling.
  - Verify existing-output rows are marked Exists.
  - Verify Exists rows can be corrected with Save As.
  - Verify source `.dat` SHA-256 hashes are unchanged.
- Run MP4 Fast mode playback QA:
  - Convert the known sample to MP4 Fast mode.
  - Copy the output to a simple local path such as `C:\Temp\dat_converter_test.mp4`.
  - Open it in VLC.
  - Open it in Chrome/Edge or on another device when available.
  - Optionally open it in Microsoft Media Player / Windows Media Player, but first confirm that the same local player can open a known-good unrelated MP4.
  - Confirm duration/timing looks correct.
  - If Microsoft players fail for both DAT Converter output and known-good MP4 controls, treat that as a local Windows player/media-stack issue.
  - If only DAT Converter Fast output fails in otherwise healthy players, preserve the failed output, capture ffprobe output for the MP4, then test Full mode.

## Publish

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\publish-portable.ps1
```

- Verify `publish\DAT_Converter_Portable\DatConverter.exe` exists.
- Verify end-user `publish\DAT_Converter_Portable\README.txt` exists.
- Verify bundled FFmpeg is present under `publish\DAT_Converter_Portable\tools\ffmpeg`.
- Verify docs are present under `publish\DAT_Converter_Portable\docs`.
- Launch the portable app and confirm bundled FFmpeg detection.

## Package Hygiene

Confirm the portable folder and ZIP do not include:

- `.dat` files
- `test-assets`
- `.dotnet`
- `.git`
- source files
- `bin` / `obj`
- temporary output videos
- temporary `.partial` files

## Checksums and Source Control

- Create `publish\DAT_Converter_Portable.zip`.
- Generate SHA-256 for the ZIP and update `publish\SHA256SUMS.txt`.
- Review source-control status.
- Confirm these are not committed unless intentionally desired:
  - `tools\ffmpeg\ffmpeg.exe`
  - `tools\ffmpeg\ffprobe.exe`
  - `publish\`
  - `DAT_Converter_Portable.zip`
  - `test-assets\`
  - `.dat` files
  - `.dotnet\`
  - `bin\`
  - `obj\`
