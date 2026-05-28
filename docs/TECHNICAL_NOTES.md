# DAT Converter Technical Notes

## Purpose
DAT Converter is a focused Windows desktop utility for converting Spotter/Spotter `.dat` surveillance exports to MP4 or MKV using bundled FFmpeg tools. It is intentionally not a general video converter. The app assumes Spotter-style raw H.264 payloads and presents a small workflow for safe batch conversion.

## Scope Rules
- Input is limited to `.dat` files.
- Source `.dat` files must never be modified, renamed, deleted, or overwritten.
- The app does not accept arbitrary video formats as source input.
- `.sef` and `.sef2` sidecars may be discovered internally for metadata and FPS calibration, but they are not user-selected inputs.
- Manual FPS choices remain available even when Auto-detect is the default.
- `README.md` is end-user documentation and should stay simple. Put maintainer details in this document instead.

## High-Level Architecture
- `MainForm` owns the WinForms UI, user interaction, queue orchestration, selected-item details, and technical log display.
- `QueueItem` stores the per-row conversion plan and state, including input path, output path, format, mode, FPS resolution, probe result, conversion result, and custom override flags.
- Queue helper services keep logic testable:
  - `QueueItemRefreshService` refreshes editable items from live/global settings without clobbering custom fields.
  - `QueueSettingsLockService` applies locked queue defaults when processing starts.
  - `QueueProcessingEligibilityService`, `QueuePreProbeService`, `QueueRunningValidationService`, and `QueueFpsValidationService` define readiness and blocking rules.
- FPS detection is split between `SpotterFpsDetector`, `FpsDecisionPolicy`, `SpotterSidecarLookup`, and `QueueItemFpsResolver`.
- `ProbeService` validates raw H.264 compatibility using bundled `ffprobe.exe` and a short `ffmpeg.exe` fallback pass.
- `ConversionService` runs FFmpeg conversion commands and reports progress/results.
- `OutputPathService` and `PartialOutputService` provide output safety, no-overwrite behavior, and partial/canceled output handling.
- `scripts/publish-portable.ps1` builds the self-contained portable package and ZIP.

## Queue Model
- The global `Batch Options` controls are defaults for new queue items and non-custom editable queued items.
- Double-clicking an editable row opens `Edit Queue Item`, where one item can override:
  - Save As/output path
  - Output format
  - Mode
  - Source FPS
- Queue items track custom flags for output path, format, mode, and FPS. Later global setting changes refresh only fields that are not customized.
- Item-level output format, mode, and FPS are the source of truth for probe and conversion.
- When queue processing starts, existing runnable/editable rows are locked to the active queue settings as appropriate. Running, completed, failed, canceled, skipped/existing, and otherwise locked rows are not mutated by later global changes.
- While a queue is running, Add Folder is disabled. Add Files remains available and opens the Edit Queue Item dialog before each selected file is added.
- Files added while running are validated/probed before conversion and are picked up by the active queue when ready.

## FPS Detection
- Source FPS defaults to Auto-detect.
- Visible Source FPS order is:
  - Auto-detect
  - 30
  - 29.97
  - 25
  - 24
  - 20
  - 15
- Auto-detect scans Spotter frame records identified by ASCII `H264` and `I264` markers.
- Accepted frame records read:
  - timestamp at marker offset `-16` as little-endian `uint64`
  - width at offset `-8`
  - height at offset `-4`
  - payload size at offset `+4`
- Optional `.sef`/`.sef2` sidecars can calibrate duration/timebase and raise confidence.
- Without a valid sidecar, detection uses the DAT-only fallback timebase.
- The decision policy prefers stable bucket evidence over noisy total average FPS and maps to supported nominal rates.
- Low-confidence or failed Auto-detect is not silently converted with assumed 30. The row shows `Needs FPS` and requires manual selection before conversion, unless the item is skipped because output already exists.
- Corrupt or unusable Spotter timestamps should fail cleanly with unresolved FPS, not crash.
- Manual FPS overrides resolve immediately. `29.97` maps to FFmpeg value `30000/1001`.

## Probe and Readiness
- Basic file validation checks extension, existence, readability, and non-zero size.
- H.264 compatibility is separate from FPS readiness. A file can be H.264-compatible and still need manual FPS selection.
- If probe requires an FPS but conversion FPS is unresolved, use a probe-only default such as 30. Do not store that as the conversion FPS.
- `Unsupported` means the input payload is not usable as raw H.264.
- `Needs FPS` means the input may be usable, but Source FPS is unresolved.
- Existing output has priority over `Needs FPS` when skip-existing behavior means the row will not be converted.
- Queue start is blocked by runnable items that need FPS. Skipped/existing items do not block queue start because conversion FPS is irrelevant for those rows.

## Conversion Commands
- All conversion paths use the queue item’s effective format, mode, and FPS.
- MP4 Fast/Remux:
  - raw H.264 input
  - `-r <item fps>` before input
  - stream copy
  - MP4-safe mapping/tag/timescale/faststart options
- MKV Fast/Remux:
  - raw H.264 input
  - `-r <item fps>` before input
  - stream copy
- MP4 Full/Encode:
  - raw H.264 input
  - `-r <item fps>` before input
  - video filter includes the same item FPS
  - H.264 encode with yuv420p and MP4 faststart
- MKV Full/Encode:
  - raw H.264 input
  - `-r <item fps>` before input
  - video filter includes the same item FPS
  - H.264 encode with yuv420p
- `29.97` must use `30000/1001` consistently in `-r` and encode filters.
- Audio, subtitles, and data streams are not converted.
- FFmpeg must never target the source `.dat` path.

## Queue Controls
- Start Queue validates runnable items and starts processing eligible rows.
- Cancel Current terminates only the active FFmpeg conversion, marks that item canceled, cleans canceled output, and continues with the next runnable item.
- Stop After Current lets the current item finish and then stops before starting another item. Remaining items stay in their normal states and can be resumed later.
- Cancel Queue uses the whole-queue cancellation path. If an item is active, it terminates the active process and applies canceled-output cleanup.
- Clear All removes editable/finished queue rows according to the current UI rules.
- Clear Completed removes finished rows such as completed, skipped/existing, failed, and canceled.
- Show Details toggles the technical details/log panel.
- Open Output opens the most relevant output file or folder for the selected/recent item.

## Output Safety
- Source files are read-only from the app’s perspective.
- Output paths are guarded so the app cannot write over the source `.dat`.
- Existing output files are not overwritten. Rows whose selected output already exists are marked `Exists` and skipped.
- If a failed conversion was not caused by user cancellation, partial output is renamed to a `.partial` path for troubleshooting.
- If the user cancels the current conversion or queue, active canceled output and active `.partial` output are deleted when safe.
- Cleanup failures are logged and do not crash the app.

## Packaging Notes
- Portable publishing is handled by `scripts/publish-portable.ps1`.
- Bundled FFmpeg tools must be under `tools/ffmpeg` next to `DatConverter.exe` in the portable package:
  - `ffmpeg.exe`
  - `ffprobe.exe`
- FFmpeg license/readme files are copied with the tools when present.
- `README.md` and `THIRD_PARTY_NOTICES.md` are included in the portable release `docs` folder.
- This technical document is internal and should not be included in the portable ZIP.
- Do not move `DatConverter.exe` away from its neighboring `tools` folder.

## Test/Verification Notes
Key scenarios to preserve:
- Normal Spotter `.dat` with matching `.sef2` resolves Auto 30 and converts.
- DAT-only sample without sidecar resolves using fallback timebase.
- Corrupt FPS timestamp sample remains H.264-compatible but shows `Needs FPS` until manual FPS is selected.
- Unsupported `.dat` files are rejected or marked Unsupported without disrupting other queue items.
- Existing output rows show `Exists`, skip normally, and do not block queue start because of unresolved FPS.
- Add Files while running opens Edit Queue Item, validates/probes confirmed files, and lets the active queue process newly ready items.
- Cancel Current cancels only the active item and continues.
- Stop After Current finishes the active item and stops before the next.
- Cancel Queue stops queue processing and cleans active canceled output.
- Output safety tests should confirm source files are unchanged and canceled partials are cleaned.
- Published ZIP smoke should launch from a clean extraction, find bundled tools, default to MP4/Fast/Auto-detect, and complete one MP4 Fast conversion with source timestamp/size unchanged.
