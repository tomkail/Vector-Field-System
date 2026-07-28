# TODO — Vector Field System

A living checklist for the Vector Field System. For full context see `HANDOVER.md`, `VECTOR_FIELDS.md`, and
`Assets/Vector Fields/Brush/RUNTIME_PAINTING_SPEC.md`. Keep `VECTOR_FIELDS.md` in sync (per `DOCS_GUIDE.md`) when the
public API changes.

## Packaging / distribution
To ship as a proper UPM package these are required:

- [x] **Added asmdefs.** `VectorFields` (runtime, asmdef at the plugin root; references `UnityX.NoiseSampler` +
      `UnityX.Noises`, `Unity.InputSystem`, `Unity.Mathematics`, and `Unity.Splines` — the splines reference is by
      GUID with a `versionDefines` entry setting `VECTOR_FIELDS_SPLINES`, so that package stays optional). All editor
      code was consolidated from the scattered `<X>/Editor/` subfolders into one `Editor/` tree mirroring the runtime
      layout (including `VectorFieldWorldEditor/`, which previously sat unguarded in the runtime assembly and would
      have broken player builds), compiled as `VectorFields.Editor`. `Tests/Editor/` compiles as
      `VectorFields.Tests.Editor`.
- [ ] **Convert the brush self-tests to NUnit.** They were a menu item only because the project had no asmdefs; now
      that `VectorFields.Tests.Editor` exists the conversion is mechanical (menu item kept for now).
- [ ] **Wrap public types in a `VectorFields` namespace.** The project is currently global-namespace; for distribution,
      namespacing avoids collisions with consumer code. Do it together with the asmdefs. (The map-family collision with
      UnityX that would otherwise force this early was sidestepped by giving the vendored maps distinct names —
      `FieldMap`/`VectorFieldMap`/`ColorFieldMap` — so this is now purely a consumer-collision concern.)
- [x] **Removed the UnityX dependency (Phase 1 of the reorg).** Vendored + trimmed the map family into
      `Vector Field/FieldMap.cs` (`FieldMap<T>`/`VectorFieldMap`/`ColorFieldMap`, migrated `Point`→`Vector2Int`); folded
      `GridRenderer` into a serializable `GridTransform`; replaced the stray helpers (`ObjectX`, `DebugX`,
      `SerializableTransform`, `IEnumerableX.GetChanges`, `ComponentX`/`GetComponentsX`, `GetHierarchyIndex`,
      `BaseEditor<T>`, `GizmosX`, `BoundsX`, `Plane.TryGetHitPoint`, `Color.WithAlpha`) with self-contained code; and
      swapped `[EasyButtons.Button]` for custom editors. Only remaining UnityX use is the `[CurveRange]` /
      `[EnumFlagsButtonGroup]` inspector attributes (being removed separately).

## Architecture / component consolidation
- [x] **Rolled the Grid component into the Vector Field component.** The required UnityX `GridRenderer` is folded into a
      serializable `GridTransform` owned by each `VectorFieldComponent` (and the standalone `SmokeSimulationComponent`),
      so a single self-contained component is now a working field — grid size lives on the component (`grid.Size`).
      Existing scenes keep their now-orphaned `GridRenderer` components (left in place by design).
- [ ] **Investigate a single Vector Field component with multiple modes** instead of several distinct vector-field
      component types. Evaluate a mode enum (or similar) on one component vs. the current per-type components — weigh
      inspector clarity, serialization, and how much behaviour actually differs between the modes.

## Layer blending — scalar magnitude ops (zones) [designed, not started]
Motivation: "areas that add/multiply magnitude" (slow zones, boost zones, speed caps) currently require an awkward
recipe — a `Blend + Magnitude` stamp with a *reversed* falloff cookie, which only behaves over a uniform-magnitude
base because it lerps toward an absolute value instead of scaling. Root cause: the combiner has no multiplicative
op, and `Blend` is the wrong operator for attenuation. (`Add + Magnitude` zones already work fine with a normal
falloff — only reduce/scale is broken.)

Agreed design — give each `GroupVectorFieldComponent.VectorFieldLayer` **two op slots** instead of growing one enum:

- **`vectorOp`: None | Add | Blend** — the current `BlendMode`, renamed (`FormerlySerializedAs`). These are the
  coupled true-vector ops (superposition / lerp) that can't be decomposed per-aspect; the `components` mask keeps
  selecting the decomposed variants exactly as today.
- **`magnitudeOp`: None | Multiply | Add | Min | Max | Set** + a per-layer float `magnitudeValue` — a scalar
  post-op on the blended result's magnitude, applied after `vectorOp` inside the same layer blit. Direction is
  never touched.

Every scalar op uses one coverage-blended formula, where `w` = saturate(incoming layer's magnitude — i.e. its
normal, un-reversed falloff cookie used as *coverage*), `s` = layer strength, `k` = `magnitudeValue`:

    mag' = lerp(mag, op(mag, k), w · s)      // op: mag·k | mag+k | min(mag,k) | max(mag,k) | k (Set)

Because it lerps from the current magnitude, every op is identity where the falloff hits 0 — default cookie shape
works as-is, footprint edges are always seamless, no inverted masks needed anywhere. Multiply gives slow/boost/dead
zones (k = 0.3 / 2 / 0), Min a speed cap, Set an exact-speed zone (fully retires the reversed-cookie recipe).
A pure zone is `vectorOp: None + magnitudeOp: Multiply`; both slots on one layer compose (e.g. add turbulence AND
cap it in one layer).

- [ ] **Implement the two-op layer model.** Shader: `ApplyMagnitudeOp` after `BlendVectors` in
      `CombineVectorFields.shader` + keep the C# `BlendVector` mirror in sync. `VectorFieldCombiner.Layer` +
      `VectorFieldLayer` gain `magnitudeOp`/`magnitudeValue` (into the layer hash). Fix the skip condition: a layer
      is skipped only when *both* slots are no-ops (today `Component.None` skips outright, which would drop pure
      zones). Migration is near-zero: `blendMode {Add, Blend}` → `vectorOp` 1:1, `magnitudeOp` defaults None.
      Wire Multiply/Min/Set first; Add/Max are one line each once the plumbing exists.
- [ ] **Inspector: two dropdowns, each revealing only its own controls.** `components` hidden when `vectorOp` is
      None; `magnitudeValue` shown only when `magnitudeOp` is active (per-op label: ×, +, cap, floor, target).
      A zone should read as "Multiply ×0.3" and nothing else.
- [ ] Document the zero-magnitude caveat once on the enum: `Add`/`Max`/`Set` can't create flow where the field is
      zero (no direction to scale along — same caveat `Add + Magnitude` has today); Multiply/Min only reduce, so
      they're unaffected.
- Deliberately **not** building now (don't design them out): a `useIncomingMagnitudeAsValue` toggle
  (field-modulating-field, `k' = k · incomingMag` — reintroduces the edge-identity problem, which is why coverage
  semantics is the default); a third `directionOp` slot (RotateBy/RotateToward, same coverage-blend pattern) if the
  direction family ever grows; a dedicated ModifierZone component (rejected as a second concept — with Multiply,
  a zone is just a stamp layer).

## Cookie source
- [ ] **`invert` toggle on `VectorFieldCookieSource`** (~3 lines: bool, `1-m` in resolve/apply, content hash). Not
      needed for the zone work above (coverage semantics makes the default falloff correct), but independently
      useful for ring shapes and edge-weighted masks.
- [ ] **Paintable cookie mode** (`Mode.Painted` alongside Falloff/Curve/Texture): a scalar float map stored on the
      owning component (matching the fields-on-components convention), painted with the existing brush pipeline via
      a magnitude-only-style op, resolved by the cookie instead of a generated falloff. Key property: because the
      canvas lives on whatever component owns the cookie, a Multiply stamp zone can have a hand-painted *shape*
      while staying a transform-positioned, reusable scene object — painting composes with componentisation instead
      of replacing it. On a group it doubles as a whole-field painted mask. Orthogonal to the zone work.

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

## Renderers
- [x] **Two-tier shader menu paths.** All plugin shaders now live under `Vector Fields/<Renderer>/<Variant>`
      (`Flow Map/`, `LIC/`, `IBFV/`, `Flow-Aligned/`, `Debug/`, `Demos/` for the Smoke example); the internal
      `CombineVectorFields` blit shader moved to `Hidden/` (it's loaded by Resources path, not name). `Water Flow Lit`
      was renamed to `Flow Lit` and its shader moved into `Renderers/Flow Map/` (it's a flow-map variant).
- [x] **Tiered variants of every flow visualizer.** `Flow Lit (Tiered)`, `LIC (Tiered)`, `Flow-Aligned (Tiered)`, and
      `IBFV (Tiered)` shaders + renderers join the existing `Flow Map (Tiered)`: N looks keyed to the normalised speed
      axis (Texture2DArray + float[] tier uniforms, `VectorFieldSpeedTiers.cginc` bracket/blend), edited via the shared
      LODGroup-style tier bar (`VectorFieldTierBarGUI`, extracted from the flow-map editor). Texture-array packing is
      consolidated in `VectorFieldRendererUtils.BakeTextureArray`.
- [ ] **Demo materials/scenes for the new tiered renderers** — nothing in `Examples/` exercises them yet.
- [ ] **Sanity-pass the tiered defaults in-editor** (tier params were chosen to read well, not yet eyeballed live);
      check the tiered LIC cost on a big quad (it marches up to 2×).

## Debug renderer / settings
- [ ] Decide whether the density controls (variable resolution / spacing / max arrows — currently per-user in the
      scene-view overlay via `EditorPrefs`) should also live in the **Vector Fields** project-settings page as
      project-wide defaults, or stay per-user. (See `VectorFieldDebugSettingsProvider` / `VectorFieldDebugSettings`.)
- [ ] Minor: the project-settings page is labelled "Vector Fields" but the backing type/file are still
      `VectorFieldDebugProjectSettings` / `ProjectSettings/VectorFieldDebugSettings.asset` — rename for consistency if desired.
