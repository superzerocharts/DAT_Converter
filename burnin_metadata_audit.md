# Burn-in Metadata Audit

## Summary

The actual Spotter/Mirasys camera name is available to DAT Converter from the `.sef2` sidecar. For the 4-hour split export, the sidecar contains a video channel whose base64 `name` attribute decodes to:

```text
8379 Marquee Northeast PTZ
```

The sidecar also includes channel ID `69`, manufacturer `AXIS`, model `AXIS Q6135-LE`, recording start/end, ordered segment file names, and a serialized Pacific time zone object. `MaterialFolderIndex.dat` contains segment start/end times. DAT frame records contain stream timestamps that can establish FPS/duration/continuity, but in this sample the DAT record timestamp used by the converter FPS detector is not a direct wall-clock `DateTime`.

Current converter burn-in can use recording time for split exports, but the top label is currently the export logical base name, such as `Cam 8379 - 4 hr clip`, not the camera display name. A safe first improvement is to extract and store the `.sef2` camera/display name, then use it as the burn-in label when available while preserving the existing folder/file-name fallback.

Timestamp behavior should be handled separately. The converter currently uses `MaterialFolderIndex.dat` segment times for split exports and treats them as local/unspecified wall time. SpotterPlayer likely uses the sidecar timezone metadata, but the exact mismatch with the observed Spotter overlay is not fully proven from files alone.

## Current Converter Burn-in Behavior

Burn-in is built by `BurnTimestampMetadataBuilder.Build`.

Current requirements:

- `QueueItem.BurnTimestamp` must be enabled.
- `RecordingTimeline.RecordingStart` must be available.
- Burn-in is supported only for Full/Encode modes through UI policy.

Current label selection:

1. `QueueItem.LogicalOutputBaseName`
2. `QueueItem.SplitExportPlan.LogicalOutputBaseName`
3. source DAT file base name
4. fallback `"Camera"`

For split exports, `SpotterSplitExportPlanBuilder.ResolveLogicalOutputBaseName` currently uses the `.sef2` base filename or folder name. For this sample that becomes:

```text
Cam 8379 - 4 hr clip
```

That explains the observed converter burn-in label:

```text
Cam 8379 - 4 hr clip
05/22/26
04:36:27
```

Current timestamp selection:

- `RecordingTimelineBuilder.Build(item)` uses `SpotterSplitExportPlan` for split recordings.
- Split segment start/end times come from `MaterialFolderIndex.dat`.
- `BurnTimestampMetadataBuilder` uses `timeline.RecordingStart` plus trim offset if a trim is selected.
- `FfmpegCommandBuilder` emits three `drawtext` filters:
  - camera/label text
  - `%{pts:localtime:<epoch>:%m/%d/%y}`
  - `%{pts:localtime:<epoch>:%H\:%M\:%S}`
- The epoch is computed with:

```csharp
new DateTimeOffset(DateTime.SpecifyKind(burnTimestamp.StartTime, DateTimeKind.Local)).ToUnixTimeSeconds()
```

So the converter treats the selected start time as the current machine's local time.

## Available Metadata Sources

### `.sef2`

Path used for this audit:

```text
W:\Projects\Sample files\Cam 8379 - 4 hr clip\Cam 8379 - 4 hr clip.sef2
```

Available fields:

- export start: `2026-05-22T03:59:59.4800000`
- export end: `2026-05-22T07:59:59.9870000`
- file list:
  - `dvrfile00000001.dat`
  - `dvrfile00000002.dat`
  - `dvrfile00000003.dat`
  - `dvrfile00000004.dat`
- channel:
  - `dataType="Video"`
  - `channelId="69"`
  - `channelType="Material"`
  - `dataSize="3675378259"`
  - `is360="false"`
  - `name="ODM3OSBNYXJxdWVlIE5vcnRoZWFzdCBQVFo="`
  - `manufacturer="AXIS"`
  - `model="AXIS Q6135-LE"`
  - `timezone="<base64 serialized time zone>"`
- XML signature is present.

The `.sef2` file is the best source for the user-facing camera/display name.

### `MaterialFolderIndex.dat`

Path:

```text
W:\Projects\Sample files\Cam 8379 - 4 hr clip\MaterialFolderIndex.dat
```

The app already parses this file in `SpotterSplitExportPlanBuilder`. For this export it contains four segment timing records:

| Segment | Start | End |
|---:|---|---|
| 1 | 2026-05-22 03:59:59.480 | 2026-05-22 05:09:01.003 |
| 2 | 2026-05-22 05:09:01.037 | 2026-05-22 06:18:37.027 |
| 3 | 2026-05-22 06:18:37.061 | 2026-05-22 07:28:32.376 |
| 4 | 2026-05-22 07:28:32.409 | 2026-05-22 07:59:59.987 |

These are currently the converter's strongest source for split-export recording time and trim-time calculation.

### DAT Frame Records

Each DAT segment contains repeated `H264`/`I264` records. The converter can read:

- marker kind
- stream timestamp
- width/height
- payload size
- payload offset
- keyframe/interframe classification

Quick scan values for this export:

| File | Records | H264 | I264 | First timestamp | Last timestamp | DAT-only duration | Avg FPS |
|---|---:|---:|---:|---:|---:|---:|---:|
| `dvrfile00000001.dat` | about 124k | about 8.3k | about 116k | 74554274475407623 | 74554274637185865 | 4141.523s | about 30.0 |
| `dvrfile00000002.dat` | about 125k | about 8.4k | about 117k | 74554274637187193 | 74554274800311803 | 4175.990s | about 30.0 |
| `dvrfile00000003.dat` | about 126k | about 8.4k | about 118k | 74554274800313131 | 74554274964192623 | 4195.315s | about 30.0 |
| `dvrfile00000004.dat` | about 57k | about 3.8k | about 53k | 74554274964193912 | 74554275037927428 | 1887.578s | about 30.0 |

These timestamps are excellent for FPS and continuity, but they are not used directly as the burn-in wall-clock time in the current converter.

### Queue Item Metadata

Current queue-item metadata relevant to burn-in:

- `LogicalOutputBaseName`
- `SplitExportPlan`
- `TrimRange`
- `BurnTimestamp`
- `PreProbeResult`

There is currently no queue item field for camera display name, manufacturer, model, channel ID, or sidecar timezone.

## Camera Name Findings

The camera/display name is in the `.sef2` channel element:

```xml
<channel
  dataType="Video"
  channelId="69"
  channelType="Material"
  name="ODM3OSBNYXJxdWVlIE5vcnRoZWFzdCBQVFo="
  manufacturer="AXIS"
  model="AXIS Q6135-LE"
  ... />
```

Base64 decode:

```text
ODM3OSBNYXJxdWVlIE5vcnRoZWFzdCBQVFo=
```

decodes as UTF-8 to:

```text
8379 Marquee Northeast PTZ
```

This field is a good candidate for the normal burn-in label. Manufacturer/model/channel are useful technical metadata, but they should not be included in the normal burn-in unless the user explicitly asks for a more technical overlay.

Current converter behavior does not extract or store this decoded name. It only uses sidecar/folder naming for the split export's logical output base name.

## Timestamp Findings

Observed converter burn-in:

```text
Cam 8379 - 4 hr clip
05/22/26
04:36:27
```

Observed SpotterPlayer overlay:

```text
11:32:22 PM  5/21/2026
8379 Marquee Northeast PTZ
(03 fps)
```

Relevant file metadata:

- `.sef2` start: `2026-05-22T03:59:59.4800000`
- `.sef2` end: `2026-05-22T07:59:59.9870000`
- Material index segment 1 start: `2026-05-22 03:59:59.480`
- Material index segment 4 end: `2026-05-22 07:59:59.987`
- Existing converted trim output path:
  - `W:\Projects\Sample files\Cam 8379 - 4 hr clip_v2\Cam 8379 - 4 hr clip_trim_260522_0436-260522_0443_01.mp4`
- That MP4's container metadata says:
  - title: `Cam 8379 - 4 hr clip`
  - comment recording range: `2026-05-22 03:59:59` to `2026-05-22 07:59:59`
  - trim range: `2026-05-22 04:36:13` to `2026-05-22 04:43:04`
  - creation_time: `2026-05-22T11:36:13.000000Z`

The converter timestamp is therefore internally consistent with its current source: it is using the split export timeline from `MaterialFolderIndex.dat`, then applying trim offset.

The Spotter timestamp shown by the user does not directly line up with the current converter trim timestamp or the raw `.sef2`/index start time. The difference cannot be fully explained from the files alone without confirming the exact playback position in Spotter and how Spotter maps the serialized timezone to display time.

## Timezone / Local Time Findings

The `.sef2` channel contains a `timezone` attribute. It is a base64 serialized .NET `System.CurrentSystemTimeZone` object. In this sample it deserializes to:

```text
StandardName: Pacific Standard Time
DaylightName: Pacific Daylight Time
```

The serialized object also contains:

- standard offset: `-8` hours
- daylight delta: `+1` hour

For May 2026, that implies Pacific daylight time, UTC-7.

Current converter behavior:

- Parses `MaterialFolderIndex.dat` timestamps as `DateTimeKind.Unspecified`.
- Does not store the sidecar timezone.
- Converts burn-in start to an epoch by treating it as the current Windows local timezone.
- Uses FFmpeg `localtime` formatting.

If the user's Windows timezone is Pacific, this may accidentally match the sidecar timezone for this export. If the user's Windows timezone differs from the recording timezone, burn-in time can drift.

Safe conclusion: the sidecar contains timezone information, but using it correctly should be a separate timestamp project. The first safe burn-in improvement is camera label only.

## Spotter "(03 fps)" Finding

The source recording evidence strongly indicates about 30 fps:

- DAT-only average FPS is about 30.0 for all four segments.
- Segment boundary gaps are about one 30 fps frame interval.
- Converter auto-detect policy for this family of exports resolves to 30 fps.

Therefore Spotter's displayed `(03 fps)` is unlikely to mean the source recording FPS if it literally reads `03 fps`. More likely possibilities:

- current playback/render/display rate
- a transient UI performance indicator
- a status display rounded or truncated in Spotter's overlay
- a value unrelated to source FPS

It should not be burned into converted video unless more evidence proves it is a user-required source metadata field.

## Recommended Converter Behavior

Smallest safe improvement:

1. Parse the `.sef2` video channel `name` attribute.
2. Base64-decode it as UTF-8.
3. Store the result as a camera/display name on the split export plan or queue item.
4. Use that camera/display name as the burn-in top label when available.
5. Keep the current fallback to logical output base name, folder name, or source file name.

Recommended normal burn-in format:

```text
8379 Marquee Northeast PTZ
05/22/26
04:36:27
```

or, if timestamp policy is later changed to a Spotter-confirmed timezone mapping:

```text
8379 Marquee Northeast PTZ
05/21/2026
11:32:22 PM
```

Do not add manufacturer/model/channel to the normal overlay by default. Those fields are better suited for technical details/logs or optional metadata.

## Implementation Plan

1. Add camera-name extraction from `.sef2`.
   - Extend sidecar parsing in `SpotterSplitExportPlanBuilder` or add a small sidecar metadata reader.
   - Read the first video/material channel with a valid base64 `name`.
   - Decode UTF-8 and sanitize for display.
   - Also capture channel ID, manufacturer, and model as technical metadata if easy.

2. Use camera name in burn-in when available.
   - Add a field such as `CameraDisplayName` to `SpotterSplitExportPlan` or `QueueItem`.
   - Update `BurnTimestampMetadataBuilder.ResolveCameraName` to prefer camera display name over logical output base name.

3. Keep fallback behavior.
   - If no sidecar exists, no channel exists, or decoding fails, keep current label behavior.
   - This preserves existing single-DAT and DAT-only workflows.

4. Investigate timestamp/timezone separately.
   - Confirm exact Spotter playback position against converter trim position.
   - Decide whether `.sef2`/index times are stored as local recording time or UTC.
   - If using sidecar timezone, avoid `DateTimeKind.Local` and model the recording timezone explicitly.
   - Add tests for recordings whose sidecar timezone differs from the user's machine timezone.

## Risks / Unknowns

- The exact Spotter timestamp mismatch is not fully explained without matching the exact Spotter playback frame/position to the converter trim frame.
- The `.sef2` timezone field is a serialized .NET object. It can be decoded in this environment, but implementing robust cross-runtime parsing should be done carefully.
- It is not yet proven whether all Spotter/Mirasys exports encode camera names as base64 UTF-8 in the same `channel/@name` field.
- Multiple-camera exports may contain multiple channels. The converter should match the selected DAT/segment/channel when that scenario appears.
- DAT frame record timestamps are strong for FPS and continuity but should not be assumed to be wall-clock time in this sample.
- Spotter's `(03 fps)` display is not proven to be source FPS and should not drive converter burn-in.
