# DAT Converter

DAT Converter is a focused Windows desktop utility for converting compatible raw H.264 `.dat` video payloads to MP4 or MKV using bundled FFmpeg tools.

It is not a general video converter. It only accepts `.dat` files that can be interpreted as raw H.264 video payloads.

## Requirements

- Windows
- `ffmpeg.exe` and `ffprobe.exe` placed under:

```text
tools\ffmpeg\
```

The app only uses bundled tools relative to the application folder. It does not search the system `PATH` and does not use globally installed FFmpeg.

Keep `DatConverter.exe` inside the extracted `DAT_Converter_Portable` folder. Do not move the executable by itself, because it needs the neighboring `tools\ffmpeg` folder. To place DAT Converter on the desktop, create a shortcut to `DatConverter.exe` instead.

## Basic Usage

1. Add compatible `.dat` files.
   - Use **Add Files** to choose one or more specific files.
   - Use **Add Folder** to scan a selected folder only by default.
   - Including subfolders requires explicit confirmation.
   - Added files are validated with a quick raw H.264 probe before they are processed.
2. Choose where converted files should be written.
   - Same folder as source file is the default.
   - Choose output folder uses one shared destination folder.
3. Choose MP4 or MKV.
4. Choose Remux or Encode.
5. Choose the frame rate.
6. Start the queue.

The queue processes one file at a time. You can add more files or folders while the queue is running; newly added items are appended to the end of the queue and validated before they can be processed.

## Remux vs Encode

- Remux is faster and preserves the original video quality because it copies the H.264 stream into a new container.
- Encode is slower, but can be more tolerant of timing or playback issues because it rebuilds the video stream with H.264 settings.

## Notes

- Source `.dat` files are never modified, overwritten, renamed, or deleted.
- Source `.dat` files are opened for reading only and passed to FFmpeg as input.
- Converted files are written next to the source `.dat` by default.
- Existing output files are not overwritten. DAT Converter creates a safe alternate name when needed.
- Existing `.partial` files are not overwritten. Failed or canceled output files are renamed to the next available `.partial` name.
- Canceling or failing a conversion may leave a `.partial` output file for troubleshooting.
- The 29.97 FPS option uses `30000/1001` internally.
- User preferences are stored in `%LocalAppData%\DAT Converter\settings.json`. DAT Converter remembers the last chosen output folder and window size. Workflow controls always start at the safe defaults: same folder as source, MP4, Remux, and 30 FPS. It does not remember the last selected `.dat` file.
- The queue is limited to 100 files for safety.
- Queue items keep the settings they were added with. Changing Format, Mode, Source FPS, or output destination affects newly added files only.
- While the queue is running, files added with **Add Files** or **Add Folder** use the active queue settings captured when **Start Queue** was clicked. DAT Converter shows a warning before adding them.
- Non-video/helper `.dat` files are marked **Unsupported** during queue validation and are not processed.
- Files that already have the selected output format in the active output folder are shown as `Exists` and not processed by default. For example, an existing MP4 does not block creating MKV, and an existing MKV does not block creating MP4. You can uncheck that option to queue them with a safe non-overwriting output name.
- Add Folder scans only the selected folder by default. Including subfolders requires explicit confirmation.
- Folder scans stop and add nothing if more than 100 `.dat` files are found. Choose a smaller folder or manually add specific files.
- Technical details are hidden by default. Use **Show Details** when you need troubleshooting information, then use **Copy Log** to copy it.
- **Stop After Current** lets the active queue item finish normally, then leaves the remaining queue items pending. **Cancel** still stops the current FFmpeg process immediately.

## Queue Controls

- **Start Queue** processes queued files sequentially.
- **Cancel** cancels the current item, safely handles any partial output, and stops the active queue run.
- **Stop After Current** lets the current item finish normally, then stops before the next pending item starts.
- **Clear All** clears all non-running queue items.
- **Clear Completed** removes completed rows only.
- **Open Output Folder** is available after a successful conversion.

## Troubleshooting

### Required FFmpeg tools were not found

Make sure `ffmpeg.exe` and `ffprobe.exe` are included in `tools\ffmpeg\` next to `DatConverter.exe`.

If you moved only `DatConverter.exe` somewhere else, move it back into the extracted `DAT_Converter_Portable` folder or create a shortcut instead. The expected files are:

```text
tools\ffmpeg\ffmpeg.exe
tools\ffmpeg\ffprobe.exe
```

### File could not be interpreted as raw H.264

The selected `.dat` file may not be a compatible raw H.264 video payload.

### Remux failed

Try Encode mode. It is slower, but more tolerant of timing or bitstream issues.

### Encode failed

The `.dat` file may be unsupported or corrupt.

## Portable Publishing

From the repository root:

```powershell
.\scripts\publish-portable.ps1
```

If PowerShell script execution is restricted on the machine, use:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\publish-portable.ps1
```

The portable output is created at:

```text
publish\DAT_Converter_Portable\
```

Place FFmpeg binaries and license/attribution files under `tools\ffmpeg\` before running the script if you want them included in the portable folder.

The portable package keeps FFmpeg as external files under `tools\ffmpeg` for reliability and so the matching license/readme files remain visible. Documentation is copied into `docs\` in the portable package.
