# Release Notes

## v0.1.1-rc1

DAT Converter v0.1.1-rc1 is the first release-candidate package for final manual playback/conversion QA.

### Package

- Package: `DAT_Converter_Portable.zip`
- SHA-256: `C11E1910EA06258A40EC8F45E0BB35B47F5FDA12C241C94625C51F4989249BC6`
- Portable app includes `DatConverter.exe`, bundled `ffmpeg.exe` / `ffprobe.exe`, FFmpeg license/readme files, README, and third-party notices.
- The app uses bundled FFmpeg tools from `tools\ffmpeg` and does not depend on system `PATH`.
- Created by Schizm Studios.

### QA Summary

- Build passed.
- Tests passed: 467 passed.
- Portable publish succeeded with `scripts\publish-portable.ps1`.
- Portable launch smoke passed.
- Package audit confirmed required runtime files are present and source/test/dev artifacts are excluded.

### Storyboard Support

- Storyboard folder import can add clips separately or merge them into one combined output.
- Storyboard merge uses storyboard-order trim planning, mixed-camera segment handling, mixed 20/30 FPS source clip normalization, segment ffprobe validation, and explicit final merge inputs.
- Known limitation: storyboard merge uses Full encoding and can take longer.

### Final Gate

- Manual playback/conversion QA remains the final human gate before promoting RC1.

## v0.2.0

DAT Converter v0.2.0 is the first queue-capable portable build.

### Core Conversion

- Converts compatible raw H.264 `.dat` video payloads to MP4 or MKV.
- Supports Fast and Full modes.
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
- Files with existing output paths are marked Exists until the user chooses a new Save As path.
- Technical log information is hidden by default and available through **Show Log** / **Copy Log**.

### Known Limitations

- Supports compatible raw H.264 `.dat` payloads only.
- Not a general video converter.
- No parallel processing.
- No `.sef` / `.sef2` parsing.
- Raw H.264 duration may be unavailable, so progress can be indeterminate.
- FFmpeg license/readme/notice files must stay bundled with the matching FFmpeg build.
