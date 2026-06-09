# Storyboard Export Audit

## Summary

The sample at `W:\Projects\Sample files\20260019100\20260019100\Storyboard` is a Spotter/Mirasys Storyboard export. It is structurally different from the earlier long split export.

The long split export was one camera recording split across multiple DAT files. This Storyboard export is a playlist/timeline of six clips from six different cameras. The `.sef2` file contains explicit `<StoryboardData>` with ordered `<clip>` records. Each clip has:

- clip name
- source start/end time
- a `node path` GUID

That node GUID maps through `LayoutData` to a `CameraXX` layout node, and the camera ID maps to a `<channel>` record. The channel record provides the camera name, channel ID, manufacturer/model, timezone metadata, and appears to correspond by order to one DAT file.

Each DAT file contains valid Mirasys `H264`/`I264` frame records. Unlike the later segments in the long split export, all six DAT files in this Storyboard sample start with an `H264` keyframe payload containing SPS/PPS/IDR NAL units. That means each clip appears independently decodable once the Mirasys DAT payload records are interpreted correctly. The DAT files are not pure raw `.h264` elementary streams as whole files; they are DAT containers/wrappers around H.264 frame payloads.

## Test Inputs

Storyboard folder:

```text
W:\Projects\Sample files\20260019100\20260019100\Storyboard
```

Files:

| File | Size | SHA-256 | Modified UTC | Likely role |
|---|---:|---|---|---|
| `Storyboard.sef2` | 19,094 | `792BCC2A439F1190614C305D0A1A51B58AD8FA5595309C59B988FADF4C74EC47` | 2026-04-11 05:13:34 | Main XML metadata: storyboard clips, file list, camera/channel metadata, signature |
| `MaterialFolderIndex.dat` | 301 | `9739D738F95DB032C2A24B73FEE69BA13BFE69D1E4FDA1A24C7413E10F185AE5` | 2026-04-11 05:13:34 | Binary clip/segment timing index |
| `dvrfile00000001.dat` | 46,317,568 | `3D002993444AB0D16DF080B29CAF1666DCE55A2DF67C0EE505000A3978256DF1` | 2026-04-11 05:13:34 | Clip/camera DAT payload |
| `dvrfile00000002.dat` | 8,060,928 | `BCFC9A0A4AEA3CCB03E8F5445B4BAF089A808570A22CC4E6F7EFFAA1D58E9503` | 2026-04-11 05:13:34 | Clip/camera DAT payload |
| `dvrfile00000003.dat` | 147,288,064 | `C925A6939B16F32103FB5971DFFA082D02BA7A4FBE198EAD140ED25EDFD315B5` | 2026-04-11 05:13:34 | Clip/camera DAT payload |
| `dvrfile00000004.dat` | 36,229,120 | `65D3C4CCD7E08BA85128B74F8B5BA538567B257FBCF41B8758E5D831CE9EAB50` | 2026-04-11 05:13:34 | Clip/camera DAT payload |
| `dvrfile00000005.dat` | 745,140,224 | `C5DD0A0B32B97268CC983E93B91A066CE2E7551D86E1924012F7B7C27C7EB2A7` | 2026-04-11 05:13:34 | Clip/camera DAT payload |
| `dvrfile00000006.dat` | 489,902,080 | `3EA9659C7ACD025F6F4715FF9443D407044C7B04AB0BED4E213D72B9D4052A0A` | 2026-04-11 05:13:34 | Clip/camera DAT payload |
| `SpotterPlayer.exe` | 489,891,760 | `B39879F068819CAB049A65191B784A321F8AC7DD7B26CAB819275B6AA79FE1BE` | 2025-03-28 18:11:46 | Bundled vendor player |

Original files were read only. No app behavior was changed.

## Folder Structure

The folder is flat:

```text
Storyboard\
  Storyboard.sef2
  MaterialFolderIndex.dat
  dvrfile00000001.dat
  dvrfile00000002.dat
  dvrfile00000003.dat
  dvrfile00000004.dat
  dvrfile00000005.dat
  dvrfile00000006.dat
  SpotterPlayer.exe
```

The main control file is `Storyboard.sef2`. It contains both the generic export file list and the storyboard-specific clip list. `MaterialFolderIndex.dat` repeats the six clip timing records in a compact binary form. The six DAT files correspond to six clip/camera entries.

## SpotterPlayer Behavior

SpotterPlayer was not modified, patched, hooked, or bypassed. Direct file-read tracing was not performed in this pass. The folder includes a bundled `SpotterPlayer.exe`, and the user's observation is that SpotterPlayer plays the folder as a Storyboard timeline.

The file evidence explains how Spotter can do that:

- `Storyboard.sef2` has an explicit `<StoryboardData>` section.
- That section lists six clips in order.
- Each clip references one layout/profile node GUID.
- `LayoutData` maps each profile GUID to a `CameraXX` node.
- `<channels>` maps each camera/channel ID to camera metadata.
- `<files>` lists six DAT files.
- `MaterialFolderIndex.dat` contains the same six timing records.

Existing Spotter logs on this machine include `MaterialFolderIndexes.ConstructSearchTree` entries for material backup loading. No Storyboard-specific file-read log for this exact sample was found during this pass, so the playback description is based on the `.sef2`/index structure plus user observation.

## Metadata Findings

`Storyboard.sef2` has root `<archive2>`.

Global export range:

- start: `2026-04-11T03:21:09.3640000`
- end: `2026-04-11T04:00:56.9870000`

It contains:

- `<StoryboardData>`
- `<LayoutData>`
- `<files>`
- `<channels>`
- XML signature

Signature metadata:

- SignatureMethod: `rsa-sha256`
- DigestMethod: `sha256`
- DigestValue: `5vwsQq5MU5vCLNT4GSbAroorWk17OKtGYfC4owIgEUo=`

The XML signature was observed but not cryptographically validated.

The `<files>` section lists all six DAT files. As with prior Spotter exports, the `hash` attribute appears to be file size plus one-based ordinal, not a cryptographic hash:

| File | Size | SEF2 `hash` |
|---|---:|---:|
| `dvrfile00000001.dat` | 46,317,568 | 46,317,569 |
| `dvrfile00000002.dat` | 8,060,928 | 8,060,930 |
| `dvrfile00000003.dat` | 147,288,064 | 147,288,067 |
| `dvrfile00000004.dat` | 36,229,120 | 36,229,124 |
| `dvrfile00000005.dat` | 745,140,224 | 745,140,229 |
| `dvrfile00000006.dat` | 489,902,080 | 489,902,086 |

All six channels include a base64 camera name, channel ID, camera manufacturer/model, and Pacific timezone metadata.

## Storyboard / Timeline Model

The storyboard is explicitly represented as a named storyboard with six clips:

```xml
<StoryboardData>
  <storyboard name="Storyboard" description="">
    <clips>
      <clip name="Clip 1" ...>
        <time start="2026-04-11 03:21:10.000Z" end="2026-04-11 03:22:25.000Z" />
        <nodes>
          <node path="dd54d89c-ac1a-4d9b-b403-e1d71f381c34" />
        </nodes>
      </clip>
      ...
    </clips>
  </storyboard>
</StoryboardData>
```

Clip mapping:

| Clip | Start | End | Profile node | Channel | Camera | DAT file |
|---|---|---|---|---:|---|---|
| Clip 1 | 2026-04-11 03:21:10Z | 2026-04-11 03:22:25Z | `dd54d89c-ac1a-4d9b-b403-e1d71f381c34` | 24 | `2161 Empire 1A` | `dvrfile00000001.dat` |
| Clip 2 | 2026-04-11 03:22:20Z | 2026-04-11 03:22:29Z | `c8fc519f-18ac-4d83-a5c7-017730d9af8b` | 77 | `2164 Empire Elev Lobby 1st` | `dvrfile00000002.dat` |
| Clip 3 | 2026-04-11 03:22:26Z | 2026-04-11 03:27:26Z | `9766027f-7f99-4e55-8cb9-f37e7c2610f0` | 114 | `2610 Pool Door PTZ` | `dvrfile00000003.dat` |
| Clip 4 | 2026-04-11 03:24:14Z | 2026-04-11 03:25:50Z | `076fef5d-f466-4f36-a83e-5417dfecfa2b` | 79 | `2602 Front Desk OV 2` | `dvrfile00000004.dat` |
| Clip 5 | 2026-04-11 03:27:19Z | 2026-04-11 03:40:30Z | `99518659-3457-45ec-9ed9-85948a6ad13a` | 152 | `2657 Lobby Front Desk Ov Multi` | `dvrfile00000005.dat` |
| Clip 6 | 2026-04-11 03:44:10Z | 2026-04-11 04:00:57Z | `a24a5788-10a7-4415-82a8-7b0319494e64` | 10 | `1822 Lost & Found Podium PTZ` | `dvrfile00000006.dat` |

The clips are listed in storyboard order. Their source recording times partially overlap:

- Clip 2 starts before Clip 1 ends.
- Clip 4 is inside the time span of Clip 3.
- Clip 5 starts shortly before Clip 3 ends.
- There is a larger gap before Clip 6.

This means the Storyboard is not simply one continuous single-camera recording. It is best modeled as a playlist/story sequence of selected camera clips with source timestamps.

## Index File Findings

`MaterialFolderIndex.dat` is 301 bytes:

- header: 21 bytes
- six 44-byte records
- trailer: 16 bytes

Header hex:

```text
01f6ba3c58932bb740a40d6f4d1ad546dd06000000
```

Trailer hex:

```text
fc305faeb8e332ba8cdd1eec91dd8986
```

Parsed records:

| Segment | Start | End | Duration |
|---:|---|---|---:|
| 1 | 2026-04-11 03:21:09.364 | 2026-04-11 03:22:24.975 | 75.611s |
| 2 | 2026-04-11 03:22:19.255 | 2026-04-11 03:22:28.987 | 9.732s |
| 3 | 2026-04-11 03:22:25.075 | 2026-04-11 03:27:25.973 | 300.898s |
| 4 | 2026-04-11 03:24:13.665 | 2026-04-11 03:25:49.996 | 96.331s |
| 5 | 2026-04-11 03:27:18.112 | 2026-04-11 03:40:29.973 | 791.861s |
| 6 | 2026-04-11 03:44:09.487 | 2026-04-11 04:00:56.987 | 1007.500s |

The index start/end values align very closely with DAT frame timestamp offsets. The `.sef2` Storyboard clip times are rounded to whole seconds; the index preserves millisecond timing.

## DAT File Analysis

All six DAT files contain valid Mirasys frame records. All six start with an H264 keyframe and have SPS/PPS/IDR near the beginning. Each appears independently decodable if the app extracts the H.264 payloads from Mirasys records.

| File | Size | Frames | Keyframes | First timestamp | Last timestamp | Duration | Detected FPS | Resolution | Starts with keyframe | SPS/PPS near start | Camera/channel association | Standalone decodable | Notes |
|---|---:|---:|---:|---:|---:|---:|---:|---|---|---|---|---|---|
| `dvrfile00000001.dat` | 46,317,568 | 2,271 | 76 | 74554136009387467 | 74554136012341021 | 75.611s | 30.035 avg; 30.304 median instant | 1920x1080 | yes | yes | channel 24, `2161 Empire 1A` | yes, via extracted payload | starts `NAL 7,8,5` |
| `dvrfile00000002.dat` | 8,060,928 | 293 | 20 | 74554136012117584 | 74554136012497740 | 9.732s | 30.107 avg; 30.304 median instant | 1920x1080 | yes | yes | channel 77, `2164 Empire Elev Lobby 1st` | yes, via extracted payload | short clip |
| `dvrfile00000003.dat` | 147,288,064 | 9,029 | 301 | 74554136012344928 | 74554136024098756 | 300.898s | 30.007 avg; 30.304 median instant | 1920x1080 | yes | yes | channel 114, `2610 Pool Door PTZ` | yes, via extracted payload | overlaps Clip 4 source time |
| `dvrfile00000004.dat` | 36,229,120 | 2,893 | 97 | 74554136016586725 | 74554136020349654 | 96.331s | 30.032 avg; 30.304 median instant | 1920x1080 | yes | yes | channel 79, `2602 Front Desk OV 2` | yes, via extracted payload | nested in Clip 3 source time |
| `dvrfile00000005.dat` | 745,140,224 | 15,657 | 790 | 74554136023791686 | 74554136054723756 | 791.861s | 19.772 avg; 15.385 median instant | 2560x1920 | yes | yes | channel 152, `2657 Lobby Front Desk Ov Multi` | yes, via extracted payload | different resolution and lower/effective variable cadence |
| `dvrfile00000006.dat` | 489,902,080 | 30,193 | 1,006 | 74554136063298521 | 74554136102653990 | 1007.500s | 29.968 avg; 30.304 median instant | 1920x1080 | yes | yes | channel 10, `1822 Lost & Found Podium PTZ` | yes, via extracted payload | one rejected marker candidate; otherwise normal |

Directly treating the whole DAT file as raw H.264 is not reliable because the file contains Mirasys wrapper/index bytes before and between payloads. The useful H.264 stream is inside the accepted frame records.

## Camera / Channel Findings

Channel metadata from `Storyboard.sef2`:

| Channel ID | Camera name | Manufacturer | Model | DAT file |
|---:|---|---|---|---|
| 24 | `2161 Empire 1A` | Hanwha | Hanwha Vision XND-6080RV | `dvrfile00000001.dat` |
| 77 | `2164 Empire Elev Lobby 1st` | Hanwha | Hanwha Techwin XNV-6010 | `dvrfile00000002.dat` |
| 114 | `2610 Pool Door PTZ` | AXIS | AXIS M5525-E PTZ Dome Network Camera | `dvrfile00000003.dat` |
| 79 | `2602 Front Desk OV 2` | AXIS | AXIS P3245-LV | `dvrfile00000004.dat` |
| 152 | `2657 Lobby Front Desk Ov Multi` | Hanwha | Hanwha Vision PNM-9085RQZ1 | `dvrfile00000005.dat` |
| 10 | `1822 Lost & Found Podium PTZ` | AXIS | AXIS M5525-E PTZ Dome Network Camera | `dvrfile00000006.dat` |

All channel names are base64 UTF-8 in the `.sef2` `channel/@name` attribute.

Each channel timezone decoded as Pacific Standard Time / Pacific Daylight Time.

## Timeline / Clip Continuity

The DAT frame timestamps line up with the material index start times. Using segment 1 as the baseline:

| File | Metadata offset | DAT timestamp offset | Duration from DAT timestamps |
|---|---:|---:|---:|
| `dvrfile00000001.dat` | 0.000s | 0.000s | 75.611s |
| `dvrfile00000002.dat` | 69.891s | 69.891s | 9.732s |
| `dvrfile00000003.dat` | 75.711s | 75.711s | 300.898s |
| `dvrfile00000004.dat` | 184.301s | 184.301s | 96.331s |
| `dvrfile00000005.dat` | 368.748s | 368.748s | 791.861s |
| `dvrfile00000006.dat` | 1380.123s | 1380.123s | 1007.500s |

This is strong evidence that the DAT timestamps and `MaterialFolderIndex.dat` are describing the same source timeline.

The storyboard timeline is not purely sequential in source wall-clock time:

- Clip 1 and Clip 2 overlap by about 5 seconds.
- Clip 3 and Clip 4 overlap for about 96 seconds.
- Clip 3 and Clip 5 overlap slightly.
- Clip 5 and Clip 6 have a gap of about 3 minutes 39.5 seconds.

This supports a storyboard/playlist interpretation: it is a chosen sequence of clips from different cameras, each retaining original source time.

## Useful Data For DAT Player

DAT Player could safely use:

- `Storyboard.sef2` as the main logical input.
- `<StoryboardData>` clip order.
- clip names and descriptions.
- clip source start/end times.
- clip `node path` GUIDs.
- `LayoutData` profile node IDs to map clips to camera/channel IDs.
- `<channels>` for camera names, manufacturer/model, channel IDs, and timezone.
- `<files>` plus channel/file order to locate the DAT payload for each clip.
- `MaterialFolderIndex.dat` for precise millisecond clip timing.
- DAT frame records for frame index, FPS, resolution, and payload extraction.

Future player behavior:

- Open `.sef2` or the Storyboard folder as a storyboard project.
- Present a clip list/timeline.
- Show camera name per clip.
- Switch cameras at clip boundaries according to storyboard order.
- Optionally display source-time gaps/overlaps.
- Seek within a clip using that clip's DAT frame index.

## Useful Data For DAT Converter

DAT Converter could safely use:

- Detect Storyboard exports by the presence of `.sef2` with `<StoryboardData>`.
- Warn that selecting one DAT is one clip from a Storyboard, not the whole Storyboard.
- Batch-add all storyboard clips in storyboard order.
- Use camera names for burn-in labels.
- Use clip start/end times for per-clip container metadata and burn-in timestamps.
- Convert each clip separately using the matching DAT and camera metadata.
- Future feature: convert the storyboard sequence into one output by concatenating clips in storyboard order.

Important caution: a combined storyboard output is not the same as combining a split recording. Source times can overlap and there can be gaps. A combined output would need a deliberate policy: preserve storyboard sequence timing, preserve source-time gaps, or simply concatenate selected clips.

## Recommendations

### DAT Player

1. Add a future Storyboard open mode for `.sef2`/folder.
2. Parse `<StoryboardData>` and build a clip playlist.
3. Map clip node GUIDs to channel IDs through `LayoutData`.
4. Map channels to camera names and DAT files.
5. Index each DAT independently.
6. Present clip boundaries and camera names.
7. Treat overlaps/gaps as source-time metadata, not necessarily playback duration.

### DAT Converter

1. Add Storyboard detection separate from split-recording detection.
2. If a single DAT from a Storyboard is selected, show an informational note that it is one storyboard clip.
3. Use `.sef2` camera names for burn-in and metadata when available.
4. Support future "Add Storyboard clips" batch flow that adds all six clips in storyboard order.
5. Keep current single-DAT conversion behavior as fallback.
6. Do not assume the six DAT files form one continuous single-camera recording.

### Future Research

1. Open this exact Storyboard in SpotterPlayer and capture direct UI behavior: one clip at a time, simultaneous cameras, or source-time timeline.
2. Capture Process Monitor file reads to confirm whether Spotter opens `.sef2` first, then `MaterialFolderIndex.dat`, then DATs on demand.
3. Test a storyboard with multiple clips from the same camera.
4. Test a storyboard with more than one node per clip, if Spotter supports multi-camera simultaneous storyboard clips.
5. Validate XML signature handling if authenticity display matters.

## Risks / Unknowns

- Direct SpotterPlayer file-read tracing was not captured in this pass.
- It is inferred, not directly observed here, that file order maps one-to-one with channel order and clip order. The mapping is strongly supported by file sizes, data sizes, timing records, and clip/channel ordering.
- `MaterialFolderIndex.dat` unknown fields were not fully decoded.
- XML signature presence was observed but not cryptographically validated.
- Direct `ffprobe -f h264` over the whole DAT files is not a reliable standalone test because DAT files contain Mirasys wrappers around the H.264 payloads.
- Storyboard playback semantics for overlapping clips need UI confirmation: Spotter may play in storyboard order, show a source-time timeline, or support multi-camera behavior not obvious from static files alone.

## Appendix

### Commands Run

Inventory:

```powershell
Get-ChildItem -Force -Recurse "W:\Projects\Sample files\20260019100\20260019100\Storyboard"
Get-FileHash -Algorithm SHA256 "W:\Projects\Sample files\20260019100\20260019100\Storyboard\*"
```

Metadata parsing used PowerShell XML loading:

```powershell
[xml]$x = Get-Content -Raw "...\Storyboard.sef2"
$x.archive2.StoryboardData.storyboard.clips.clip
$x.archive2.LayoutData.layoutdata.node
$x.archive2.channels.channel
```

Index parsing:

```powershell
$bytes = [IO.File]::ReadAllBytes("...\MaterialFolderIndex.dat")
# 21 byte header, six 44 byte records, 16 byte trailer.
# Segment number and .NET DateTime tick fields were parsed little-endian.
```

DAT scanning:

```text
Scanned for ASCII H264/I264 markers.
Accepted records with sane width/height/payload size.
Read timestamp at marker - 16.
Read width at marker - 8.
Read height at marker - 4.
Read payload size at marker + 4.
Inspected first payload NAL unit types for SPS/PPS/IDR.
```

### SEF2 Snippets

Storyboard clip example:

```xml
<clip name="Clip 1" description="">
  <time start="2026-04-11 03:21:10.000Z" end="2026-04-11 03:22:25.000Z" />
  <nodes>
    <node path="dd54d89c-ac1a-4d9b-b403-e1d71f381c34" />
  </nodes>
</clip>
```

Channel example:

```xml
<channel
  dataType="Video"
  channelId="24"
  channelType="Material"
  dataSize="45166183"
  name="MjE2MSBFbXBpcmUgMUE="
  manufacturer="Hanwha"
  model="Hanwha Vision XND-6080RV" />
```

Decoded channel name:

```text
2161 Empire 1A
```
