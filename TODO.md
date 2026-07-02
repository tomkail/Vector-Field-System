# TODO — Vector Field System

A living checklist for the Vector Field System. For full context see `HANDOVER.md`, `VECTOR_FIELDS.md`, and
`Assets/Vector Fields/Brush/RUNTIME_PAINTING_SPEC.md`. Keep `VECTOR_FIELDS.md` in sync (per `DOCS_GUIDE.md`) when the
public API changes.

## Packaging / distribution
The plugin is currently laid out as an Assets-folder plugin (everything in `Assembly-CSharp` / `Assembly-CSharp-Editor`
via `Editor/` folders). To ship it as a proper UPM package these are required:

- [ ] **Add asmdefs.** A runtime asmdef plus an **editor-only** asmdef (editor platform only) for the `Editor/` code
      (settings provider, drawing tool, debug-renderer editor glue). This is what *enforces* the editor/runtime split
      rather than relying on the `Editor/` folder name, and it's required for UPM. Watch the editor→runtime references
      (e.g. `VectorFieldComponentDrawer` → `VectorFieldDebugRenderer`/`VectorFieldDebugAppearance`).
- [ ] **Wrap public types in a `VectorFields` namespace.** The project is currently global-namespace; for distribution,
      namespacing avoids collisions with consumer code. Do it together with the asmdefs.

## Architecture / component consolidation
- [ ] **Investigate rolling the Grid component into the Vector Field component** so a user doesn't need to add two
      separate components to get a working field. Check what the Grid actually owns (cell size / resolution / bounds)
      and whether it's ever shared across fields — if not, fold it in so a single component is self-contained.
- [ ] **Investigate a single Vector Field component with multiple modes** instead of several distinct vector-field
      component types. Evaluate a mode enum (or similar) on one component vs. the current per-type components — weigh
      inspector clarity, serialization, and how much behaviour actually differs between the modes.

## Runtime painting API (first cut → finish the spec)
See `RUNTIME_PAINTING_SPEC.md` for the intended design; the code in `Assets/Vector Fields/Brush/` is a deliberate
first cut.

- [ ] **Centripetal Catmull-Rom** for `VectorFieldStroke` (currently uniform → overshoots on uneven spacing).
- [ ] **`TipMode` toggle** (Smoothed / Leading) — wanted as a per-stroke setting; currently zero-lag tip only.
- [ ] **Exact snapshot + max-coverage overlap path** for compounding ops (`CompoundsOnReapply`/`NeedsSnapshot`:
      add/smudge/burn/dodge/erase) so frame joins don't double-apply. Currently correct only for set ops.
- [ ] **Pool the per-stroke cell buffers** (avoid per-stroke allocation for many short-lived strokes).
- [ ] **Directional `Stamp`** — add an angle param (`Stamp` currently paints with `Vector2.up`; fine for radial ops).
- [ ] **`VectorFieldBrushShape.FromMap`** — wrap the editor's GPU cookie brush for textured/directional brushes.
- [ ] **Unify editor + runtime** (spec step 7): repoint `VectorFieldDrawingTool` onto the runtime API to delete its
      duplicate cell-building. Touches the working editor — do carefully.

## Demos
- [ ] `VectorFieldDecay` dependency in the burst/trail demos is implicit — consider a warning guard.
- [ ] Validate `Demo_VectorFieldSimFade` impulse-clear timing and `Demo_VectorFieldGroupFade` field pool in-editor.
- [ ] Build additional demos (directional beam, persistent wind via sim, vortex field), each exercising a fade strategy.

## Demo suite — examples + showcases
Two goals, deliberately separate:
1. **Teaching examples** — one concept per scene, minimal code, obviously readable. The point is that a user can open
   the scene, understand *how a piece works*, and copy it. Bias toward "boring but crystal-clear."
2. **Showcase demos** — wow the viewer and prove the range. Can combine concepts and hide plumbing; the point is impact.

Coverage to keep in mind (a good suite hits each source × consumer at least once):
- **Sources:** noise · drawable/painted · fluid sim · polygon-shape · Group (layered/combined).
- **Consumers:** particle-system force field · flow-vis (IBFV) · texture renderer · debug arrows · CPU sampling
  (`EvaluateWorldVector`/`EvaluateVector`/`EvaluateRotation`) for gameplay agents, rigidbodies, character movement.
- **Interaction:** runtime brush ops (Draw/Swirl/Repel/Attract/Stamp/`PaintLine`) · `VectorFieldDecay` fade strategies.

### Teaching examples (simple, one concept each)
- [ ] **Noise field → particles.** Static `NoiseVectorFieldComponent` + a particle system via `ParticleSystemVectorField`.
      The "hello world": open, press play, see drift. Almost no code.
- [ ] **Sampling the field from code.** One object that reads `EvaluateWorldVector` at its position and moves/rotates.
      Isolates the CPU sampling API so users see how gameplay reads a field.
- [ ] **Each brush op, side by side.** A drawable field + flow-vis, one stamp per op (Draw/Swirl/Repel/Attract) laid out
      in a row with labels. A visual glossary of what each op does.
- [ ] **Paint sandbox.** Drawable field + IBFV flow-vis + mouse painting with a brush/op palette. The core authoring loop,
      nothing else. (Smoke demo's interaction, stripped of the fluid sim.)
- [ ] **Fade strategies compared.** The same repeated stamp under each `VectorFieldDecay` strategy so the difference is
      obvious. Doubles as the fix for the implicit-decay TODO above.
- [ ] **Group/layered field.** `GroupVectorFieldComponent` combining two trivial sources (e.g. constant + noise) with the
      combined result visualised — shows fields stack like layers.

### Showcase demos (impressive, show range)
- [ ] **Flocking / crowd flow.** Agents read the field via `EvaluateVector` to bias heading; drop a moving Vortex/Repel
      stamp and watch them part and swirl. Best proof the field is code-usable, not just a particle toy.
- [ ] **River / racetrack current.** `PolygonVectorField` defines a channel; rigidbody props get swept downstream by
      force sampled from the field. Exercises the polygon source + real physics (both currently unshown).
- [ ] **Player in the current.** Top-down character pushed by the field — headwind, sweeping current, a Repel "force
      push" ability. Most relatable gameplay pitch.
- [ ] **Coloured smoke** (existing Smoke demo) — polish/keep as the fluid-sim + colour-advection flagship.
- [ ] **Layered environment.** Group source: base noise breeze + painted gust + simulated eddy → one particle/flow output.
      The "production-realistic" composition demo.
- [ ] **Ambient beauty pass.** Leaves/snow/petals on a noise field — the low-effort screenshot/gif that sells at a glance.

## Debug renderer / settings
- [ ] Decide whether the density controls (variable resolution / spacing / max arrows — currently per-user in the
      scene-view overlay via `EditorPrefs`) should also live in the **Vector Fields** project-settings page as
      project-wide defaults, or stay per-user. (See `VectorFieldDebugSettingsProvider` / `VectorFieldDebugSettings`.)
- [ ] Minor: the project-settings page is labelled "Vector Fields" but the backing type/file are still
      `VectorFieldDebugProjectSettings` / `ProjectSettings/VectorFieldDebugSettings.asset` — rename for consistency if desired.
