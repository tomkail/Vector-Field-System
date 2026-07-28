# TODO — Vector Field System

A living checklist for the Vector Field System. For full context see `HANDOVER.md`, `VECTOR_FIELDS.md`, and
`Assets/Vector Fields/Brush/RUNTIME_PAINTING_SPEC.md`. Keep `VECTOR_FIELDS.md` in sync (per `DOCS_GUIDE.md`) when the
public API changes.

## Architecture / component consolidation
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
- [ ] **Paintable cookie mode** (`Mode.Painted` alongside Falloff/Curve/Texture): a scalar float map stored on the
      owning component (matching the fields-on-components convention), painted with the existing brush pipeline via
      a magnitude-only-style op, resolved by the cookie instead of a generated falloff. Key property: because the
      canvas lives on whatever component owns the cookie, a Multiply stamp zone can have a hand-painted *shape*
      while staying a transform-positioned, reusable scene object — painting composes with componentisation instead
      of replacing it. On a group it doubles as a whole-field painted mask. Orthogonal to the zone work.

## Demos
- [ ] Validate `Demo_VectorFieldSimFade` impulse-clear timing and `Demo_VectorFieldGroupFade` field pool in-editor.
- [ ] Build scenes for the additional demos (directional beam, persistent wind via sim, vortex field) — the
      `Demo_VectorFieldBeam`/`Wind`/`Vortex` scripts exist (each guards its fade-strategy dependency via
      `WarnIfNoFadeStrategy`) but aren't referenced by any scene yet.

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
- [ ] **River / racetrack current.** `PolygonVectorField` defines a channel; rigidbody props get swept downstream by
      force sampled from the field. Exercises the polygon source + real physics (both currently unshown).
- [ ] **Player in the current.** Top-down character pushed by the field — headwind, sweeping current, a Repel "force
      push" ability. Most relatable gameplay pitch.
- [ ] **Coloured smoke** (existing Smoke demo) — polish/keep as the fluid-sim + colour-advection flagship.
- [ ] **Layered environment.** Group source: base noise breeze + painted gust + simulated eddy → one particle/flow output.
      The "production-realistic" composition demo.
- [ ] **Ambient beauty pass.** Leaves/snow/petals on a noise field — the low-effort screenshot/gif that sells at a glance.

## Renderers
- [ ] **Sanity-pass the tiered defaults in-editor** (tier params were chosen to read well, not yet eyeballed live);
      check the tiered LIC cost on a big quad (it marches up to 2×). The Rendering Demo scene exercises the tiered
      Flow Map / LIC / Flow-Aligned materials; the `Vector Field Flow IBFV (Tiered)` material exists but isn't
      referenced by the scene yet — wire it in while passing.

## Debug renderer / settings
- [ ] Decide whether the density controls (variable resolution / spacing / max arrows — currently per-user in the
      scene-view overlay via `EditorPrefs`) should also live in the **Vector Fields** project-settings page as
      project-wide defaults, or stay per-user. (See `VectorFieldDebugSettingsProvider` / `VectorFieldDebugSettings`.)