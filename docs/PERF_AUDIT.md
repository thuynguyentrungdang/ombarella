# Perf audit (0.4.3 -> 0.5.0)

All numbers are from code analysis, NOT measured. Enable debug logging: `[perf]` line every 5 s gives samples, avg/max ms of render+apply.

| # | Finding in 0.4.3 | Change |
|---|---|---|
| 1 | Every sample re-classified every player renderer by name: up to 8 parent `.name` + `ToLowerInvariant` + materials/shader names per renderer = thousands of string allocs per sample (GC spikes) | Classified once per renderer-set rebuild (renderer died / held item changed / option flipped); alloc-free `IndexOf` |
| 2 | `TargetPlayerRenderScope`: new `List`, struct copies, ~6 native property writes per renderer, then restore, every sample | Reused parallel arrays, writes only when value differs, no allocs |
| 3 | Per-sample `List<Player>` x4, `List<Renderer>`, `HashSet`, `ToArray`, `AsyncScoreBatch`, `RenderStats` class, closure | Removed / reused; delegate cached |
| 4 | Pixel loop: 3 float mults + class-field increments per pixel | Int sums, 3 mults total; pure `ScoreCalculator` (tested) |
| 5 | Aim patch: `FieldInfo.GetValue` x2 per call (boxed float + Vector3) on a per-bot hot path; rewrote end point on top of the original (2x `UpdateLookDir`) | Typed public fields in 4.1 (`_timeCoef`, `_offsetStandart`), zero alloc |
| 6 | Vision/aim patches: Unity-object null check + `ConfigEntry.Value` per call | Static cached config, plain dictionary lookup on 24-char id |
| 7 | Fika averaging rendered every human player each sample on host (N renders in one frame) and on every client | One render per client, own player only |
| 8 | `Update()` every frame + `OnGUI` (several calls/frame) even outside raids | 0.25 s (or sample-interval) coroutine poll; HUD is its own component, enabled only in raid when shown, Repaint only |
| 9 | `Utils.Log` built interpolated string every sample even with debug off | Removed; throttled log only |
| 10 | Compute-shader sync fallback: dumps float4/pixel (4x bytes) then CPU loop, plus asset bundle | `Texture2D.ReadPixels` blocking fallback, same scorer; bundle dropped |
| 11 | Sampled at fixed rate even when no bot cares | **On-demand sampling** (idea: user). Patch stamps demand when a bot evaluates a target with `IsVisible == false`; sampler runs only within 2 s of a stamp. Value is held meanwhile; averaging history cleared after 10 s idle |

## Who is ignored (no demand stamp, no scaling)
- Bots not evaluating a player: patch only runs when the bot computes visibility for a target.
- Bots that already see the player (`IsVisible`): no demand.
- Ghosted bots (ORBIT Ghost Mode): need no handling. Dormant ghosts run no vision checks against players and have no `GoalEnemy`; they wake when a player gets close. No ORBIT dependency in this mod.
- Bots vs other bots: vanilla.

## Hot-path cost of the bot patches (runs on the bot-owning process: solo, host, headless)
`GetVisibilityChangeSpeedK` fires per bot per enemy (bot-vs-bot pairs included), so the patch is ordered cheapest-first:
1. `Enabled` static bool.
2. `Registry.Observe`: ONE linear scan of the few tracked human ids (reference-equal fast path, no string hashing, no alloc). Bot-vs-bot pairs exit here.
3. Only for bot-to-human pairs: demand stamp, scale.
- Time comes from `Meters.Now` (set by the 0.25 s poll) instead of the native `Time.time` call.
- Aim patch: `GoalEnemy` null (ghosts, idle bots) exits first.
- Before: 2 to 3 string hashes + `Time.time` per pair. Now: a short scan + bool/null checks for bot-vs-bot. Not measured.

## Not changed (needs in-game profiling)
- `Camera.Render()` itself (scene render + shadows within 20 m) is the dominant cost and still a 1-frame spike per sample. Candidates: culling mask trimming, lower far plane. Risk: alters measured luma, needs A/B.
- Light-camera layer mask `-1` kept.

## Behaviour changes to know
- Vision/aim handicap now applies only to tracked human targets. Bot-vs-bot checks are vanilla (0.4.3 applied the local player's meter to all of them).
- Aim: dark-only penalty added on top of the vanilla/SAIN end point: `end += offsetStandart * timeCoef * (1-meter)*100*AimNerf`. Bright light or AimNerf 0 = untouched (0.4.3 replaced the end point, giving zero spread at full light).
