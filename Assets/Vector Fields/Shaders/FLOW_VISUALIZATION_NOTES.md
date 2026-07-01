# Flow Visualization — design notes & the cell-seam investigation

Internals/design note for `VectorFieldFlowVisualization.shader` + `VectorFieldFlow.cginc`. Not user-facing
(`VECTOR_FIELDS.md` is the usage reference). This captures *why* the shader is shaped the way it is, and the long
investigation into the per-cell **seam** so we don't re-run the dead ends.

## What the effect does

Renders a vector field as animated "streaks" that flow along the field. Driven by the field render texture
(`_MainTex`, RG = vector·0.5+0.5) and a streak texture (`_Tex`, e.g. a seamless sand/noise image).

Per fragment, in `sampleStreakAtPoint`:
1. Sample the flow direction `flowDir` from `_MainTex` (bicubic — see below).
2. Build a streak-texture coordinate `fluidTexUV = (dot(flowDir, fragVec), dot(flowSide, fragVec))`, where
   `fragVec = fragGridCellFrac - midGrid` is the fragment position **measured from the centre of the whole field**
   (in grid-cell units, so up to ±`_GridCellCount/2`).
3. Scroll it over time: `fluidTexUV.x += flowMag * _Speed * _AnimationTime`.
4. Sample `_Tex` there → the streak pattern. Alpha comes separately from flow magnitude through `_AmplitudeRamp`.

The **legacy** look (`legacyStreakBlend`) samples the flow at the **four cell corners** (`floor(fragGridCellFrac)`),
computes a streak for each, and bilinearly blends them by sub-cell position. Four differently-oriented streak fields
interfering is what gives the characteristic woven, cell-locked texture.

## The bug: a per-cell "seam"

A faint grid of lines on the cell boundaries (`_GridCellCount` grid). Observed properties (each one ruled something
out):

- **Scales with `_GridCellCount`** → it's the tile grid, not the field-texel grid and not the 150-tile vs field-res
  confusion.
- **~1px wide regardless of zoom** → screen-space / perceptual, not a feature in the field data (which would
  magnify).
- **The seam animates** → the time-dependent scroll term feeds it; with `_Speed = 0` a *static* seam remains, so
  there are two contributions (static spatial seam + animation amplifying it).
- **Looks like "sampling from the wrong position."** (User's words — and literally correct, see root cause.)

## Root cause

Two things combine, and they are the *same mechanism* that produces the desired look:

1. **Per-cell direction quantisation.** The four corner directions are held constant across each cell and the corner
   *set* changes at every boundary. This is what makes the interference cell-stable (the look), and what steps at the
   boundary (the seam).
2. **A huge lever arm.** `fragVec` is measured from the field centre, so the streak-texture coordinate is
   `dot(flowDir, ~75-cells)`. A *tiny* per-cell change in `flowDir` is multiplied by that lever arm into a **large
   jump in where `_Tex` is sampled** — so the two sides of a cell boundary read the streak texture from far-apart
   places and don't line up. The scroll then slides those mismatched positions over time → the seam shimmers.

The lever arm is what makes streaks long and globally coherent; the per-cell held directions are what make the woven
look. **Together they mathematically guarantee the seam.** It is not a bug to be found — it's the algorithm's core
trick showing its seams. (The projection `dot(flowDir, globalPos)` only equals true streamline arc length for
*uniform* flow; for varying flow it's an approximation, and the seam is where the approximation is discontinuous.)

### Why "continuous" looks completely different (broad streaks vs fine filaments)

Comparing Legacy and Continuous Single side by side made this precise. Legacy holds `flowDir` **constant per cell**, so
each corner contributes `dot(constDir, P)` — a clean linear ramp → **broad, filament-free** streaks; it blends four of
them. Continuous uses a **per-pixel-varying** `flowDir(P)`, so `dot(flowDir(P), P)` carries a `P · d(flowDir)/dP` term;
with the huge `P` lever arm, gentle direction variation is amplified into **fine high-frequency filaments**.

Consequence: **broadness requires piecewise-constant directions; continuous directions inherently produce filaments.**
So you cannot get the broad legacy look from a single continuous direction — which is exactly why Mode 2 is unusable as
the effect, and why C must blend *several constant-direction fields* (each broad) with *continuous weights* (no seam),
rather than vary one direction continuously.

## What was tried and ruled out (don't repeat)

| Attempt | Result | Why it couldn't work |
|---|---|---|
| `tex2Dlod` / force base mip on `_Tex` | no change | `_Tex` and `_MainTex` have **no mipmaps** (`enableMipMap: 0`); there's no mip to pop. |
| Bicubic (C2) sampling of the flow field | no change to the seam | Fixed a *different*, real artifact (C1 creases on the **field-texel** grid). Kept — it's a genuine quality win — but unrelated to the cell seam. |
| `smoothstep` → `smootherstep` blend weights | no change | The seam isn't a blend-smoothness/derivative-order issue. (And smootherstep flattens *harder* at edges → worse quilting.) |
| Raise `_TextureScale` (lower texture freq) | no change | The seam is independent of texture content → not aliasing of `_Tex`. |
| Enable mipmaps on the sand texture | no change | Same — not a texture-mip issue. |
| Supersampling 2×2 / 4×4 of the whole fragment | no change **and crashed Unity** | The brightness across the seam is *continuous*; AA integrates a continuous function and leaves it continuous, so a perceptual/curvature line passes through untouched. The 4×4 `[unroll]` also inlined the heavy fragment 16× → the Metal cross-compiler choked and took the editor down. **Lesson: never leave a big `[unroll]` of a heavy function in; all branches compile regardless of the runtime toggle.** |
| Linear vs smooth interpolation of corners | changes the *flavour* only | Smooth weights → "pillow/quilt" (zero-slope ridges at every grid line). Linear → thinner kink-lines. Neither removes it. Legacy now uses **linear** (least objectionable). |
| Feather: crossfade legacy→continuous in a thin edge band | looked **strange** (comb/fringe strip) | Legacy and continuous don't resemble each other even at the boundary, so the band paints in a foreign-looking effect. Removed. |
| Seam-blur: near edges, blend toward a small cross-blur of the **same** legacy streak | helped *a bit* | Too timid (±1px). Strengthened into the **seam-mask bridge** that is now Mode 1 (reaches across the seam into clean interiors). Masks, doesn't fix. |
| Continuous single sample (one bicubic direction) | **removes the seam — but wrong look** | No floor/cell/corner → continuous, but a per-pixel-varying direction makes **fine filaments** (the `P·dD/dP` term), not broad streaks. Was Mode 2; **removed** in cleanup (documented here). |
| **C — Steerable basis:** blend N constant-direction streak fields by flow alignment | **crosshatch, misses the look** | Blending a 0° shear and a 45° shear is **not** a 20° shear — it's a crosshatch interference. Legacy works only because its corner directions are all ~the local flow angle. Was Mode 3; **removed**. |
| **D — Texture advection:** integrate the sand lookup backward along the flow | **looks like noise** | Advection *displaces* where the sand is read; it never *orients* the sand's ripples. It drags/scrambles the ripples instead of aligning them. Wrong operation. Was Mode 4; **removed**. |

Also fixed along the way (real, kept): the **amplitude/alpha** term was a four-corner bilinear blend that showed its
own per-cell line in the smooth magnitude field; since colour is flat white by default, that bled straight into the
visible alpha. Amplitude is now sampled **continuously in every mode** (`sampleAmplitudeAtPoint(uv)`) — it only gates
alpha and carries none of the streak look, so this is free. This cleared the most visible part of the seam.

## What the effect actually is (the key realization)

The sand texture **already has ripples**. The legacy effect samples that sand in a **frame rotated to the local flow
direction** — it *orients the sand's existing ripples* to point along the flow. That orientation **is** the effect.
Everything else (corner blend, weights) just decides *which* direction to orient by.

This is why C and D missed: **D advects** (displaces where the sand is read) → drags/scrambles the ripples instead of
orienting them → noise. **C blends fixed-angle orientations** → a 0°+45° blend is a crosshatch, not a 20° orientation.
Only Legacy and Continuous actually *rotate* the frame.

## The fundamental limit (why the seam can't be removed without changing the effect)

Orienting an anisotropic texture, at a fixed ripple density, along a flow with **curl** forces a choice:

- **Orientation held rigid over regions** (Legacy) → broad ripples, but orientation must *jump* somewhere → **seam**.
- **Orientation varies continuously** (Continuous) → no seam, but `d(orientation)/dP × (sand-coordinate)` injects extra
  frequency → **fine filaments**.

Checked: shrinking the coordinate's lever arm doesn't escape it — the extra frequency is tied to `ripple-density ×
curl`, invariant under rescaling. So for rotational flow the curl *must* be spent as either a seam or filaments. It's a
constraint, not a bug. The seam is worst where curl is highest (note it crowding the vortex centre). A truly seamless
*flowing* look is possible only as a **different aesthetic** (LIC / IBFV — blurry directional smear, not sand ripples);
see the IBFV prototype shader.

## The seam-copy experiment (mode 2) — how far it got and where it stalled

A second attempt to *hide* the seam (not fix the effect): for each seam-band pixel, **copy** a nearby clean pixel
instead of blending. The idea is sound and got most of the way, through several real bugs found and fixed:

1. **Copied from a distant fixed column, not each pixel's own neighbour** → the band looked like a flat foreign strip.
   Fix: parallel shift from each pixel's own position (`g.x + sign(sd)*reach`), so the band is a shifted copy of the
   adjacent content, not one column smeared across.
2. **`sign(0) == 0`** → a pixel landing exactly on the boundary got a zero offset and sampled the seam itself (the one
   line that stayed dark while neighbours copied). Fix: never-zero sign `(sd >= 0 ? 1 : -1)`.
3. **Screen-vs-uv space** (user's hypothesis): tried shifting via `ddx/ddy` (screen space) instead of uv/cell space, in
   case perspective/rotation made the uv shift point the wrong way. Made no difference on this view → **not** a
   screen-mapping issue. Reverted to the simpler cell-space shift.

Where it stalled: even working correctly, mode 2 leaves a residual at **some** seams and not others. Best current
understanding — it's the **streak direction relative to the seam**: where streaks *cross* the seam the outward copy
slides along a streak (seamless); where streaks run *along* the seam the perpendicular copy jumps to a different streak
(mismatch). That's the same orientation/curl limit as above, showing up per-seam. Copy confines the artifact to the
worst seams instead of a continuous line, but doesn't beat the blur decisively. **Not resolved — shelved for later.**
Unexplored next ideas: shift *along the flow* rather than perpendicular to the seam; or a hybrid (copy across the band +
1px blend only on the centre changeover line).

## Current shader state

`_FlowSamplingMode`:
- **0 — Cell Blend (Legacy):** original effect, full seam.
- **1 — Cell Blend Seam Masked:** legacy everywhere; in a `_SeamBand`-wide strip around each edge it bridges *across*
  the seam (averaging the legacy streak sampled `_SeamReach` px into each neighbouring interior). *Default.* The most
  usable result — masks the seam well, residual soft ghost at high-curl seams.
- **2 — Cell Blend Seam Copy:** experiment above; copies each seam pixel from a parallel-shifted neighbour. Knobs:
  `_SeamBand` (which pixels are seam pixels), `_SeamReach` (shift distance). Kept for the next revisit.

Debug/instrumentation still in the shader:
- **`_SeamDebug`** toggle — paints seam pixels **green** (mode-2 copy target is clean) / **red** (target still on a
  seam) / black (interior). For validating the copy targeting.
- **`_ContinuousAmplitude`** toggle — on = continuous amplitude (default); off = legacy 4-corner amplitude blend.
  Finding: toggling it made **no visible difference** on the test field, because `_GridCellCount` (150) is finer than
  the field texels (32–128) so the 4-corner blend ≈ the point sample. Continuous kept because it's cheaper and can't
  look worse; the earlier claim that the amplitude blend was a *primary* seam source was overstated (confounded by a
  stale-compile — see below).

Other features from this work (independent of the seam, all kept): **bicubic** field sampling (fixes a separate
field-texel crease); a **recolor gradient** with selectable source (`_GradientSource`: magnitude vs streak luminance);
**90° texture rotation** (`_TextureRotation`); a **Use Texture Color** toggle.

## Gotcha: Unity doesn't recompile on `.cginc`-only edits

Editing `VectorFieldFlow.cginc` alone often does **not** trigger a shader recompile — Unity only watches the `.shader`.
Several "that change did nothing" results during this work were **stale compiles**, not failed changes. The `.shader`
carries a `rev N` comment in the CGPROGRAM block; **bump it whenever you touch the `.cginc`** (or reimport the shader)
to force a recompile. Also: `[Enum(...)]` labels must not be purely numeric (`0`,`90`,…) — Unity's Enum drawer throws.

## Decision

Ship **Mode 1 (Seam Masked)** for the sand look — the seam is intrinsic (orienting an anisotropic texture over
rotational flow), so we mask it rather than chase a fix. **Mode 2 (Seam Copy)** is a parked experiment worth another
look with the "shift along flow" / hybrid ideas. A genuinely seamless *flowing* look lives in the separate **IBFV
prototype** (`VectorFieldFlowIBFV.shader`) as a distinct aesthetic, not a drop-in replacement for the sand ripples.
