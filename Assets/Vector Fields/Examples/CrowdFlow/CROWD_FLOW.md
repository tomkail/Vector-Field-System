# Crowd Flow — flow-field pathfinding demo

A theme-park–style crowd that navigates a Unity **Terrain** toward multiple destinations
("attractions"), routing around water, hills and obstacles. Each destination has its own **flow
field**; when the landscape changes the fields re-solve and the crowd re-routes live. Built on the
Vector Field System: the navigation flow is written into a `DrawableVectorFieldComponent` per
destination, so the existing flow visualisers can render the currents on the ground, and agents
steer by sampling the solved field.

## Run it

1. Menu **Vector Fields ▸ Examples ▸ Create Crowd Flow Demo** — builds a terrain (two hills + a
   lake), three attractions, the manager/director/editor, a camera and a light into the active scene.
2. Press **Play**. Visitors spawn on walkable ground and stream between attractions.
3. Edit the landscape live and watch them re-route, using the on-screen panel or the keyboard:
   - **Click a brush** (Raise / Lower / Water / Obstacle) or press **1–4**. The active brush is
     highlighted and tints the panel accent + the brush ring on the ground.
   - Drag the **Brush size** slider or **scroll** to resize; the ring previews the affected area.
   - Hold **left-drag** to paint (raise / lower / raise water / place obstacle); **right-drag** does the
     inverse (lower / lower water / remove obstacle).
   - **↺ Reset world** (button or **R**) restores the terrain, water and clears placed obstacles.
   - The panel shows live **visitor count**, **water level** and **re-solve time**.

   The original heightmap is snapshotted on Play and restored on exit, so sculpting never permanently
   edits the shared `CrowdTerrain.asset`.

## How it works

```
Terrain ──heightmapChanged(RectInt)──► CrowdFlowManager ──► NavCostField (shared)
                                             │                    │  per-cell passability + directional climb cost
                                             │                    ▼
                                             ├──► FlowFieldSolver × N   (Dijkstra from each attraction)
                                             │         │
                                             │         ├──► DrawableVectorFieldComponent (flow-vis on the ground)
                                             │         └──► CrowdAgent.FlowDir(...)  (steering, read straight off CPU)
                                             └──► WorldEditor (play-mode sculpt/water/obstacles)
```

- **Engine-agnostic core** (`Core/`): `NavCostField` + `FlowFieldSolver` behind `INavCostSource`. A
  multi-source Dijkstra builds the cost-to-goal *integration field*; steepest descent turns that into
  the *flow field*. The step cost is **directional** — walking uphill costs more than across or down —
  so crowds skirt hills instead of climbing them. Unit-tested in `Tests/` with no scene or play mode.
- **`TerrainNavSource`**: samples the terrain into the cost grid — height (for the climb penalty),
  slope (gentle cost + cliffs blocked), below-waterline blocked, and colliders on the *Blocked* layer.
- **`CrowdFlowManager`**: owns the shared cost field, one solver per attraction, and the authoritative
  `GridTransform` (nav plane) that keeps terrain sampling, agent steering and the visualisation aligned.
  Landscape edits arrive via Terrain's own `heightmapChanged` callback; a burst of brush stamps
  coalesces to one re-solve per frame (`LastSolveMs` shows the cost).
- **`CrowdDirector` / `CrowdAgent` / `CrowdSpatialHash`**: spawn the crowd, keep a per-frame neighbour
  hash, and steer each agent along its destination's flow plus **boids-style flocking**. Agents read the
  solver's CPU flow directly (no GPU readback) via `CrowdFlowManager.FlowDir`.
- **Flocking** (`CrowdDirector.Flock`, one neighbour query per agent): **separation** (dominant — push
  out of a `separationRadius` personal space so visitors avoid each other and spread out), **alignment**
  (gently match neighbours' heading within `neighborRadius`, forming natural streams/lanes), and
  **cohesion** (off by default — the flow field already groups the crowd toward attractions, so pulling
  them together would fight the spread; raise `cohesionWeight` for tighter packs). Tunable on the director.

## Designed for scale (not yet built)

The solver takes cell **rects** and the manager already re-samples only the edited cost region, so the
deferred optimisations are additive — see the `// CHUNKING:` markers:
- **Dirty-chunk skipping**: bound the flow re-solve to the edit's cost horizon instead of re-solving
  the whole grid (currently a full re-solve per change — sub-ms at 128², fine for moderate maps).
- **Sectored/hierarchical**: portals + a coarse inter-chunk graph for very large maps.
- **Field tiling**: split each field into per-chunk Drawables under a Group when one texture is too big.

## Look

- The terrain uses **`Shaders/MarioGrass.shader`** (`CrowdFlow/MarioGrass`) — a stylised multi-texture shader
  tuned to read like *Super Mario 3D World*. All tiling in world space:
  - **Grass** on non-steep ground — two grass tiles (`_GrassTex` / `_GrassTex2`) swapped **by world height**
    at `_GrassHeight` (softened by `_GrassHeightBlend`): low grass below, high grass above. Planar on world XZ.
  - **Wall / cliff** (`_WallTex`, striped strata) on **steep faces — slope greater than `_WallSlopeAngle`**
    (degrees, softened by `_SlopeBlend`); projected so the stripes stay horizontal (V = world Y).
  - **Sand** (`_SandTex`) in a band just above the waterline (`_WaterLevel` / `_SandBand`), on non-steep ground.

  Lighting is smooth high-key lambert (raised `_ShadeFloor` + `_AmbientBoost`, no toon banding) with a
  **fresnel rim** (`_FresnelColor` / `_FresnelPower` / `_FresnelStrength`). Tunable via **`GrassTerrain.mat`**.
  It's a plain URP object shader, so the terrain is assigned with **`drawInstanced = false`** (both set by
  the setup builder).
- **Texture note:** the demo expects `grass.psd`, `grass 2.psd`, `sand.psd` and `wall.psd` tiles in this
  folder. Swap them for original or licensed textures before publishing a public build — don't ship
  textures ripped from a game.

## Flow-map visualisation (arrows over the terrain)

`FlowArrowVisualizer` (on the CrowdFlow root) shows each destination's flow field as arrows using the
Vector Field **`VectorFieldArrowRenderer`**, draped over the terrain heightmap so they follow the ground:

- It builds a height texture from the terrain (`GetHeights`), rebuilt whenever the terrain is sculpted
  (`heightmapChanged`), and pushes it to the arrow shader via global properties (`_VFHeightMap`,
  `_VFHeightDrape`, `_VFHeightRect`, `_VFHeightParams`). The arrow shader (`DebugArrow.shader`) overrides
  each arrow's world Y from that heightmap when `_VFHeightDrape` is on — a general, default-off feature of
  the arrow system (`heightOffset` lifts the arrows off the surface).
- One arrow renderer per destination flow field, tinted its attraction colour (Fixed colour mode) so the
  maps stay distinct; the flat texture-quad viz is hidden. Density = `arrowCount` (Fixed resolution).
- Pick the visible map from the HUD ("Flow map" row: Off / 1 / 2 / 3) or cycle with **V**. Only one shows
  at a time (`SetVisible` / `Cycle`).

## Notes

- The *Blocked* layer (`CrowdObstacle`) is created by the setup and must stay in the manager's
  **Blocked Mask** for placed obstacles to block.
