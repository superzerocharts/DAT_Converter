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
- Run a live queue QA smoke test:
  - Add multiple `.dat` files.
  - Run Remux MP4/MKV.
  - Run Encode MP4/MKV where practical.
  - Verify Add Files/Add Folder while running.
  - Verify Stop After Current.
  - Verify Cancel Current and partial output handling.
  - Verify existing-output skip behavior.
  - Verify duplicate base-name output reservation in a chosen output folder.
  - Verify source `.dat` SHA-256 hashes are unchanged.

## Publish

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\publish-portable.ps1
```

- Verify `publish\DAT_Converter_Portable\DatConverter.exe` exists.
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
