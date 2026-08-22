# BoostFPS

Windows FPS / input-lag optimizer. WPF .NET 10, x64.

Rolls the FiveM tuning I keep on this machine into one app: registry tweaks
(from `GameOptimize.reg`), a curated services list with per-hardware gating,
NVIDIA `.nip` import via `nvidiaProfileInspector`, NIC/TCP/Power/BCD tuning
ported from `Apply_Portable.ps1`, a RAM cleaner (`NtSetSystemInformation`),
a DNS ranker + setter, a Game Mode / Graphics preference manager,
an auto-tune button that reads the machine and picks the right tier,
a Group Policy toggle page, and a baseline diff so you can see what has
actually been changed vs Windows defaults.

## Build

Requires **.NET 10 SDK** (10.0.201 tested).

```bash
dotnet build BoostFPS.sln -c Release
dotnet publish src/BoostFPS/BoostFPS.csproj -c Release -r win-x64 \
    --self-contained false -p:PublishSingleFile=true -o publish
```

The exe requires administrator rights (app manifest).

## Layout

- `src/BoostFPS.Core/` — models + services (no UI dependency)
- `src/BoostFPS/` — WPF app
- `tools/Probe/` — headless catalog + gate probe
- `src/BoostFPS.Core/Data/tweaks.json` — 50+ tweak definitions
- `src/BoostFPS.Core/Data/services.json` — 89 gated service definitions

## Safety

Every Apply captures the prior state of every touched value AND creates a
System Restore point BEFORE the first write. Backups live under
`%ProgramData%\BoostFPS\Backups\`. Revert page rolls one snapshot back to
the exact prior values (no blanket defaults).

## Runtime data (not committed)

- `%ProgramData%\BoostFPS\Backups\<id>\snapshot.json` — per-apply capture
- `%ProgramData%\BoostFPS\Backups\<id>\key_*.reg` — reg.exe exports
- `%ProgramData%\BoostFPS\changelog.json` — append-only action log
