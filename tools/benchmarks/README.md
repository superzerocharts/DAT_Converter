# Full Encode Benchmark Runner

Developer-only benchmark for comparing Full-mode MP4 encode speed across machines.

Example:

```powershell
.\tools\benchmarks\benchmark-full-encode.ps1 `
  -InputDat "W:\Projects\DAT_Converter\test-assets\samples\dat_5min_sample.dat" `
  -OutputFolder "W:\Projects\BenchmarkResults" `
  -Fps 30 `
  -Iterations 1 `
  -MaxDurationSeconds 300 `
  -DeviceId "workstation-a"
```

The runner uses bundled `tools\ffmpeg\ffmpeg.exe`, raw H.264 DAT input options, MP4 output, `-progress pipe:1`, and unique output names. It writes `benchmark_results.csv` and `benchmark_results.md`.

Per-run result files are written to `-OutputFolder` next to the generated MP4 outputs. The same rows are also appended to Git-trackable aggregate files under `docs\benchmarks` by default:

- `docs\benchmarks\benchmark_results.csv`
- `docs\benchmarks\benchmark_results.md`

Use `-ResultsFolder` to choose a different tracked results folder.

By default, each case is capped at 300 seconds so large real-world exports do not run for an hour. Use `-MaxDurationSeconds` to choose a different cap.

`-DeviceId` defaults to the machine name and is included in CSV rows and the Markdown comparison table for cross-workstation comparisons.

Burn-in cases use fixed text by default. They measure drawtext encode cost, not recording metadata lookup.

This tool does not change production app settings or defaults.
