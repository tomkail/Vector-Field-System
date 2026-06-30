# Handover: Vector Field System — runtime painting API & demos

You're taking over work on the Unity **Vector Field System** (2D grids of `Vector2` that drive forces, flow visuals, and gameplay effects). Your job: **build more gameplay demos and improve the runtime painting API.** Read this, skim the pointers, then validate the current code in the Unity editor before extending it.

## Orientation (read these first)
- `VECTOR_FIELDS.md` (repo root) — user-facing feature reference. Start here for the whole system.
- `DOCS_GUIDE.md` (repo root) — how that reference is regenerated (keep it current as you change the API).
- `Assets/Vector Fields/Brush/RUNTIME_PAINTING_SPEC.md` — the **full intended design** for runtime painting. The API built so far is a deliberate *first cut* of this spec; closing the gaps below = finishing the spec.
- Project memories under the agent memory dir (`vector-field-*`) capture prior decisions.

## Architecture you must keep intact
- **Field types** are `VectorFieldComponent` subclasses (Drawable, Noise, Polygon, Stamp, Simulated, Group) under `Assets/Vector Fields/Vector Field/Components/`. They render lazily to a GPU texture; a CPU mirror is produced only when a consumer registers. Read forces via `RegisterCpuConsumer` + `EvaluateWorldVector`, or `TrySampleWorldVector` (GPU).
- **Brush-op core** (`Assets/Vector Fields/Brush/Ops/`) is **editor-independent and runtime-safe** — keep it that way (gameplay calls it):
  - `IVectorFieldBrushOp` (`Id`, `DisplayName`, `Tooltip`, `GizmoColor`, `NeedsSnapshot`, `CompoundsOnReapply`, `UsesBrushDirection`, `Apply(in BrushApplyContext)`).
  - `BrushApplyContext`, `VectorFieldBrushCell` (per-cell `gridPoint`/`brushForce`/`finalForce`/`strokeForce`/`brushCenter`).
  - `VectorFieldBrushKernel.Apply(field, cells, pressure, op, out RectInt dirty)` — applies cells once.
  - `VectorFieldBrushOpRegistry.Groups` (Paint / Magnitude / Shape) → 11 ops (draw, additive, smudge, erase, burn, dodge, clamp, normalize, repel, attract, swirl).
- **Editor painting**: `VectorFieldDrawingTool` + `VectorFieldDrawingToolSettingsOverlay` (`Assets/Vector Fields/VectorFieldWorldEditor/`). Still builds its own cells — **not yet on the runtime API** (see TODO).

## What was built this session (UNCOMMITTED, UNTESTED)
Runtime painting API — additive, in `Assets/Vector Fields/Brush/`:
- `VectorFieldBrush.cs` — `VectorFieldBrushShape.Radial(softness)` + the `VectorFieldBrush` config (shape/op/size/pressure).
- `VectorFieldStroke.cs` — continuous swept stroke (Catmull-Rom path, soft-capsule rasterization, no dabbing).
- `VectorFieldPainting.cs` — extension facades: `field.Stamp(brush, pos)`, `field.PaintLine(brush, a, b)`, `field.BeginStroke(brush).To(pos)`.

Demos — `Assets/Vector Fields/Examples/`:
- `Demo_VectorFieldTrail` (stroke), `Demo_VectorFieldBurst` (stamp), `Demo_VectorFieldSimFade` (sim damping), `Demo_VectorFieldGroupFade` (group-layer fade), and `VectorFieldDecay` (a tiny "decay-in-place" helper that multiplies a drawable field toward zero each frame).

Fade strategies (from the demos): **decay-in-place** is the recommended default (simplest, cheapest, cost independent of effect count); **simulator** for natural dissipation; **group layers** for per-effect fade curves.

## Known gaps / the API improvements to pursue
1. **Stroke is a first cut** (`VectorFieldStroke.cs`), per the spec:
   - Uniform Catmull-Rom → switch to **centripetal** (avoids overshoot on uneven spacing).
   - **Zero-lag tip only** → add the `TipMode` Smoothed/Leading toggle (the user wanted it as a setting).
   - **Cheap overlap path only** → implement the **exact snapshot + max-coverage** path for compounding ops (`CompoundsOnReapply`/`NeedsSnapshot`: add/smudge/burn/dodge/erase) so frame joins don't double-apply. Currently correct only for set ops (draw/repel/etc.).
   - Per-stroke allocation → pool the cell buffers for many short-lived strokes.
2. `Stamp` paints with no direction (`Vector2.up`) — fine for radial ops; add an angle param for directional stamps.
3. `VectorFieldBrushShape` is radial-only → add a `FromMap` variant wrapping the editor's GPU cookie brush (textured/directional brushes).
4. **Unify editor + runtime** (spec step 7): repoint `VectorFieldDrawingTool` onto the runtime API to delete its duplicate cell-building. Do this carefully — it touches the working editor.
5. Demo polish: the `VectorFieldDecay` dependency in the burst/trail demos is implicit (consider a warning guard); `Demo_VectorFieldSimFade`'s impulse-clear timing and `Demo_VectorFieldGroupFade`'s field pool need in-editor validation.

## Working constraints (important)
- **You cannot rely on this session's code being compiled/validated** — none of the runtime API or demos has run in Unity. Validate in the editor (Unity 6.5 is installed) and fix issues; the **stroke geometry is the riskiest** part.
- **The working tree contains a large parallel refactor by ANOTHER agent** — the `Assets/Legacy/` folder and the `Assets/Vector Fields/SO/` ScriptableObject system are being deleted, plus sim/renderer/scene edits and asset imports. **None of that is "our" work.** Don't sweep it into your commits. (Note: if the SO system is really removed, the "Saving fields as assets" section of `VECTOR_FIELDS.md` should be dropped — regenerate per `DOCS_GUIDE.md`.)
- Branch: `vector-field-core-refactor`. Commit only your own work, in **atomic commits**. Commit trailer: `Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>`.
- macOS gotcha already handled across the editor tools: delete/snap gestures use `EditorGUI.actionKey` (Cmd on Mac / Ctrl elsewhere), never raw `Ctrl` — keep that pattern for any new editor gesture.

## Suggested first steps
1. Open the project, get the `Examples/` demos running on a `DrawableVectorFieldComponent` (+ `GridRenderer`, + `VectorFieldDecay`), and confirm the stroke looks smooth and frame-rate independent. Fix whatever doesn't compile/behave.
2. Pick from the gaps above — the highest-value API improvements are the **centripetal Catmull-Rom + exact overlap path** (correctness for all ops) and the **TipMode toggle**.
3. Build additional demos as you go (e.g. directional beam, persistent wind via the sim, vortex field), each exercising the API and a fade strategy.
4. Keep `VECTOR_FIELDS.md` in sync (regenerate via `DOCS_GUIDE.md`) when the public API changes.
