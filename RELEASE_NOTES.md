# Release Notes

## v0.2.0

DAT Converter v0.2.0 is the first queue-capable portable build.

### Core Conversion

- Converts compatible raw H.264 `.dat` video payloads to MP4 or MKV.
- Supports Remux and Encode modes.
- Provides FPS options including 29.97, which uses `30000/1001` internally.
- Uses bundled FFmpeg tools only from `tools\ffmpeg`.
- Runs raw H.264 probe validation before conversion.

### Queue Features

- Add multiple files with **Add Files**.
- Add folders safely with **Add Folder**.
- Folder scans are selected-folder only by default.
- Including subfolders requires explicit confirmation.
- Queue processing is sequential: one file at a time.
- Files and folders can be added while the queue is running.
- **Stop After Current** lets the active item finish before stopping the queue.
- **Cancel** cancels the current item and stops the queue.
- Queue and folder scans are capped at 100 `.dat` files for safety.

### Safety

- Source `.dat` files are never modified, renamed, deleted, or opened for writing.
- Existing outputs are never overwritten.
- Existing `.partial` files are never overwritten.
- Files with direct matching MP4/MKV output are skipped by default.
- Output conflicts use safe alternate names.
- Technical details are hidden by default and available through **Show Details** / **Copy Log**.

### Known Limitations

- Supports compatible raw H.264 `.dat` payloads only.
- Not a general video converter.
- No parallel processing.
- No `.sef` / `.sef2` parsing.
- Raw H.264 duration may be unavailable, so progress can be indeterminate.
- FFmpeg license/readme/notice files must stay bundled with the matching FFmpeg build.
