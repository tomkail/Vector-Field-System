# Vector Field Runtime Painting — Tier-2 Spec

Status: ready to build. Tier-1 (ops + apply kernel) already exists in `Brush/Ops/`. This spec covers the
runtime-usable painting layer (Tier 2) and the friendly facades (Tier 3) shared by gameplay and the editor.

## Goals

- One painting API usable identically at **runtime (effects)** and in the **editor tool**.
- **Smooth lines, not dabbing**: a stroke is a swept shape, not a row of stamps. No beading.
- **Frame-rate independent**: the painted result depends on the stroke's points, not on how many frames
  delivered them. Identical at 30 / 60 / 144 fps.
- **Naturally curving**: the path is splined, not a corner-y polyline — and the painted *vector direction*
  follows the smooth tangent, not piecewise-constant segment directions.
- **Efficient / effects-grade**: zero per-frame allocation, pooled scratch, band-only cell visitation.

## Non-goals

- Undo (editor-only; stays in the tool, never in the runtime path).
- Field decay/fade for trails (a separate full-field tick; noted under Future).
- GPU/Burst execution (CPU main-thread now; ops are pure to keep that door open — see Future).

---

## Architecture

```
Tier 1  core kernel (exists)
  IVectorFieldBrushOp        per-cell pure op: Apply(in BrushApplyContext)
  BrushApplyContext          inputs for one cell
  VectorFieldBrushCell       one touched cell + the brush's contribution
  VectorFieldBrushKernel     apply cells to a field, report dirty rect   (renamed; see Migration)

Tier 2  runtime painting (new)
  VectorFieldBrushShape      brush profile: 2D map (stamps) + 1D radial falloff (sweeps)
  VectorFieldBrush           reusable config: shape + op + size + pressure + tipMode
  IStrokeCellSource          produces cells for a stamp or a swept span
    PointStampSource         samples the 2D map at a point (textured/directional brushes)
    SweptPathSource          rasterizes a swept capsule/spline with radial coverage
  VectorFieldStroke          stateful stroke (struct): point ring buffer + arc cursor + overlap state

Tier 3  facades
  DrawableVectorFieldComponent.Stamp / PaintLine / BeginStroke
  VectorFieldDrawingTool     editor: input + Undo over the same Tier-2 API
```

Tier 1 stays the law: every painting path ends in `VectorFieldBrushKernel.Apply(field, cells, op)`. The only
thing that varies is **which cell source** fed it.

---

## Kernel generalization (change to Tier 1)

Today `ApplyStroke(field, cells, strokeForce, pressure, brushCenter, op, out dirty)` passes `strokeForce` and
`brushCenter` as **batch** params — one value for all cells. A curved sweep needs them **per cell** (the tangent
and nearest-path-point vary along the stroke). Move them into the cell:

```csharp
struct VectorFieldBrushCell {
    Point   gridPoint;
    Vector2 brushForce;   // coverage-weighted brush sample; magnitude == 0..1 weight
    Vector2 finalForce;   // direction * magnitude * weight
    Vector2 strokeForce;  // local stroke vector (tangent * step magnitude)   [was batch]
    Vector2 brushCenter;  // nearest path point, grid space                   [was batch]
}

// kernel becomes:
bool VectorFieldBrushKernel.Apply(Vector2Map field, IReadOnlyList<VectorFieldBrushCell> cells,
                                  float pressure, IVectorFieldBrushOp op, out RectInt dirty);
```

`BrushApplyContext` is built from the cell + current value + pressure + source. Point stamps just write the same
`strokeForce`/`brushCenter` into every cell, so this is backward compatible — it only *adds* the ability for the
direction to vary per cell, which is what makes curves smooth.

---

## Public API (Tier 2/3)

```csharp
// --- brush shape: built once, cached, reused forever -------------------------------------------------
sealed class VectorFieldBrushShape {
    static VectorFieldBrushShape Radial(float softness);                  // pure CPU, no GPU
    static VectorFieldBrushShape FromCookie(VectorFieldCookieSource c,    // GPU-generated 2D map,
                                            VectorFieldBrushSettings s);  // built once + cached
    Vector2 Sample2D(Vector2 normalizedPos);   // for PointStampSource (textured/directional)
    float   RadialFalloff(float normalizedDistance);  // for SweptPathSource (0..1)
}

enum TipMode { Smoothed, Leading }   // Smoothed = 1-frame Catmull-Rom lag; Leading = zero-lag extrapolated tip

// --- the reusable brush config -----------------------------------------------------------------------
struct VectorFieldBrush {              // value type holding refs (shape, op) + scalars; built once
    VectorFieldBrushShape shape;
    IVectorFieldBrushOp   op;
    float   size;                       // grid-space diameter
    float   pressure;                   // magnitude reference (Draw caps at this; Clamp/Normalize target it)
    TipMode tipMode;                    // default Smoothed; set Leading for beams / visible heads
}

// --- one-shots (stateless, zero-alloc) ---------------------------------------------------------------
void DrawableVectorFieldComponent.Stamp(in VectorFieldBrush brush, Vector2 worldPos);
void DrawableVectorFieldComponent.PaintLine(in VectorFieldBrush brush, Vector2 fromWorld, Vector2 toWorld);

// --- continuous stroke (struct, zero-alloc, frame-rate independent) ----------------------------------
VectorFieldStroke DrawableVectorFieldComponent.BeginStroke(in VectorFieldBrush brush, Vector2 worldPos);
// then, each frame, on the stored stroke variable:
stroke.To(Vector2 worldPos);
stroke.End();                           // returns pooled snapshot/coverage buffers, if any
```

### Usage — the two driving cases

```csharp
// field behind the player — continuous, smooth, frame-rate independent
VectorFieldStroke trail;                          // a field on the component (mutable struct)
void OnEnable()  => trail = field.BeginStroke(trailBrush, transform.position);
void Update()    => trail.To(transform.position);
void OnDisable() => trail.End();

// attack that traces a line then explodes at the end
field.PaintLine(beamBrush, origin, target);       // swept, smooth
field.Stamp(blastBrush, target);                  // radial burst (Repel op, large size)
```

---

## Smoothing & rasterization (SweptPathSource)

### Path

- `VectorFieldStroke` keeps a small ring buffer of recent world points (4 is enough for Catmull-Rom).
- Fit a **centripetal Catmull-Rom** spline through them (centripetal α=0.5: passes through points, no
  overshoot/cusps on sharp turns).
- **TipMode.Smoothed**: render up to the second-newest point (the newest is look-ahead for the tangent) →
  ~1 frame lag, smoothest.
- **TipMode.Leading**: render to the newest point using a tangent **extrapolated from recent velocity**
  (Hermite). Zero lag. To hide the extrapolation artifact on a hard turn, **blend the head tangent toward the
  true Catmull-Rom tangent** over the next 1–2 points as they arrive.

### Coverage (no dabbing)

Rasterize the band of cells within `size/2` of the spline. Per cell, in a single pass:

```
d        = distance(cell, nearestPointOnSpline)
weight   = shape.RadialFalloff( clamp01(d / radius) )     // one value per cell
tangent  = spline tangent at that nearest point           // smoothly varying
cell.brushForce  = weight  (as magnitude)
cell.strokeForce = tangent * stepMagnitude
cell.finalForce  = tangent * magnitude * weight
cell.brushCenter = nearestPointOnSpline (grid space)
```

Because each cell gets **one** coverage value for the whole span, there are no beads and no spacing-dependent
intensity — unlike point dabs, which double-apply on overlap.

### Arc-length parameterization (frame-rate independence)

- Walk the newly-added arc in increments of ≈ half a cell, carrying a **fractional-arc cursor** across frames
  (the role today's `gridDistance` accumulator plays, but along the spline rather than a straight segment).
- The spline is defined by the points, so the shape and coverage are identical regardless of frame pacing.

---

## Overlap handling (per-op auto-select — no setting)

A swept thick line revisits cells just behind the moving head. How the op runs on a revisited cell is chosen
**automatically from the op**, never by the caller:

- **Set-style ops** (don't compound on re-apply): apply **live**, no snapshot. Cheap path.
- **Compounding ops** OR **neighbour-reading ops**: apply from a **pre-stroke snapshot** using a per-stroke
  **max-coverage** map, so the whole stroke equals one coverage-weighted application from the original field —
  exact, seam-free, frame-rate-proof.

This needs one new flag on the op, composing with the existing `NeedsSnapshot`:

```csharp
interface IVectorFieldBrushOp {
    ...
    bool NeedsSnapshot { get; }        // existing: reads neighbours
    bool CompoundsOnReapply { get; }   // new: re-applying at the same coverage changes the result
}
// Rule: use snapshot+coverage iff (NeedsSnapshot || CompoundsOnReapply).
```

| Op | CompoundsOnReapply | Overlap mode |
|---|---|---|
| Draw | false | live (cheap) |
| Clamp | false | live (cheap) |
| Normalize | false | live (cheap) |
| Additive | true | snapshot+coverage |
| Smudge | true | snapshot+coverage |
| Burn | true | snapshot+coverage |
| Dodge | true | snapshot+coverage |
| Erase | true | snapshot+coverage |
| Smooth (future) | (NeedsSnapshot) | snapshot+coverage |

Why no toggle: both modes share the dominant cost (visit band cells + op + write). The exact path adds only a
`float` compare per cell and two bbox-sized side buffers, and is only taken for compounding ops anyway. For
those ops the cheap path isn't "a bit of dabbing" — it's visibly wrong (over-bright, frame-rate-dependent
seams). So it's always-correct, with these cost controls:

- **Pool** the snapshot + coverage buffers (rent at `BeginStroke`, return at `End`) → no GC churn.
- Snapshot/coverage are scoped to the stroke's **bounding rect**, not the whole field.
- **Future memory lever (optional):** linear accumulating ops (Additive, Smudge) can be made exact with the
  coverage map alone by applying the **coverage delta** — no value snapshot. Only nonlinear ops (Burn/Dodge/
  Erase) truly need the snapshot.

---

## VectorFieldStroke (data)

```csharp
struct VectorFieldStroke {
    DrawableVectorFieldComponent field;   // ref
    VectorFieldBrush brush;               // value (copy of config)
    // path
    Vector2[] recentPoints; int count;    // small ring buffer (world space)
    float arcCursor;                      // fractional arc carried across frames
    Vector2 headTangentBlend;             // Leading mode: current head tangent, blended toward true
    // overlap (only allocated when brush.op needs it; pooled)
    RectInt region; Vector2[] snapshot; float[] coverage;
    // reusable cell buffer (pooled / shared)
    List<VectorFieldBrushCell> cellScratch;

    void To(Vector2 worldPos);   // push point, advance arc cursor, rasterize new span, apply, MarkRegionDirty
    void End();                  // return pooled buffers
}
```

Zero per-frame allocation: ring buffer fixed-size; cell/snapshot/coverage buffers pooled; `To` reuses them.

---

## Editor integration

`VectorFieldDrawingTool` stops owning `GetBrushCells` / `GetStampCells` / `GetDrawingSteps` and the brush-map
build. It becomes: gather mouse input → drive `BeginStroke`/`To`/`End` (or `Stamp`) → wrap the stroke in
`Undo.RegisterCompleteObjectUndo` at `BeginStroke`. Same Tier-2 API the game uses. The overlay's op selector,
brush size/pressure, cookie, and the `M` cycle shortcut all stay; they just feed a `VectorFieldBrush`.

---

## Migration / build order

1. **Rename** Tier-1 static `VectorFieldBrush` → `VectorFieldBrushKernel`; free up `VectorFieldBrush` for the
   config struct. Update the editor tool's one call site.
2. **Kernel generalization**: move `strokeForce` + `brushCenter` into `VectorFieldBrushCell`; kernel signature
   becomes `Apply(field, cells, pressure, op, out dirty)`. Update `PointStampSource` to fill them per cell.
3. Add `IVectorFieldBrushOp.CompoundsOnReapply` + set it on the 8 existing ops per the table.
4. Add `VectorFieldBrushShape` (`Radial` CPU + `FromCookie` GPU-cached), `VectorFieldBrush` config, `TipMode`.
5. Add `IStrokeCellSource` + `PointStampSource` (lift from the tool) + `SweptPathSource` (new: spline +
   arc-length + coverage rasterization).
6. Add `VectorFieldStroke` (struct) with pooled buffers; add component facades `Stamp` / `PaintLine` /
   `BeginStroke`.
7. Repoint `VectorFieldDrawingTool` onto the facades; delete its now-duplicated cell/step code.
8. Bring the **Repel** op (+ Swirl/Attract) forward from Phase 3 so the "explode at the end" case has its op.

Each step compiles on its own; 1–3 are pure refactors, 4–6 are additive, 7 deletes the duplication, 8 is new ops.

## Future / companion

- **Trail fade**: a full-field decay/relax tick per frame (separate from painting) so trails dissipate.
- **Burst**: define the explosion shape with the Repel op stamped; consider a per-cell magnitude that falls off
  from `brushCenter` so it reads as an outward blast.
- **Burst/Jobs**: ops are pure; for very large fields, swap the `IVectorFieldBrushOp` interface for struct ops +
  generics (or function pointers) to run the band in a Burst job. Math is unchanged.
