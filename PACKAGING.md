# Packaging DAT Converter

Use the portable publish script from the repository root:

```powershell
.\scripts\publish-portable.ps1
```

If PowerShell script execution is restricted on the machine, use:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\publish-portable.ps1
```

The script publishes a self-contained Windows x64 Release build to:

```text
publish\DAT_Converter_Portable\
```

Expected portable structure:

```text
DAT_Converter_Portable\
  DatConverter.exe
  README.txt
  docs\
    THIRD_PARTY_NOTICES.md
  tools\
    ffmpeg\
      ffmpeg.exe
      ffprobe.exe
      [FFmpeg license/attribution files]
```

If `tools\ffmpeg\ffmpeg.exe` and `tools\ffmpeg\ffprobe.exe` are present in the repository root before publishing, the script copies them into the portable folder. If they are absent, the script leaves an instruction file in the portable `tools\ffmpeg` folder and prints a warning.

The app itself is published as a compressed self-contained single-file Windows executable. FFmpeg remains external under `tools\ffmpeg` for reliability and to keep the matching license/readme files visible.

Do not move `DatConverter.exe` by itself. Keep it in the extracted `DAT_Converter_Portable` folder with the neighboring `tools` folder. If you want DAT Converter on the desktop, create a shortcut to `DatConverter.exe` instead.

DAT Converter does not search the system `PATH` and does not use globally installed FFmpeg.

The script does not copy `test-assets/`, `.dat` samples, `.dotnet/`, source control folders, or build intermediates into the portable package.

The script is safe to rerun. It cleans and recreates only the selected portable folder under `publish\`.

## Release Verification Notes

Before publishing a release, verify the current queue workflow:

- **Add Files** supports multiple `.dat` files.
- **Add Folder** scans the selected folder only by default.
- Including subfolders requires explicit confirmation.
- Folder scans stop and add nothing if more than 100 `.dat` files are found.
- The queue is limited to 100 files and processes one file at a time.
- Files and folders can be added while the queue is running.
- **Stop After Current** lets the current item finish before stopping the queue.
- **Cancel** cancels the current item and stops the queue.
- Same-folder output is the default; **Choose output folder** uses one shared folder.
- Existing matching MP4/MKV outputs are skipped by default.
- Existing outputs and existing `.partial` files are never overwritten.
- Source `.dat` files are never modified, renamed, deleted, or opened for writing.
- Technical details are hidden by default and available with **Show Details** / **Copy Log**.

After publishing, inspect `publish\DAT_Converter_Portable` and the release ZIP to confirm they do not contain `.dat` files, `test-assets`, source files, `.dotnet`, `.git`, `bin`, `obj`, temporary videos, or temporary `.partial` files.
