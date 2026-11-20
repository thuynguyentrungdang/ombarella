# Changelog

## [Unreleased]

- Aim handicap fade-out score slider: penalty fades from full at score 0.1 to none at the chosen score.
- Light match option (Off/Attach): attaches TraceSix indirect-light pass to the meter camera. Default Off, scoring unchanged.
- Meter HUD shows when the raid starts, hides while inventory/menu screens cover the battle UI, returns on close.
- Meter skips a sample when the orbit camera is inside geometry (score holds last value).
- Release tooling: CI test workflow, `Build-Release.ps1 -Publish`, SHA256SUMS.txt.

## [v0.6.0]

- Light meter reworked: orbit camera renders a raw HDR frame, exposure (EV) applied before scoring, preview shows the post-exposure frame.
- Natural daylight now counts: EFT's own ambient pass is attached to the light camera (previously only lamps lit the frame).
- Sky dome hidden from the meter render; steeper, closer orbit defaults (less sky in frame).
- Bot vision speed uses a power curve (default meter^0.5) with a distance fade (5 m none, 30 m full) instead of linear; NVG bots keep their blend.
- Score is now brightness + weighted color depth, capped at 1 (was averaged, which capped a whiteout at 0.5). New config "Color depth weight" (default 0.5).
- Weapon flashlight override (idea from VeilSight): white light on = rendering paused, score 1.0. IR light on = only NVG bots ignore darkness. Lasers ignored. Fika meter packet gained an IR flag (host and client must match builds).
- New config: Exposure (EV), Use EFT sky ambient, Darkness curve exponent, Darkness effect start/full distance.
- SPT 4.1 port with Fika sync add-on, on-demand sampling and perf rework (earlier commits on this branch).
