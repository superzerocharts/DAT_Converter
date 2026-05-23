# DAT Converter — Full Project Source Instructions

## Project purpose

Build a standalone portable Windows desktop app for converting compatible surveillance-exported `.dat` video files into standard MP4 or MKV files using bundled FFmpeg tools.

The app is intended for non-technical Windows users and should have a simple, compact interface similar in spirit to Rufus: clear controls, minimal clutter, an obvious workflow, and a reliable status/progress area.

## Primary goal

Convert compatible raw H.264 `.dat` files into playable MP4 or MKV files without requiring users to install FFmpeg or configure the system `PATH`.

## Recommended tech stack

Use a native Windows desktop app, preferably:

- C# WinForms, or
- C# WPF

Publish as a self-contained .NET app where practical.

Prioritize:

- Reliability
- Simple portable packaging
- Responsive UI
- Clean FFmpeg process control
- Low maintenance burden

Avoid Python/PyInstaller, Electron, or Tauri unless there is a strong reason to change direction later.

## Hard scope

- Windows desktop app.
- Standalone/portable preferred.
- No system-wide FFmpeg installation required.
- FFmpeg and ffprobe must be bundled with the app and called by relative path.
- Only ingest `.dat` files.
- Do not support `.sef`, `.sef2`, `.mp4`, `.mkv`, `.avi`, or arbitrary video inputs in the main workflow.
- Source files are expected to be raw H.264 video payloads from compatible surveillance exports.
- Output format must be selectable:
  - MP4
  - MKV
- Conversion mode must be selectable:
  - Remux / stream copy
  - Encode / rebuild
- Remux should be the default mode.
- Encoding should be optional and user-selected.
- Frame rate must be user-selectable for input interpretation.
- The app must not modify, overwrite, or delete the source `.dat` file.
- The app is not a general video converter.

## Supported frame rates

The frame-rate selector should include common surveillance values from 15–30 FPS:

- 15
- 20
- 24
- 25
- 29.97
- 30

Default frame rate: **30 FPS**.

For 29.97 FPS, use `30000/1001` consistently in both FFmpeg `-r` and the video filter expression.

## Required UI

Use one compact main window.

Required controls:

- File selection button for one `.dat` file.
- Output folder selection button.
- Output format selector:
  - MP4
  - MKV
- Conversion mode selector:
  - Remux
  - Encode
- Frame-rate selector.
- Convert button.
- Cancel button.
- Progress bar.
- Status/log area.
- Open output folder button after successful conversion.

Default UI state:

- Output format: MP4
- Mode: Remux
- FPS: 30
- Convert disabled until input validation/probe succeeds.
- Cancel disabled until conversion is running.
- Open output folder disabled until successful conversion.

UI style:

- Simple compact desktop utility.
- Similar usability pattern to Rufus.
- One main window.
- Clear input/output selections.
- Simple dropdowns.
- Obvious Convert button.
- Progress/status at the bottom.
- Avoid clutter.
- Avoid advanced codec settings in the first version.
- Keep settings visible but minimal.

## Known proven FFmpeg findings

Prior testing established the following:

- `.sef2` itself was not readable by FFmpeg.
- The actual video payload was in `dvrfile00000001.dat`.
- Plain ffprobe against the `.dat` initially returned invalid data / low-confidence detection.
- Forcing raw H.264 interpretation worked.
- FFmpeg successfully detected:
  - codec: h264
  - profile: Baseline
  - resolution: 1920x1080
  - pixel format: yuvj420p
- Fast remux to MP4 worked when interpreting the raw H.264 input at 30 FPS.
- The output probed as:
  - `codec_name=h264`
  - `width=1920`
  - `height=1080`
  - `r_frame_rate=30/1`
  - `avg_frame_rate=30/1`
  - `duration=297.266667`
- VLC showed the output duration as 4:57, matching FFmpeg.
- Clean re-encode also worked and fixed timing issues.
- Earlier remux attempts without proper timestamp handling caused end-of-clip speed-up.
- The final fast remux sample played correctly.

## Core FFmpeg behavior

Use bundled FFmpeg by relative path. Do not require FFmpeg to be installed system-wide. Do not require users to modify `PATH`.

All conversions should treat compatible `.dat` files as raw H.264 input using:

```text
-f h264 -r SELECTED_FPS -i "INPUT.dat"
```

Use the selected FPS in place of `SELECTED_FPS`.

For 29.97 FPS, use:

```text
30000/1001
```

## Proven fast remux command — MP4

```bat
ffmpeg -y ^
  -fflags +genpts+discardcorrupt ^
  -err_detect ignore_err ^
  -f h264 -r SELECTED_FPS -i "INPUT.dat" ^
  -c:v copy ^
  -movflags +faststart ^
  "OUTPUT.mp4"
```

## Proven fast remux command — MKV

```bat
ffmpeg -y ^
  -fflags +genpts+discardcorrupt ^
  -err_detect ignore_err ^
  -f h264 -r SELECTED_FPS -i "INPUT.dat" ^
  -c:v copy ^
  "OUTPUT.mkv"
```

## Safe encode command — MP4

```bat
ffmpeg -y ^
  -fflags +genpts+discardcorrupt ^
  -err_detect ignore_err ^
  -f h264 -r SELECTED_FPS -i "INPUT.dat" ^
  -vf "setpts=N/(SELECTED_FPS*TB),fps=SELECTED_FPS,format=yuv420p" ^
  -an ^
  -c:v libx264 -preset veryfast -crf 22 ^
  -movflags +faststart ^
  "OUTPUT.mp4"
```

## Safe encode command — MKV

```bat
ffmpeg -y ^
  -fflags +genpts+discardcorrupt ^
  -err_detect ignore_err ^
  -f h264 -r SELECTED_FPS -i "INPUT.dat" ^
  -vf "setpts=N/(SELECTED_FPS*TB),fps=SELECTED_FPS,format=yuv420p" ^
  -an ^
  -c:v libx264 -preset veryfast -crf 22 ^
  "OUTPUT.mkv"
```

## Remux rules

- Remux is the default.
- Remux must use `-c:v copy`.
- Remux should not re-encode.
- Remux should be fast.
- Remux should preserve original video quality.
- If remux fails, suggest trying Encode mode.
- If remux output has playback/timing issues, suggest trying Encode mode.

## Encode rules

Encoding is optional and user-selected.

Use:

- `libx264`
- `-preset veryfast`
- `-crf 22`
- `format=yuv420p`
- `-an`

MP4 encode should also use:

- `-movflags +faststart`

Encoding is slower than remuxing but more robust for bad timing, damaged timestamps, or playback issues.

No audio is included by default because the tested compatible `.dat` files contain video only.

## Probe and validation requirements

When a `.dat` file is selected:

1. Confirm the extension is `.dat`.
2. Confirm the file exists.
3. Confirm the file is readable.
4. Confirm the file is not zero bytes.
5. Run a quick FFmpeg/ffprobe-based validation using forced raw H.264 interpretation.
6. Extract/display where possible:
   - codec
   - profile
   - width
   - height
   - selected FPS
   - approximate duration, if available

If the probe succeeds:

- Enable Convert.
- Show the detected/probed information in the UI.

If the probe fails:

- Disable Convert.
- Show this message:

```text
This .dat file could not be interpreted as raw H.264. It may not be a compatible video payload.
```

Probe behavior:

- Probe should be quick.
- Probe should run when the file is loaded.
- Probe should run again when relevant options change, especially selected FPS if it affects displayed assumptions.
- Do not rely on `.sef` or `.sef2` parsing in the first version.

## Progress bar requirements

- The progress bar should be as accurate as possible.
- Use FFmpeg progress output where feasible.
- Prefer `-progress pipe:1` with parsed key/value output.
- If reliable duration is available, calculate progress from `out_time_ms / duration`.
- If duration is unavailable for raw input, use a reasonable fallback:
  - indeterminate progress during conversion, or
  - estimated progress based on processed frame count if total frame count is known, or
  - estimated progress based on output/input size only if clearly treated as approximate internally.
- Do not show fake precision.
- Do not imply exact progress if the app only has an approximation.
- The UI must remain responsive during conversion.
- Conversion must run in a background process/thread.

## Cancel behavior

- Cancel should terminate the FFmpeg process.
- Canceled partial output should be removed or renamed with `.partial`.
- UI should return to a ready state.
- Status should clearly say conversion was canceled.
- Never delete the source `.dat` file.

## Output naming

Default output filename should be based on the input filename.

Examples:

```text
dvrfile00000001.dat -> dvrfile00000001.mp4
dvrfile00000001.dat -> dvrfile00000001.mkv
```

If output already exists, avoid overwriting by appending:

```text
_converted
_01
_02
```

Example sequence:

```text
dvrfile00000001.mp4
dvrfile00000001_converted.mp4
dvrfile00000001_01.mp4
dvrfile00000001_02.mp4
```

Do not overwrite existing files without explicit user action.

Failed or canceled partial output files should be deleted or renamed with `.partial`.

## Error handling

- If FFmpeg exits with non-zero code, show failure clearly.
- Preserve FFmpeg stdout/stderr in a technical log.
- Show a simplified user-facing error message in the UI.
- Include copyable or expandable technical details.
- If remux fails, suggest trying Encode mode.
- If encode fails, mark the file as unsupported or corrupt.
- Never delete source files.

Suggested user-facing outcomes:

### Remux failed

```text
Remux failed. This file may have timing or bitstream issues. Try Encode mode, which is slower but more tolerant.
```

### Encode failed

```text
Encode failed. This .dat file may be unsupported or corrupt.
```

### Missing FFmpeg tools

```text
Required FFmpeg tools were not found. Make sure ffmpeg.exe and ffprobe.exe are included in the tools folder.
```

### Unsupported file

```text
This .dat file could not be interpreted as raw H.264. It may not be a compatible video payload.
```

## FFmpeg bundling

The app package should include:

```text
/tools/ffmpeg/ffmpeg.exe
/tools/ffmpeg/ffprobe.exe
```

The app must verify these binaries exist at startup.

If missing:

- Show a clear startup error.
- Disable conversion.
- Do not fall back to system PATH unless explicitly requested later.

Distribution should include required FFmpeg license/attribution files.

## Packaging requirements

Package should include:

- App executable
- `ffmpeg.exe`
- `ffprobe.exe`
- Required FFmpeg license/attribution files
- Any required .NET runtime files if self-contained publishing is used

Preferred distribution:

- Portable folder, or
- Self-contained publish package

Do not require an installer unless explicitly requested later.

## Development workflow

All implementation work will be performed in Codex.

Project chats should output Codex-ready prompts in copyable text blocks.

Prompts should be incremental and focused.

Each Codex prompt should include:

- Goal
- Context
- Exact features/files to touch when known
- Constraints
- Acceptance criteria
- Test/build verification commands

Do not ask Codex to build everything in one giant step.

Preserve current behavior unless explicitly changing it.

## Preferred build phases

1. Create minimal C# WinForms/WPF app skeleton and UI.
2. Add bundled FFmpeg/ffprobe path detection.
3. Add `.dat` file selection and output folder selection.
4. Add probe/validation.
5. Add remux mode for MP4/MKV.
6. Add encode mode for MP4/MKV.
7. Add progress parsing and cancellation.
8. Add logging/error handling.
9. Add packaging/publish workflow.
10. Add final QA and documentation.

## Codex prompt style

When asked for implementation help, output prompts like this:

```text
Goal:
[Specific implementation goal]

Context:
[Relevant project context and current state]

Files/features to touch:
[List exact files if known. Otherwise state expected files/classes.]

Constraints:
- Preserve existing behavior unless explicitly changed.
- Keep the app focused on raw H.264 .dat conversion only.
- Do not add out-of-scope features.

Acceptance criteria:
- [Specific observable outcomes]
- [UI behavior]
- [Error handling]
- [Tests/build expectations]

Verification commands:
[dotnet build/test/publish commands or manual verification steps]
```

## Core rule

The app is not a general video converter. It is a focused raw H.264 `.dat` converter.

## Do not add unless explicitly requested

- Batch conversion
- Hot-folder watching
- `.sef` / `.sef2` parsing
- Arbitrary input formats
- Advanced codec tuning UI
- Cloud upload
- Source file deletion/archive workflow
- Database
- User accounts
- Required installer
