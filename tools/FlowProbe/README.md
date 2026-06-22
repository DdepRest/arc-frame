# FlowProbe

Diagnostic console that replicates the WPF app's `VelopackFlowSource` check
for `packId=ARC-Frame`, `channel=win` without launching the GUI.

## Why

After `deploy-flow.bat` uploads a release, you want to know whether it's
actually **published and reachable** on the `win` channel — not stuck in
Draft. Three options without launching the WPF app:

| Option | Cost | Caveat |
|---|---|---|
| Open `flow.velopack.io` and click Publish | 1 click | Need confirm in UI |
| Write a curl script | Free | Velopack's API URL isn't documented |
| **Run `FlowProbe`** | `dotnet run` | **Authoritative** (uses same code path) |

## How it works

`FlowProbe.csproj` references the same `Velopack 1.2.0` NuGet the main
WPF project uses. It:

1. Constructs `new VelopackFlowSource()` — official Velopack Flow integration.
2. Injects a `TestVelopackLocator("ARC-Frame", "0.0.1", ...)` with
   `ExplicitChannel="win"` so `UpdateManager` doesn't need a real Velopack
   install context (avoids the `InvalidOperationException` you'd get
   from `new UpdateManager(source)` alone in a vanilla console exe).
3. Calls `await mgr.CheckForUpdatesAsync()` — **same code path as the
   WPF app's `UpdateService.CheckOnStartupAsync()`**.
4. Prints the result.

The version `"0.0.1"` is intentionally below every released version so
Flow returns the LATEST published release — i.e. the same thing the WPF
app would see regardless of which version is currently installed.

## Run

```bash
cd "C:\Users\DeepRest\Desktop\A.R.C. Frame\gwga"
dotnet run --project tools/FlowProbe -c Release
```

## Exit codes

- `0` — `RESULT: UPDATE_AVAILABLE <version>` (release is published, users can get it)
- `1` — `RESULT: NO_UPDATE_AVAILABLE` (release is still in Draft in Flow UI)
- `2` — `ERROR: ...` (network / protocol error — full exception printed)

The Flow URL is internal to `VelopackFlowSource` — the probe uses the
exact same network call the WPF `UpdateService` uses, so a green probe
guarantees users will see the update.
