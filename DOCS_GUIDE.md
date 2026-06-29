# How to (re)build the Vector Field docs

This describes how to regenerate [VECTOR_FIELDS.md](VECTOR_FIELDS.md) — the user‑facing reference for the Vector Field System — from the current code. It's written so an AI agent (or a person) can recreate the doc faithfully whenever the project changes. The doc is a **living document**: the code is the source of truth; `VECTOR_FIELDS.md` is derived.

**To run it:** hand this file to an agent — *"Follow `DOCS_GUIDE.md` to regenerate `VECTOR_FIELDS.md` from the current code."* That's the whole prompt. Do all the reading/writing in one agent (don't stop after delegating); the deliverable is the written file.

## Goal & audience

`VECTOR_FIELDS.md` is for someone *using* the system — adding components in the Inspector, sampling fields from gameplay code, painting, and wiring up generators. It is **not** an internals/architecture doc.

## House style (follow these)

1. **Usage‑first.** Document what a user sets and calls: Inspector fields, public methods/properties, setup steps. Describe behavior and *when to use* each feature.
2. **No internals.** Don't explain how the code works inside (render loops, shader math, dirty‑tracking, buffer management). The code shows that.
3. **Snippets only when they clarify usage.** Add a short code snippet when calling convention or setup order isn't obvious (sampling, painting from code, combining, generators). Do **not** add code that merely restates what a method does. Keep snippets minimal and correct.
4. **One section per feature.** Group related features; keep each section tight. Maintain the table of contents.
5. **Accuracy over completeness.** Use real type/member names and signatures from the code. List only the *meaningful* Inspector fields, not every serialized field. If unsure of an exact name, verify in the code rather than guessing.
6. **Readable.** Short paragraphs, tables for enumerations (ops, gestures, modes), and links to related sections.
7. **Open with orientation.** Start the doc with a short **Concepts** section (the mental model: field = grid of vectors on a plane, component model, lazy GPU render + optional CPU mirror, local vs world space) and a **Quick start** (add a Drawable field → paint → read a force). Then the per‑feature sections.

## Source of truth

Everything lives under `Assets/Vector Fields/`, plus a few editor tools it depends on under `Assets/UnityX/`. Map sections to code like this:

| Doc section | Code |
|---|---|
| Field components | `Vector Field/Components/` (`VectorFieldComponent` + subclasses: Drawable, Noise, Polygon, Stamp, Simulated, Group) |
| Reading a field from code | `Vector Field/Components/VectorFieldComponent.cs` (Register/Unregister CpuConsumer, Evaluate*, TrySample*, SampleWorldVectorAsync) |
| Painting tool | `VectorFieldWorldEditor/VectorFieldDrawingTool.cs`, `VectorFieldDrawingToolSettingsOverlay.cs` |
| Brush ops | `Brush/Ops/VectorFieldBrushOps.cs` (op list + `Tooltip`/group), `VectorFieldBrushOp.cs` (interface), `VectorFieldBrushKernel.cs` (Apply) |
| Cookies | `Brush/VectorFieldCookieSource.cs` |
| Brush emitters | `Brush/VectorFieldBrushSettings.cs`, `Brush/VectorFieldBrushTextureCreator.cs` |
| Combining fields | `Vector Field/VectorFieldCombiner.cs` |
| Grid data | `Assets/UnityX/Scripts/Extensions/Grid/Grid 2D/Map Types/Vector2Map.cs` + `TypeMap.cs` |
| Procedural generators | `Vector Field/NoiseVectorField.cs`, `Vector Field/PolygonVectorFieldGenerator.cs` |
| Saving as assets | `SO/VectorFieldScriptableObject.cs`, `SO/TypeMapScriptableObject.cs` |
| Driving particles | `Particles/ParticleSystemVectorField.cs`, `KillOutOfBoundsParticles.cs`, `KillZeroSpeedParticles.cs` |
| Visualization & debugging | `Debug Renderer/`, `Vector Field/Components/Editor/VectorFieldDebugOverlay.cs`, `Texture Renderer/`, `Visualisation/` |
| Utilities | `Vector Field/VectorFieldRenderTextureUtils.cs`, `Vector Field/VectorFieldUtils.cs` |

Related design docs that the reference should *link to*, not absorb:
- `Assets/Vector Fields/Brush/RUNTIME_PAINTING_SPEC.md` — the planned runtime stroke‑painting layer (mark as "not yet implemented" until it ships).

## Procedure

1. **Re‑inventory.** List `Assets/Vector Fields/**/*.cs`. Diff against the section→code table above; add sections for new subsystems, remove sections for deleted ones.
2. **Extract the public surface** for each area. Work through three areas in turn (do it yourself; if you delegate to sub‑agents, you must still gather their results and write the final file — don't stop after delegating):
   - **Components** — for each `VectorFieldComponent` subclass: purpose, meaningful Inspector fields, public methods, and how they relate (group/blend/sampling).
   - **Core/utilities/cookie** — `Vector2Map`/`TypeMap` read‑write API, `VectorFieldUtils`, `VectorFieldCombiner`, `VectorFieldRenderTextureUtils`, `VectorFieldCookieSource`, brush emitter (`BrushSettings`/`TextureCreator`), `NoiseVectorField`/`PolygonVectorFieldGenerator`, ScriptableObject storage.
   - **Tools/visualisation** — drawing tool + overlay, brush ops + kernel API, particles, debug/visualisation renderers, texture renderer.
   For each item capture: one‑line purpose, the Inspector fields a user sets, the public methods/signatures they'd call, and whether a snippet helps.
3. **Write/update sections** per the house style. Prefer updating existing sections in place to preserve structure and links.
4. **Verify** (below) before finishing.

## What to exclude

- Editor‑only plumbing: custom `PropertyDrawer`s (`Brush/Editor/*`), inspectors/editors, gizmo drawing.
- Internal helpers, private fields, and anything not part of the user‑facing API.
- Commented‑out / disabled features (e.g. the Shapes‑package debug renderer) — skip until active.
- Test/scratch scripts (e.g. `*Tester`, EXR test) unless they're a real feature.
- **Legacy code that depends on `VectorFieldManager`** (now only under `Assets/Legacy/`) — notably the particle‑based debug views under `Assets/Vector Fields/Visualisation/`. They don't work with the current `VectorFieldComponent` model; omit them with a one‑line "legacy" note rather than documenting a broken API. (Check with `grep -rl VectorFieldManager "Assets/Vector Fields"` — any hit is legacy.)

## Verification

- Every type and method name in the doc exists in the code with the stated signature. Spot‑check `grep` for renamed/removed members (e.g. brush op `Id` strings, `Evaluate*`/`TrySample*`, `MarkRegionDirty`, `PaintField`).
- The brush‑ops table matches `VectorFieldBrushOpRegistry.Groups` order and each op's `DisplayName`/`Tooltip`.
- The painting‑tool gestures and shortcut keys match `VectorFieldDrawingTool` (`[Shortcut]` attributes) and the overlay.
- Snippets are syntactically plausible and use real APIs; they don't need to compile verbatim but must not reference nonexistent members.
- The TOC links resolve.
- Note that nothing here is compiled by the doc process — if a referenced API was changed in an uncommitted/unverified state, flag it rather than documenting a broken signature.

## When to regenerate

Re‑run when any of these change: a field component is added/removed or gains/loses Inspector knobs; the brush op set or registry groups change; the painting tool's gestures/overlay change; the cookie modes, combiner layer options, or sampling API change; or the runtime painting layer ships (move it from "spec / not implemented" to a real section).
