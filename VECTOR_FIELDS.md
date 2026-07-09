# Vector Field System

A Unity toolkit for authoring, generating, simulating, blending, sampling, and visualizing 2D **vector fields** — grids where every cell holds a direction + magnitude. Fields drive forces (push particles/agents), flow visuals, and gameplay effects, and can be hand‑painted, generated procedurally, simulated as fluid, or blended together.

> This is a living reference, regenerated from the code. See [DOCS_GUIDE.md](DOCS_GUIDE.md) for how to rebuild it.

## Contents

- [Concepts](#concepts)
- [Quick start](#quick-start)
- [Field components](#field-components)
  - [VectorFieldComponent (base)](#vectorfieldcomponent-base)
  - [Drawable (painted) field](#drawable-painted-field)
  - [Noise field](#noise-field)
  - [Polygon field](#polygon-field)
  - [Spline field](#spline-field)
  - [Stamp field](#stamp-field)
  - [Simulated (fluid) field](#simulated-fluid-field)
  - [Group (blended) field](#group-blended-field)
- [Reading a field from code](#reading-a-field-from-code)
- [The painting tool](#the-painting-tool)
- [Brush ops](#brush-ops)
- [Runtime painting (code)](#runtime-painting-code)
- [Cookies (falloff masks)](#cookies-falloff-masks)
- [Brush emitters (code stamping)](#brush-emitters-code-stamping)
- [Combining fields in code](#combining-fields-in-code)
- [Grid data: FieldMap](#grid-data-fieldmap)
- [Procedural generators (code)](#procedural-generators-code)
- [Saving fields as assets](#saving-fields-as-assets)
- [Driving particles](#driving-particles)
- [Visualization & debugging](#visualization--debugging)
- [Utilities](#utilities)

---

## Concepts

- A **field** is a grid of `Vector2` values laid out on a plane. The component's transform sets the plane (the field's "up" is `transform.forward` / `planeNormal`); grid size and world↔cell mapping come from the component's own serialized `grid` (a **`GridTransform`**), so no separate grid component is needed.
- Every field is a **`VectorFieldComponent`** (a MonoBehaviour). Different field *types* subclass it (painted, noise, polygon, stamp, simulated, group).
- Fields render lazily: they re‑render only when something changes (transform, parameters, the grid). The result lives in a GPU `renderTexture`; a CPU mirror (`VectorFieldMap vectorField`) is produced only when a consumer asks for it.
- A field's vectors are in its **local space**. Sampling helpers return either the local vector or a world‑space vector (rotated by the component's orientation).
- Fields compose: parent several fields under a **Group** to blend them on the GPU.

---

## Quick start

Author a painted field and read a force from it:

1. Add a **DrawableVectorFieldComponent** to a GameObject (it owns its own grid; set the grid size on the component under **Grid**).
2. Select it, press **P** in the Scene view to activate the **Vector Field** tool, and paint (drag to draw, see [the painting tool](#the-painting-tool)).
3. Read the force at a position from gameplay code:

```csharp
[SerializeField] VectorFieldComponent field;

void OnEnable()  => field.RegisterCpuConsumer(this, immediate: false);
void OnDisable() => field.UnregisterCpuConsumer(this);

void FixedUpdate() {
    field.EnsureUpToDate();
    Vector3 force = field.EvaluateWorldVector(transform.position);
    rb.AddForce(force);
}
```

For one‑off or batch reads without registering a consumer, use [GPU sampling](#reading-a-field-from-code).

---

## Field components

All field types share the base knobs and sampling API in [VectorFieldComponent](#vectorfieldcomponent-base). Each type below adds its own way of *producing* the field.

### VectorFieldComponent (base)

The abstract base for every field. Owns the render/dirty cycle, the GPU `renderTexture`, optional CPU readback, and all sampling.

**Inspector**
- **Magnitude** & **Cookie** — the field's **output transform**. Together they scale the field's *rendered output*: `magnitude` is a uniform multiplier, `cookie` is an optional [falloff mask](#cookies-falloff-masks). They're applied once in `Render()` (after the field is produced), *not* baked into the component's internal/authored state — so a simulator's solver and a drawable's paint data never see them, but **every consumer does**: the GPU render texture, the group blend, the visualizer, and the read-back CPU field are all pre-scaled. (So `EvaluateVector`/`TrySample*` return the already-transformed value — they don't re-apply magnitude.)

**Common API**
- `SetDirty()` / `EnsureUpToDate()` — request a re‑render / guarantee the field is fresh before sampling.
- `RenderTexture renderTexture` — the encoded field on the GPU.
- `GridTransform grid`, `Vector3 planeNormal` — the grid (size + world↔cell conversions) and plane orientation. Convenience accessors mirror it: `Vector2Int GridSize`, `Matrix4x4 GridToWorldMatrix` / `GridToLocalMatrix`, `Vector2 WorldToGridPosition(Vector3)`. Set the grid size via `grid.Size` (default 64×64).

Sampling is covered in [Reading a field from code](#reading-a-field-from-code).

### Drawable (painted) field

`DrawableVectorFieldComponent` — a field you paint by hand with the [painting tool](#the-painting-tool), or write to from code.

- `VectorFieldMap PaintField` — the authored buffer (created at the current grid size on demand). Read/write it directly to paint from code.
- `MarkRegionDirty(RectInt gridRegion)` — after writing cells, report the touched rect so only that region re‑uploads.
- `Clear()` — zero the field.
- `LoadPaintField(VectorFieldMap source)` — seed the painted field from another field (e.g. bake a noise field into an editable one); resizes the grid to match.

### Noise field

`NoiseVectorFieldComponent` — a scrolling fractal‑noise flow.

**Inspector**
- **Noise sampler** — frequency/scale, octaves, persistence, lacunarity, offset.
- **Space** — `Local` (field fixed to the grid) or `World` (field flows past a moving grid).
- **Vortex angle** — rotates each vector around the plane normal (0 = toward the noise gradient, 90 = circulate, 180 = away).

### Polygon field

`PolygonVectorField` — every cell points toward (or away from) the nearest edge of a polygon. Good for obstacle‑aware flow and edge‑following.

**Inspector**
- **Polygon renderer** — the source shape.
- **Sides** — `Inside` / `Outside` flags; enable both to fill the whole grid.
- **Boundary flip** — reverse direction on one side (converge vs diverge).
- **Angle** — rotate vectors around the normal (0 = toward edge, 90 = circulate, 180 = away).
- **Inner / Outer falloff** — distance over which strength fades inside / outside the shape.

### Spline field

`SplineVectorFieldComponent` — traces a Unity spline: every cell takes its vector from the nearest point on the path. Good for rivers, roads, and guided flows.

The `com.unity.splines` package is an **optional** dependency: the VectorFields asmdef's `versionDefines` set the `VECTOR_FIELDS_SPLINES` scripting define while the package is installed, and this component only compiles under that define — install or remove the package and the component follows automatically. The underlying `SplineVectorFieldGenerator` is polyline-generic (it takes pre-flattened samples) and is always available.

**Inspector**
- **Spline container** — the spline(s) to trace (falls back to a `SplineContainer` on the same GameObject).
- **Direction mode** — `Flow` (vectors follow the path's tangent) or `Fixed` (every cell uses **Fixed direction**, in field‑local plane space).
- **Rotation** — rotates every vector around the plane normal (degrees). **Rotation along spline** (`SplineData<float>`) adds extra rotation authored at points along the spline, interpolated between them.
- **Falloff** — distance from the path over which strength fades to zero (0 = constant strength). **Falloff along spline** (`SplineData<float>`) multiplies it at points along the spline.
- **Samples per spline** — how finely each spline is flattened; raise it for tight curves.

Knot edits re‑render automatically (the component listens to `Spline.Changed`).

### Stamp field

`StampVectorFieldComponent` — a single procedural emitter stamped into the field: a uniform **directional** beam or a radial **spot/vortex**.

**Inspector**
- **Brush settings** — emitter type (Directional / Spot), direction angle, vortex angle, strength. (See [brush emitters](#brush-emitters-code-stamping).)

The base **Cookie** shapes its falloff.

### Simulated (fluid) field

`SimulatedVectorFieldComponent` — an incompressible 2D fluid simulation. Instead of a static field, it integrates a velocity field forward each frame, forming vortices and wakes. Runs while playing.

**Inspector (key knobs)**
- **Simulation FPS / Max substeps / Time scale** — fixed‑timestep cadence, hitch protection, and sim speed.
- **Pressure iterations** — incompressibility solve quality (20–40 typical).
- **Viscosity damp** — per‑step damping (1 = inviscid).
- **Advection mode** — `SemiLagrangian` (stable, diffuses) or `MacCormack` (sharper, ~2× cost).
- **Vorticity strength** — re‑injects small‑scale swirl to fight diffusion.
- **Force field** + **Force mapping** (`Stretched` / `DirectTexel` / `WorldSpace`) + **Force strength** — drive the sim with another field (e.g. a Noise or Stamp field as wind/fans).
- **Boundary mode** — `Wrap` (tiling), `Wall` (contained), `Open` (outflow); optional **obstacles** mask.
- **Output scale** — maps raw solver velocity into the encoded range.

Sample it like any other field.

### Group (blended) field

`GroupVectorFieldComponent` — both a field and a container. It collects descendant fields as **layers** and blends them on the GPU; the group's output is itself a field you can sample or nest in another group.

**Inspector — per layer**
- **Component** — the child field.
- **Strength** (0–1) and **Blend mode** — `Add` (sum) or `Blend` (lerp by strength).
- **Components** — whether this layer affects `Magnitude`, `Direction`, or both.
- **Alignment ramp** — scale a layer's strength by how aligned it is with the field below (e.g. only reinforce where layers agree).
- **Scale by field magnitude** — couple a layer's strength to the underlying flow speed.

Children are pulled fresh and projected into the group's frame before blending, so moving/rotating a child updates the blend automatically.

---

## Reading a field from code

A field's vectors are local‑space. Two ways to read them:

**CPU path** (best for many evaluations per frame, e.g. an agent steering): register as a consumer so the CPU mirror is kept up to date.

```csharp
field.RegisterCpuConsumer(this, immediate: false);   // call once (OnEnable)
// ...
field.EnsureUpToDate();
Vector2 local = field.EvaluateVector(worldPos);        // local-space force
Vector3 world = field.EvaluateWorldVector(worldPos);   // rotated into world space
Quaternion rot = field.EvaluateRotation(worldPos);     // facing along the field
// ...
field.UnregisterCpuConsumer(this);                     // call when done (OnDisable)
```

Pass `immediate: true` if you need the data the same frame you register; otherwise readback is async and `OnCpuDataReady` fires when it lands.

**GPU path** (no registration; good for occasional or batched reads):

```csharp
if (VectorFieldComponent.SupportsGPUSampling &&
    field.TrySampleWorldVector(worldPos, out Vector3 world)) {
    // use world
}

// batch (one readback covers all queries):
field.TrySampleVectors(positions, results);

// non-blocking:
field.SampleWorldVectorAsync(worldPos, v => { /* ... */ });
```

`TrySample*` blocks for a GPU readback and returns `false` if unsupported or not yet rendered.

---

## The painting tool

An `EditorTool` for painting a [Drawable field](#drawable-painted-field) in the Scene view.

- **Activate**: select a `DrawableVectorFieldComponent`, press **P** (or pick the tool in the toolbar). Settings persist across sessions.
- **Overlay** ("Vector Field Brush"): pick the **Mode** (grouped buttons — Paint / Magnitude / Shape), set **Size** and **Pressure**, choose the **Direction** behavior, and edit the **Shape** (cookie). A live B&W preview shows the cookie shape.
- **Gestures**:

| Gesture | Action |
|---|---|
| Drag | Paint with the active op |
| Action‑key + Drag (`Cmd`/`Ctrl`) | Temporary erase |
| Shift + Click | Stamp once |
| Action‑key + Scroll | Resize brush |
| `M` | Cycle mode |

**Direction** (shown only for ops that paint a direction — Draw/Add):
- **Flow** — `Follow stroke` (flow follows your drag) or `Fixed angle` (flow uses the emitter angle; the slider reads "Angle"). In Follow mode the slider reads "Stroke Rotation" (offset relative to the stroke).
- **Emitter** — `Directional` (uniform **Angle**) or `Spot` (radial, with **Swirl**).

---

## Brush ops

Each op decides how a brush touch combines with the field. They're grouped in the overlay and available to code via the registry.

| Op | Group | Effect |
|---|---|---|
| **Draw** | Paint | Set the field to the brush direction (magnitude = pressure, size‑independent) |
| **Add** | Paint | Add the brush vector (accumulates) |
| **Smudge** | Paint | Drag existing flow along the stroke (advection) |
| **Erase** | Paint | Fade toward zero |
| **Burn** | Magnitude | Increase magnitude |
| **Dodge** | Magnitude | Decrease magnitude |
| **Clamp** | Magnitude | Cap magnitude at the pressure value |
| **Normalize** | Magnitude | Set magnitude to the pressure value |
| **Repel** | Shape | Point outward from the brush center (burst) |
| **Attract** | Shape | Point inward to the center (sink) |
| **Swirl** | Shape | Circulate around the center (vortex) |

**Code‑driven painting.** Ops implement `IVectorFieldBrushOp` and run through the kernel. To paint a batch of cells with an op:

```csharp
var op = VectorFieldBrushOpRegistry.ById("draw");
if (VectorFieldBrushKernel.Apply(field.PaintField, cells, pressure, op, out RectInt dirty))
    field.MarkRegionDirty(dirty);
```

`cells` is a list of `VectorFieldBrushCell` (grid point + brush sample + stroke direction + center) that you produce for your brush shape. Ops are pure per‑cell functions, so they also run at runtime.

---

## Runtime painting (code)

Paint fields from gameplay — smooth, frame‑rate‑independent strokes and stamps built on the same [brush ops](#brush-ops) as the editor tool. Extension methods on `DrawableVectorFieldComponent` (in `Brush/VectorFieldPainting.cs`). Pair with a fade strategy (below) so transient effects don't accumulate forever.

**Configure a brush once, reuse it every frame.** `VectorFieldBrush` is a cheap value type:

```csharp
var brush = new VectorFieldBrush(
    VectorFieldBrushShape.Radial(softness: 0.6f),   // CPU radial falloff (0 = hard edge, 1 = fully soft)
    VectorFieldBrushOpRegistry.Draw,                 // any brush op — named accessors (Draw, Erase, Repel, ...)
    size: 2f,                                        // brush radius in WORLD units
    pressure: 1f);                                   // op strength / magnitude reference
```

Prefer the named op accessors (`VectorFieldBrushOpRegistry.Draw`, `.Repel`, …) over `ById("draw")`; `ById` is for ids that arrive as data (a serialized field), and warns if the id is unknown. The facades validate their arguments — a null field, an un‑initialised field (no grid yet), or a brush missing its shape/op throws a clear exception rather than silently doing nothing.

For a textured / directional brush, use `VectorFieldBrushShape.FromCookie(cookie, emitter)` — it builds the 2D brush map on the GPU from a cookie mask + a directional/spot emitter (the same pipeline the editor uses) and caches it on the CPU; build it once in setup and reuse. (`FromMap(map)` wraps a `VectorFieldMap` you already have.) The brush's `directionMode` controls how a stroke orients a map brush: `FollowStroke` (default) rotates the emitter to the path tangent; `FixedAngle` keeps the map's baked direction. The in‑editor [painting tool](#the-painting-tool) runs on exactly this API, so editor and runtime strokes are the same code.

**One‑shots** (stateless, no allocation):

```csharp
field.Stamp(brush, worldPos);                 // a single dab / burst
field.Stamp(brush, worldPos, facing);         // directional dab: Draw/Add paint `facing`; radial ops ignore it
field.PaintLine(brush, origin, target);       // a straight swept line
```

**Continuous stroke** — hold it and feed world positions each frame; the path is splined (centripetal Catmull‑Rom), so the line stays smooth and identical at any frame rate:

```csharp
VectorFieldStroke _stroke;
void OnEnable()    => _stroke = field.BeginStroke(brush);
void FixedUpdate() => _stroke.To(transform.position);
void OnDisable()   => _stroke.End();
```

`TipMode` (the brush's last argument) controls the moving head:

| Mode | Behavior | Use for |
|---|---|---|
| **Smoothed** (default) | ~1 point of lag; head follows the smoothed spline. Tail flushed on `End()`. | Trails, wakes — smoothest |
| **Leading** | Zero lag; head extrapolated to the newest point. | Beams / visible heads where lag would read as latency |

**Fade strategies** — a transient effect needs something to drain the field:

| Strategy | How | When |
|---|---|---|
| **Decay‑in‑place** | add `VectorFieldDecay` to the field (multiplies toward zero each frame) | Default; cheapest, cost independent of effect count |
| **Simulator** | feed the drawable into `SimulatedVectorFieldComponent.forceField` | Natural dissipation (spread, vortices) |
| **Group layers** | one drawable per effect as a `GroupVectorFieldComponent` layer; fade its `strength` | Per‑effect fade curves |

Runnable examples in `Assets/Vector Fields/Examples/`: `Demo_VectorFieldTrail`, `Demo_VectorFieldBurst`, `Demo_VectorFieldBeam`, `Demo_VectorFieldWind`, `Demo_VectorFieldVortex`, `Demo_VectorFieldSimFade`, `Demo_VectorFieldGroupFade`.

Strokes are **frame‑rate independent**: the painted result depends only on the stroke's geometry (identical at 30/60/144 fps). Effect builds up with drag **distance** — a cell ramps up as the brush sweeps across it and reaches full after roughly a brush‑width of travel (like a real brush), so a slight nudge makes a slight mark. Holding still adds nothing (no distance travelled), and each cell's op is applied once from its pre‑stroke value, so nothing double‑applies and span joins are seam‑free.

Full design in [Assets/Vector Fields/Brush/RUNTIME_PAINTING_SPEC.md](Assets/Vector%20Fields/Brush/RUNTIME_PAINTING_SPEC.md).

---

## Cookies (falloff masks)

`VectorFieldCookieSource` shapes a brush stamp or masks a whole field's strength.

- **Mode** — `None`, `Falloff` (radial **softness** 0–1), `Curve` (radial profile via `AnimationCurve`), `Texture` (red channel = mask).
- `Resolve(Vector2Int size)` → the mask texture (generated on demand for Falloff/Curve).
- `Apply(RenderTexture target, Vector2Int size, float strength = 1, RectInt? region = null)` → apply a field's output transform in place: multiply its strength by `strength` (the field's magnitude) **and** by this cookie's mask. `strength` alone (with `Mode.None`) is a pure magnitude scale; `region` limits the pass to a sub-rect (the drawable's region upload uses this). No-op when `strength ≈ 1` and mode is `None`.
- `Dispose()` → release generated textures.

```csharp
var cookie = new VectorFieldCookieSource { mode = VectorFieldCookieSource.Mode.Falloff, falloffSoftness = 0.7f };
cookie.Apply(fieldRenderTexture, gridSize);
```

---

## Brush emitters (code stamping)

`VectorFieldBrushSettings` defines what a brush *emits*; `VectorFieldBrushTextureCreator` renders it.

- **Force type** — `Directional` (uniform angle) or `Spot` (radial/vortex).
- **Directional angle** / **Vortex angle**.

```csharp
var settings = new VectorFieldBrushSettings { forceType = VectorFieldBrushSettings.ForceEmitterType.Directional, directionalAngle = 45f };
VectorFieldBrushTextureCreator.Dispatch(target, gridSize, magnitude, settings, cookie.Resolve(gridSize));
```

`target` must be a valid random‑write `ARGBFloat` texture — see [Utilities](#utilities).

---

## Combining fields in code

`VectorFieldCombiner` blends field render textures as layers (the engine behind [Group fields](#group-blended-field)).

```csharp
var layers = new List<VectorFieldCombiner.Layer> {
    new() { field = windRT,  strength = 1f, blendMode = VectorFieldCombiner.BlendMode.Add,  components = VectorFieldCombiner.Component.All,       localToWorldMatrix = windXf },
    new() { field = brushRT, strength = 1f, blendMode = VectorFieldCombiner.BlendMode.Blend, components = VectorFieldCombiner.Component.Direction, localToWorldMatrix = brushXf },
};
VectorFieldCombiner.Combine(target, gridSize, groupLocalToWorld, layers);
```

Each layer is projected into the group's frame, so layer transforms tilt/scale their contribution. Optional `alignmentRamp` and `scaleByFieldMagnitude` weight a layer per cell.

---

## Grid data: FieldMap

`VectorFieldMap` (a `FieldMap<Vector2>`) is the CPU grid structure under every field; the smoke sim uses `ColorFieldMap` (a `FieldMap<Color>`). A map is a `Vector2Int size` plus a flat `values` array with bilinear sampling.

```csharp
var map = new VectorFieldMap(new Vector2Int(64, 64));

Vector2 a = map.GetValueAtGridPoint(10, 5);              // direct cell (fastest)
Vector2 b = map.GetValueAtGridPosition(new Vector2(10.4f, 5.2f)); // bilinear interpolation
Vector2 c = map.GetValueAtNormalizedPosition(new Vector2(0.5f, 0.5f)); // [0,1] space

map.SetValueAtGridPoint(10, 5, Vector2.up);              // or SetValueAtGridPoint(new Vector2Int(10, 5), …) — bounds-checked
map.ClampMagnitude(1f);                                   // whole-map op (VectorFieldMap only)
map.Fill(Vector2.zero);
map.Clear();
var copy = map.CloneMap();                                // deep copy, keeps the subtype
```

Access `map.values` directly for bulk work (encode/decode, `Array.Copy`).

---

## Procedural generators (code)

Both generate into a GPU texture you own:

```csharp
// Fractal noise flow
NoiseVectorField.Dispatch(target, gridSize, gridToSampleMatrix, noiseProps, vortexAngle, magnitude);

// Field pointing toward polygon edges
PolygonVectorFieldGenerator.Dispatch(target, ref vertexBuffer, gridSize, verts, gridToPolyLocal,
                                      polyToFieldVector, sides, boundaryFlip, innerFalloff, outerFalloff, angle, magnitude);
```

These are what the [Noise](#noise-field) and [Polygon](#polygon-field) components call internally; use them directly for custom pipelines. The polygon generator's `vertexBuffer` is caller‑owned (create once, release when done).

---

## Saving fields as assets

`VectorFieldScriptableObject` stores a field as a `Texture2D` asset and round‑trips it to a `VectorFieldMap`.

```csharp
VectorFieldMap map = asset.CreateMap();   // load from texture
// ...edit map...
asset.Save(map);                       // re-encode + write texture (auto-computes encoding range)
```

Encoding is lossy (vectors packed into color), scaled by `maxComponent`.

---

## Driving particles

`ParticleSystemVectorField` (alongside a `ParticleSystem`) makes Unity particles follow a field via a `ParticleSystemForceField`.

- **Vector field component** — the field to follow.
- **Amplitude curve** — reshape magnitude (baked to a LUT).

It bakes the field into a 3D texture the force field reads, and refreshes when the field changes. Companion scripts: **KillOutOfBoundsParticles** (cull particles outside a box / the shape module, with an `extraBoundaryDistance` buffer) and **KillZeroSpeedParticles** (cull stalled particles).

---

## Visualization & debugging

- **VectorFieldDebugOverlay** — a Scene‑view overlay (visible when a field is selected) to toggle arrow visualization: **Variable resolution**, **Spacing**, **Max arrows**.
- **VectorFieldDebugRenderer** — draws the field as instanced arrows straight from the GPU texture (no readback), with zoom‑responsive density. Call `Draw(field, opacity, camera, variableResolution, targetSpacingPixels, maxArrows)`.
- **VectorFieldTextureRenderer** — shows the raw field as a flow‑visualization quad in world space; supports an **amplitude→alpha** curve and a **color gradient** recolor, with a `depthOffset` for layering. Also the simplest way to feed a field into *your own* shader (see below).
- **Water Flow Map** (`Vector Fields/Water Flow Map` shader) — scrolls a texture along the field per‑pixel, the classic ping‑pong flow‑map look. Point a `VectorFieldTextureRenderer` at a field, put a material using this shader on the same quad, and assign your image to `_WaterTex`. Optional dual‑scale second layer breaks up tiling.
- **Water Flow Lit** (`Vector Fields/Water Flow Lit` shader) — same ping‑pong flow, but derives a normal from the flowed texture (as a heightfield) and lights it, so moving specular/shading ride the field. Self‑contained lighting (its own `_LightDir`); best with a smooth wavy `_WaterTex`. Wiring as above.
- **Flow (IBFV)** — `VectorFieldFlowIBFV` component + shader: a seam‑free flowing‑streak visualization (van Wijk 2002) built by advecting a feedback buffer along the flow and injecting animated noise. Runs in play mode; tunables (`flowStep`, `noiseAmount`, `noiseScale`, `noiseRate`) are on the component.
- **LIC** (`Vector Fields/Vector Field LIC` shader) — Line Integral Convolution: the classic dense "combed along the flow" picture of a field. Stateless (recomputed per frame), so it's crisp and never washes out. Point a `VectorFieldTextureRenderer` at a field, put a material using this shader on the quad, and assign a tiling white‑noise texture to `_NoiseTex`. Keep `_NoiseScale` low (a few px per noise texel).

### Consuming a field in your own shader

A field is published as a live GPU texture — `VectorFieldComponent.renderTexture`, an `ARGBFloat` render texture that compute shaders write directly (no CPU readback). Two things to know to sample it:

- **Binding.** Add a **VectorFieldTextureRenderer** to your quad and point it at the field; it pushes the live texture into your material's `_MainTex` via a `MaterialPropertyBlock` every time the field re‑renders (including after a resize), so you don't touch the shared material. If you'd rather bind it yourself, call `material.SetTexture("_MainTex", field.renderTexture)` — but do it on the component's `OnRendered` event, since the texture reference can change on a resize.
- **Encoding.** The vector is stored in the **R,G** channels remapped to 0–1 as `rg = vector * 0.5 + 0.5` (B,A unused), normalized so the field's max component maps to ±1 — magnitudes beyond that clamp. Decode in the shader with:

  ```hlsl
  float2 flow = (tex2D(_MainTex, uv).rg - 0.5) * 2.0;   // signed velocity, roughly [-1, 1]
  ```

  The built‑in flow shaders negate this (`-(rg - 0.5)`) so apparent motion matches the debug arrows; match that sign if you want your effect to flow the same direction as the others.

> **Legacy:** the particle‑based debug views under `Assets/Vector Fields/Visualisation/` (`MapDebugView`, `VectorFieldDebugView`, `VectorFieldDotsDebugView`, `VectorFieldParticleRenderer`, `LocalisedGridParticles`) depend on a `VectorFieldManager` singleton that now lives only under `Assets/Legacy/`, so they don't work with the current component model. They're omitted here pending retirement or a port.

---

## Utilities

- **VectorFieldRenderTextureUtils** — lifecycle for the `ARGBFloat` render textures fields use. Call `EnsureValid(ref rt, size)` before rendering (cheap if already valid) and `Destroy(ref rt)` on teardown to avoid GPU leaks.
- **VectorFieldUtils** — encode/decode between `Vector2[]` and `Color[]` (`VectorsToColors` / `ColorsToVectors`, scaled by a max component), build ramp textures from an `AnimationCurve` or `Gradient`, and convert a field to a `Texture2D` / `Texture3D`. In‑place overloads avoid allocation in hot loops.
