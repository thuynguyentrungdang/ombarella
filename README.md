# Ombarella (SPT 4.1)

Light-meter based bot vision/aim handicap. Branch `spt4.1` = SPT 4.1 build (3.11 version lives on `master`).

## Layout
- `ombarella.csproj` + root folders: core mod (`ombarella.dll`), no Fika dependency. Exposes `OmbarellaApi` hooks.
- `Ombarella.Fika/`: separate project (`ombarella.fika.dll`), Fika sync add-on. Hard-depends on core + `com.fika.core`.
- `tests/Ombarella.Tests`: pure-logic tests (links `Core/*.cs`, no game refs).

## Build
```
dotnet build ombarella.csproj                              # Debug: DLL+PDB -> F:\SPT_4.1 - Dev\BepInEx\plugins\Ombarella
dotnet build Ombarella.Fika\Ombarella.Fika.csproj          # Debug: ombarella.fika.dll -> same folder
dotnet build <either> -c Release                           # bin\Release\Ombarella.zip / Ombarella.Fika.zip + D:\Staging Area\Ombarella
dotnet test tests\Ombarella.Tests
```
Compile refs: `-p:GameDir=<SPT install>\` (default `F:\SPT_4.1\`, or env `SPT_INSTALL_PATH`). Fika.Core: `-p:FikaDll=`. Version lives in `Directory.Build.props`.

## Install
Zips are game-root layout: `BepInEx\plugins\Ombarella\ombarella.dll` (+ `ombarella.fika.dll` for Fika). No shader bundle needed anymore.

## Fika
Needs the add-on (`Ombarella.Fika.zip`) on **every client AND the host/headless**; solo play needs core only.
- Each client meters only its own player and sends the raw value to the host/headless.
- Host/headless applies it per target player (`EnemyInfo.ProfileId`) with its own *Light meter strength* / *Bot aim handicap*.
- Headless does no rendering, receive only. Host replies with an ack + "a bot is watching you" flag.
- Host without Ombarella.Fika: client probes ~16 s, then stops sending and logs a warning.
- Removed config: *Use Fika player averaging* (replaced by real sync).

## Performance
See [docs/PERF_AUDIT.md](docs/PERF_AUDIT.md). New option *On-demand sampling*: light is only rendered while some bot is looking at a player it has not detected yet.

## Cutting a release
1. Bump `ModVersion` in `Directory.Build.props`, add a `## [vX.Y.Z]` entry to `CHANGELOG.md`, commit.
2. `scripts/Build-Release.ps1` locally (needs the game install) -> `bin/Release/Ombarella.zip`, `Ombarella.Fika.zip`, `SHA256SUMS.txt`, also copied to `D:\Staging Area\Ombarella`.
3. `scripts/Build-Release.ps1 -Publish` (clean tree, `gh` CLI): tags `vX.Y.Z`, pushes the tag -> CI runs tests, checks tag vs version, creates a DRAFT release from the changelog; script then attaches zips + checksums.
   Manual alternative: `git tag vX.Y.Z && git push origin vX.Y.Z`, attach files to the draft.
4. Review the draft on GitHub, publish. (`ci.yml` runs the tests on every push/PR.)
