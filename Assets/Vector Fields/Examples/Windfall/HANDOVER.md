# Windfall — Session Handover

Status snapshot for picking up the Windfall demo game in a fresh Claude session.

## What this is
**Windfall** — a local-multiplayer "wind golf / magnetic balls" demo that shows off
the Vector Field System. Players ride an authored (now generated) vector field with
a one-button electromagnet catch/coast, trying to settle inside a scoring ring.
Everything lives in `Assets/Vector Fields/Examples/Windfall/`. **Read `GAME_DESIGN.md`
first** — it's the full design intent; **§14 in it records what actually shipped**,
which is what this snapshot summarises.

## Current state — BUILT & PLAYABLE
The game runs in `Windfall_Greybox.unity`: press Play, it fades in, pans the level,
names the round, then all players fly simultaneously; scores accumulate across
rounds; a results screen shows between rounds and final standings at the end.
Compiles clean (checked via `mcp__UnityMCP__read_console`).

Scripts (namespace `Windfall`, behind the `Windfall` asmdef):
- `WindfallGame` — round manager + phase state machine
  (`FadeIn → Pan → RoundName → Playing → Results → FadeOut → GameOver`); spawns
  players, scatters collectibles, cumulative scoring, round timer, Backspace reset.
- `WindfallCamera` — intro pan + follow-cam (lead-toward-goal, HUD-strip
  reservation, Scene-view gizmos). Auto-created on the game object if unwired.
- `WindfallLevelGenerator` — random level per round as a `GroupVectorFieldComponent`:
  toward-goal pull + noise + 0–2 stamps + optional spline road (`WINDFALL_SPLINES`).
- `WindGlider` — one-button launch + catch/coast integrator, CPU field consumer,
  metallic material + plasma trail, `Frozen` gate.
- `WindfallInput` — one button per player (keyboard key or gamepad South) + `Label`.
- `WindfallHUD` — runtime UGUI: score bar, fade, banner, "+N" popups, results,
  timer bar, screen-edge goal arrow; `TopInsetPixels()` for the camera.
- `WindfallJuice` — magnetic hum / catch pulse / collision clack (§7a).
- `WindfallPostFx` — runtime URP bloom.
- `TargetRing`, `Collectible`, `WindfallSettings` (live-tuned feel SO).

## How it diverged from the original design (see GAME_DESIGN §14)
- **Simultaneous** multiplayer (not turn-based-first).
- **Random generated** levels (not hand-authored courses); `field` + `generateLevels=false`
  still works for a fixed field.
- **No islands / re-launch**; a run is a single flight, with a safety round timer.
- Scoring adds **collectibles** + a **settle-order rank bonus** on top of ring zones.
- **Items** (§9a) not built.

## Key knobs (on `WindfallGame` unless noted)
- Wiring: `playerPrefab`, `spawnParent`, `field`, `levelGenerator`, `targetRing`,
  `settings`, `generateLevels`, `bloom`.
- Rounds: `roundCount` (3), `roundNames`, `roundTimeLimit` (45s); timing:
  `fadeDuration`, `panDuration`, `roundNameDuration`, `resultsDuration`.
- Level: `levelSizeRange` (world size randomised per round).
- Camera (on `WindfallCamera`): `minZoom`, `maxZoom`, `margin`, `followSpeed`,
  `lead`, `establishZoom`, `drawGizmos`.
- Generator (on `WindfallLevelGenerator`): `targetPullStrength` (0.4),
  `noiseStrength` (1), `frequencyRange` (1–2), stamp/spline ranges.
- Feel: everything on the `WindfallSettings` asset, tunable live in play mode.

## Environment gotchas
- **Use the community "MCP for Unity" server (`mcp__UnityMCP__*`)**, NOT the
  official Unity one. If those tools aren't listed, the server isn't registered
  with Claude Code — not a code bug. `no_unity_session` = Editor not attached.
- After editing scripts: `refresh_unity` (scope `all`, mode `force`,
  compile `request`) to import, then `read_console` for errors before using new
  types; the MCP bridge often drops across the domain reload — retry after ~10s.
- Unity 6000.5.1f1, URP, new Input System.
- **Concurrent git hazard:** other agents run broad `git add -A`/commits on this
  branch — stage Windfall files by pathspec, never `git add -A`.
- The scene (`Windfall_Greybox.unity`) carries a large diff from concurrent scene
  work and is generally **left uncommitted**; commit Windfall `.cs` files only.
- Public field API reference: repo-root `VECTOR_FIELDS.md` §"Reading a field from
  code".

## Likely next tasks / known rough edges
- Target ring size doesn't scale with level size (can feel small on big levels).
- Goal arrow can nudge the top HUD bar when the target is straight up.
- Play-mode verification is flaky when the Editor is unfocused (freezes play mode).
