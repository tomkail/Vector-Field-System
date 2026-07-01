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
- [Grid data: Vector2Map](#grid-data-vector2map)
- [Procedural generators (code)](#procedural-generators-code)
- [Saving fields as assets](#saving-fields-as-assets)
- [Driving particles](#driving-particles)
- [Visualization & debugging](#visualization--debugging)
- [Utilities](#utilities)

---

## Concepts

- A **field** is a grid of `Vector2` values laid out on a plane. The component's transform sets the plane (the field's "up" is `transform.forward` / `planeNormal`); grid size and cell layout come from a required **GridRenderer**.
- Every field is a **`VectorFieldComponent`** (a MonoBehaviour). Different field *types* subclass it (painted, noise, polygon, stamp, simulated, group).
- Fields render lazily: they re‑render only when something changes (transform, parameters, the grid). The result lives in a GPU `renderTexture`; a CPU mirror (`Vector2Map vectorField`) is produced only when a consumer asks for it.
- A field's vectors are in its **local space**. Sampling helpers return either the local vector or a world‑space vector (rotated by the component's orientation).
- Fields compose: parent several fields under a **Group** to blend them on the GPU.

---

## Quick start

Author a painted field and read a force from it:

1. Add a **GridRenderer** + **DrawableVectorFieldComponent** to a GameObject (the grid component is required and auto‑added).
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
- **Magnitude** — multiplies the field's output strength at sample time.
- **Cookie** — an optional [falloff mask](#cookies-falloff-masks) applied to the whole rendered field.

**Common API**
- `SetDirty()` / `EnsureUpToDate()` — request a re‑render / guarantee the field is fresh before sampling.
- `RenderTexture renderTexture` — the encoded field on the GPU.
- `GridRenderer gridRenderer`, `Vector3 planeNormal` — grid geometry and orientation.

Sampling is covered in [Reading a field from code](#reading-a-field-from-code).

### Drawable (painted) field

`DrawableVectorFieldComponent` — a field you paint by hand with the [painting tool](#the-painting-tool), or write to from code.

- `Vector2Map PaintField` — the authored buffer (created at the current grid size on demand). Read/write it directly to paint from code.
- `MarkRegionDirty(RectInt gridRegion)` — after writing cells, report the touched rect so only that region re‑uploads.
- `Clear()` — zero the field.
- `LoadPaintField(Vector2Map source)` — seed the painted field from another field (e.g. bake a noise field into an editable one); resizes the grid to match.

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
    VectorFieldBrushOpRegistry.ById("draw"),        // any brush op
    size: 2f,                                        // brush radius in WORLD units
    pressure: 1f);                                   // op strength / magnitude reference
```

For a textured / directional brush, use `VectorFieldBrushShape.FromMap(map)` — a 2D `Vector2Map` whose sampled vector is the brush contribution (magnitude = weight, direction = the emitter/cookie direction). The brush's `directionMode` then controls how a stroke orients it: `FollowStroke` (default) rotates the emitter to the path tangent; `FixedAngle` keeps the map's baked direction. The in‑editor [painting tool](#the-painting-tool) runs on exactly this API (its cookie‑shaped emitter wrapped via `FromMap`), so editor and runtime strokes are the same code.

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
- `Apply(RenderTexture target, Vector2Int size)` → multiply a rendered field's strength by the mask in place.
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

## Grid data: Vector2Map

`Vector2Map` (a `TypeMap<Vector2>`) is the CPU grid structure under every field.

```csharp
var map = new Vector2Map(new Point(64, 64));

Vector2 a = map.GetValueAtGridPoint(10, 5);              // direct cell (fastest)
Vector2 b = map.GetValueAtGridPosition(new Vector2(10.4f, 5.2f)); // bilinear interpolation
Vector2 c = map.GetValueAtNormalizedPosition(new Vector2(0.5f, 0.5f)); // [0,1] space

map.SetValueAtGridPoint(10, 5, Vector2.up);
map.ClampMagnitude(1f);                                   // whole-map op
map.Clear();

foreach (var cell in map) { /* cell.point, cell.value */ }
```

Also offers whole‑map arithmetic (`Add`/`Subtract`/`Multiply`/`Divide` with a scalar, vector, or another map) for blending fields on the CPU.

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

`VectorFieldScriptableObject` stores a field as a `Texture2D` asset and round‑trips it to a `Vector2Map`.

```csharp
Vector2Map map = asset.CreateMap();   // load from texture
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
- **VectorFieldTextureRenderer** — shows the raw field as a flow‑visualization quad in world space; supports an **amplitude→alpha** curve and a **color gradient** recolor, with a `depthOffset` for layering.

> **Legacy:** the particle‑based debug views under `Assets/Vector Fields/Visualisation/` (`MapDebugView`, `VectorFieldDebugView`, `VectorFieldDotsDebugView`, `VectorFieldParticleRenderer`, `LocalisedGridParticles`) depend on a `VectorFieldManager` singleton that now lives only under `Assets/Legacy/`, so they don't work with the current component model. They're omitted here pending retirement or a port.

---

## Utilities

- **VectorFieldRenderTextureUtils** — lifecycle for the `ARGBFloat` render textures fields use. Call `EnsureValid(ref rt, size)` before rendering (cheap if already valid) and `Destroy(ref rt)` on teardown to avoid GPU leaks.
- **VectorFieldUtils** — encode/decode between `Vector2[]` and `Color[]` (`VectorsToColors` / `ColorsToVectors`, scaled by a max component), build ramp textures from an `AnimationCurve` or `Gradient`, and convert a field to a `Texture2D` / `Texture3D`. In‑place overloads avoid allocation in hot loops.
