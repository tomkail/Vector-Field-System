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

## Debug renderer / settings
- [ ] Decide whether the density controls (variable resolution / spacing / max arrows — currently per-user in the
      scene-view overlay via `EditorPrefs`) should also live in the **Vector Fields** project-settings page as
      project-wide defaults, or stay per-user. (See `VectorFieldDebugSettingsProvider` / `VectorFieldDebugSettings`.)
- [ ] Minor: the project-settings page is labelled "Vector Fields" but the backing type/file are still
      `VectorFieldDebugProjectSettings` / `ProjectSettings/VectorFieldDebugSettings.asset` — rename for consistency if desired.
