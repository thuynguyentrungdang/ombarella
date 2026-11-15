# Changelog

## [v0.5.0]

- Light meter reworked: orbit camera renders a raw HDR frame, exposure (EV) applied before scoring, preview shows the post-exposure frame.
- Natural daylight now counts: EFT's own ambient pass is attached to the light camera (previously only lamps lit the frame).
- Sky dome hidden from the meter render; steeper, closer orbit defaults (less sky in frame).
- Bot vision speed uses a power curve (default meter^0.5) with a distance fade (5 m none, 30 m full) instead of linear; NVG bots keep their blend.
- New config: Exposure (EV), Use EFT sky ambient, Darkness curve exponent, Darkness effect start/full distance.
- SPT 4.1 port with Fika sync add-on, on-demand sampling and perf rework (earlier commits on this branch).
