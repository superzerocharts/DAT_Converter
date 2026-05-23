# Third-Party Notices

## FFmpeg

DAT Converter uses FFmpeg command-line tools when they are bundled with the application:

- `ffmpeg.exe`
- `ffprobe.exe`

FFmpeg is a third-party project. The distributor of a portable DAT Converter package is responsible for including the appropriate FFmpeg license, notice, and attribution files for the exact FFmpeg build being distributed.

Do not assume the license terms for a specific FFmpeg build without checking that build's included documentation and configuration.

Recommended distribution folder:

```text
tools\ffmpeg\
  ffmpeg.exe
  ffprobe.exe
  [FFmpeg license and attribution files]
```

DAT Converter does not download FFmpeg and does not use a system-wide FFmpeg installation.
