# Windfall — Session Handover

Status snapshot for picking up the Windfall demo game in a fresh Claude session.

## What this is
Building **Windfall**, a demo game that shows off the Vector Field System. It's a
one-button "wind golf" game. Everything lives in
`Assets/Vector Fields/Examples/Windfall/`. **Read `GAME_DESIGN.md` in that folder
first** — it's the full, iterated spec.

## Locked design decisions (see GAME_DESIGN.md for detail)
- **One button, whole game.** Launch is golf-style and one-button: an oscillating
  **direction** sweep (tap to lock) → oscillating **power** bar (tap to fire).
- **Flight is 100% field-driven** — no steering. Hold button = *catch* (velocity
  snaps toward the local field vector, impulse-like via exponential approach +
  press kick). Release = *coast* under deliberately **low** drag so the settle
  lingers (roulette-wheel tension). Patient settle detection.
- **Single flight**, with **islands** that grant a re-launch (route-planning).
- **Out of bounds = level fail.**
- **A level takes any `VectorFieldComponent`** (Tom authors the fields); lies flat
  in the **XY plane** (plane normal = Z, the component's default orientation), so
  `EvaluateWorldVector` returns an XY vector.
- **Player-vs-player collision** is core (pétanque knock; settled pieces stay as
  obstacles). Dormant with one player. For local multiplayer (turn-based first).
- **All feel constants live in `WindfallSettings` (ScriptableObject)** so they
  tune LIVE in play mode.
- **Theme undecided** — leaning marbles/boules or conkers (collision wants a solid
  knockable object). **Items** (field-editing power-ups) deferred; MVP without.

## What's built (Step 1 — DONE, compiles clean)
Three scripts, namespace `Windfall`, in this folder:
- `WindfallSettings.cs` — the live-tuned SO (`Create ▸ Windfall ▸ Settings`).
- `WindfallInput.cs` — one-button-per-player (new Input System polling; Space /
  Enter / gamepad South).
- `WindGlider.cs` — launch state machine + catch/coast integrator (2D kinematic on
  XY, not Rigidbody2D), CPU-consumer field reads, events
  (`OnLaunch/OnCatchStart/OnCatchEnd/OnSettle`), grey-box gizmos + optional aim
  LineRenderer / TrailRenderer. Tapping while settled re-launches (island-style).

Reads the field via the CPU path (register consumer in OnEnable with
`immediate:true`, `EnsureUpToDate()` before `EvaluateWorldVector` each FixedUpdate).

## Build order (from GAME_DESIGN.md §11) — where we are
0. Visualization bake-off (NOT started; needs Editor — screenshot each renderer
   over one field, pick the base look; the "clear + not ugly" question is open).
1. **Movement prototype scripts — DONE.** Still need to create a `WindfallSettings`
   asset + a grey-box scene and confirm the FEEL in play mode. ← **NEXT**
2. Scoring (target ring + patient settle + OOB fail).
3. Islands (rest-on-island → re-launch).
4. Juice (VFX/SFX events §7a + camera).
5. Real course (a Tom-authored field + chosen visualization).
6. Local multiplayer (turn-based first, with collision + knockable settled pieces).
7. (Stretch) items.

## Immediate next task
1. Confirm the community **"MCP for Unity"** server is connected to Claude Code
   (run `/mcp`; tools are `mcp__UnityMCP__*`). If missing, it must be registered
   with the client and the session restarted — opening the Editor bridge alone
   is not enough.
2. `read_console` → confirm clean (it is, as of this handover).
3. Create a `WindfallSettings` asset in this folder.
4. Build a grey-box scene here: a `NoiseVectorFieldComponent` (identity rotation =
   XY plane) + a glider GameObject (sphere/sprite) with `WindGlider` (assign field
   + settings + a TrailRenderer) + an orthographic camera looking down +Z.
5. Enter play, screenshot, and tune the SO live to get the launch→catch→coast→
   settle feel right. That feel is the whole game — nail it before scoring.

## Environment gotchas
- **Use the community "MCP for Unity" server (`mcp__UnityMCP__*`)**, NOT the
  official Unity one. If `mcp__UnityMCP__*` tools aren't in the tool list, the
  server isn't registered with Claude Code (checked via `/mcp`) — not a code bug.
- Unity 6000.5.1f1. Input System 1.19.0 (activeInputHandler = Both). No asmdefs in
  `Vector Fields/` — these scripts are in `Assembly-CSharp`.
- **Concurrent git hazard:** other agents run broad `git add -A`/commits on this
  branch — be careful staging.
- Public field API reference: repo-root `VECTOR_FIELDS.md` §"Reading a field from
  code".
</content>
</invoke>
