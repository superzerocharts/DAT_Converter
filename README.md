# DAT Converter

DAT Converter is a focused Windows app for converting compatible `.dat` video files to MP4 or MKV.

It is not a general video converter. It only accepts `.dat` files that contain compatible H.264 video.

## Requirements

- Windows
- `ffmpeg.exe` and `ffprobe.exe` under:

```text
tools\ffmpeg\
```

Keep `DatConverter.exe` inside the extracted `DAT_Converter_Portable` folder. To place it on the desktop, create a shortcut to `DatConverter.exe`.

## Basic Usage

1. Add `.dat` files with **Add Files** or **Add Folder**.
2. Choose where converted files should be written.
3. Choose MP4 or MKV.
4. Choose a mode:
   - **Fast**: quickest option; preserves original quality.
   - **Full**: slower option; may help if Fast output has playback issues.
5. Choose the source FPS.
6. Click **Start Queue**.

The queue processes one file at a time. You can add more files while the queue is running.

## Notes

- Source `.dat` files are never modified, overwritten, renamed, or deleted.
- Existing output files are never overwritten.
- If an output file already exists, the row is marked **Exists**.
- Double-click a Ready or Exists row to change the save location or filename.
- Failed or canceled output files may be renamed with a `.partial` suffix for troubleshooting.
- The queue is limited to 100 files.
- Non-video/helper `.dat` files are marked **Unsupported** and are not processed.
- Technical details are hidden by default. Use **Show Details** and **Copy Log** for troubleshooting.

## Queue Controls

- **Start Queue** processes queued files sequentially.
- **Cancel Current** cancels the current item.
- **Stop After Current** lets the current item finish, then stops before the next item.
- **Clear All** clears all non-running queue items.
- **Clear Completed** removes completed rows.
- **Open Output** is available after a successful conversion.

## Troubleshooting

### Required FFmpeg tools were not found

Make sure these files are next to `DatConverter.exe`:

```text
tools\ffmpeg\ffmpeg.exe
tools\ffmpeg\ffprobe.exe
```

### File could not be read

The selected `.dat` file may not be a compatible video file.

### Fast mode failed

Try **Full** mode.

### Full mode failed

The `.dat` file may be unsupported or corrupt.

### A player cannot open an output

Try VLC, Chrome/Edge, another Windows machine, or another device before assuming the converted file is bad.

## Portable Publishing

From the repository root:

```powershell
.\scripts\publish-portable.ps1
```

If PowerShell script execution is restricted:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\publish-portable.ps1
```

The portable output is created at:

```text
publish\DAT_Converter_Portable\
```
