# Windfall — Game Design Doc

> **Status:** draft for iteration. Nothing here is implemented yet.
> Working title: *Windfall*. (Alt names: Drift, Updraft, Gale Golf, Slipstream.)

A local-multiplayer "wind golf" built to show off the Vector Field System. The
player rides an authored wind field toward a target ring — like golf, Monkey
Target, or pétanque, the goal is to *come to rest inside the ring*.

---

## 1. Pitch

You're a seed / balloon / parachutist drifting over a windy landscape. You can't
walk. The **only** way you move is by opening your parachute to **catch the
wind** — and the wind is a vector field that swirls and gusts differently across
the course. Time your catches and releases to thread the flow and settle gently
into the scoring ring.

The vector field *is* the level. Reading the flow, predicting where it will carry
you, and choosing the instant to let go is the whole game.

---

## 2. Core fantasy: launch, then ride the wind

A shot has two phases:

The **entire game is one button** — launch and flight both. No aim stick, no
second input.

**A) Launch (golf-style, one button).** From rest, a two-stage oscillating meter:
first a **direction** indicator sweeps back and forth around the player — tap to
lock the angle; then a **power** bar pumps up and down — tap to lock the
magnitude and fire. This gives you an opening burst of momentum to commit to a
line, using only the catch button.

**B) Ride the wind (same button).** In flight, hold the button to **catch** the
wind; release to **coast**.

- **Catch (button held):** the local field vector grabs your velocity and
  steers it — hard and fast — toward the wind's flow. This is a *parachute
  snapping open*, not a slow build. You feel the wind take you almost instantly.
- **Coast (button released):** the parachute closes. You keep your velocity and
  ballistic-drift, slowing gradually to a stop under light drag. The field no
  longer touches you.

That asymmetry is the skill: your **launch** sets the opening line, then you
**catch** to gain/redirect momentum from the flow and **coast** to carry it
somewhere the wind *isn't* helping — bleeding speed so you settle on target.

**Flight is entirely field-driven** — no steering, no free thrust, ever (not just
MVP; this is the design). Once airborne you are a passenger of the field and a
pilot of *timing*. That's what makes it a true one-button game and keeps all the
attention on the field; the launch is the only moment you set a direction, and
even that is one button.

---

## 3. Movement model (the important part)

Custom 2D kinematic integrator on the player (not Rigidbody2D — we want exact
control over the "impulse" feel and the coast/friction). Game runs in the **XY
plane**; the wind field lies flat in XY (plane normal = Z, the field
component's default orientation), so `EvaluateWorldVector` returns an XY vector.

**All the constants below live in a `WindfallSettings` ScriptableObject (§3a), so
they're tunable live in play mode** — the player reads the SO every frame rather
than caching values, which is the whole point of the asset.

**Launch (phase A).** From rest, the player picks a `launchDir` and a normalized
`power` (0–1). On fire: `v = launchDir * lerp(minLaunchSpeed, maxLaunchSpeed, power)`.
Then flight (below) takes over.

Per `FixedUpdate` (dt) in flight:

```
windVel = field.EvaluateWorldVector(pos) * windScale     // desired velocity from the flow

if (catching):
    // Responsive, impulse-like steer toward the wind. High response = snappy.
    // Exponential approach is framerate-independent and never overshoots:
    t = 1 - exp(-response * dt)          // response ~ 12–25 → feels like an impulse
    v = lerp(v, windVel, t)
else:
    // Coast: keep momentum, bleed speed so the player can settle & stop.
    v *= exp(-coastDrag * dt)            // coastDrag small; tune for "puttable" stops

pos += v * dt
```

Notes / knobs to tune for feel:
- **`response`** — how violently the wind grabs you. The brief says *impulse, not
  gradual accel*, so this is high. Optionally add a one-frame **kick** on the
  *press* edge (`v += (windVel - v) * kick`) for extra punch, then hold the steer.
- **`windScale`** — converts field magnitude → world speed. The field stores
  unit-ish directions; this sets how fast the wind can carry you.
- **`coastDrag`** — controls how far/long you drift after release. Kept **low**
  on purpose so the coast lingers and the settle stays tense (roulette-wheel
  feel). Central to whether stopping on target feels fair.
- **Speed cap** — optional clamp on `|v|` so a strong gust can't fling you off-map.
- **`minLaunchSpeed` / `maxLaunchSpeed`** — the power meter's range.

Because catching *lerps toward* the wind velocity (not add-force), holding the
button doesn't accelerate you forever — you settle at the local wind speed, just
like a real parachute reaches terminal drift. That's bounded and readable.

---

## 3a. Player settings (ScriptableObject)

A `WindfallSettings` `ScriptableObject` holds every feel constant so we can tune
it **live in play mode** (edit the asset in the Inspector; changes apply next
frame — no recompile, no exiting play). Grouped, roughly:

```
[CreateAssetMenu] WindfallSettings : ScriptableObject
  // Launch
  float minLaunchSpeed, maxLaunchSpeed;
  // Flight / catch
  float windScale;          // field magnitude → world speed
  float response;           // catch steer sharpness (impulse feel)
  float pressKick;          // extra one-frame punch on the press edge (0 = off)
  float coastDrag;          // speed bleed while coasting
  float maxSpeed;           // optional cap
  // Settling / scoring
  float stopThreshold;      // speed below which we start the settle timer
  float settleTime;         // how long below threshold before the shot scores
  // Collision (§3b)
  float radius;             // player collision circle
  float restitution;        // bounciness of player-player hits (0 dead … 1 elastic)
  float mass;               // for momentum transfer (usually equal across players)
```

The player MonoBehaviour holds a `WindfallSettings settings` reference and reads
`settings.x` directly each frame (don't copy into locals at start), so dragging a
slider mid-flight changes the feel immediately. Ship a couple of preset assets
(e.g. *Slippery*, *Tame*) to A/B feel quickly.

**Coast should linger.** `coastDrag` is tuned low so that after release you drift
and slow *gradually* — the settle is a drawn-out, tense "will it stop in the
ring?" moment, like watching a roulette wheel lose speed. Don't snap to a stop;
let it hang. (This trades against fairness — see §5 settle detection.)

---

## 3b. Player-vs-player collision (the knock)

Players are physical bodies that **collide and knock each other**. This is a core
interaction, not a nicety — it's the pétanque move: bump a rival off their line,
or blast a settled opponent *out of the scoring ring*.

- **Model:** circle-vs-circle elastic-ish collision resolved in the same custom
  integrator. On overlap, separate the pair and exchange momentum along the
  contact normal, scaled by `restitution` (equal `mass` → clean symmetric knock).
  A hard field-driven flier ramming a slow drifter transfers real speed.
- **The wind keeps acting through it.** Collision only edits velocity; whoever is
  *catching* keeps being steered by the field the next frame, so you can pin or
  shepherd someone against a gust. Knocked players are subject to the same
  coast/settle rules, so a good hit can un-settle a scored opponent.
- **Settled pieces stay on the board** (pétanque): once a player settles in the
  ring they remain a physical obstacle, so a later shot can clatter them out.
  This is what makes collision matter in *turn-based* play, not just simultaneous.
- **Knock feeds §7a juice:** a `OnCollide(impactSpeed)` event → impact spark,
  screen-shake, *clack* SFX scaled by impact.
- **Solo levels:** with one player, collision is just dormant — the code path is
  the same, so nothing special-cases player count.

**Open:** do players also collide with **islands/walls** (bounce), or only each
other? Leaning: islands are landing zones (no bounce), optional stretch walls
bounce. Tracked in §10.

---

## 4. The vector field's role

The course is an authored wind field. **A level takes *any* `VectorFieldComponent`
— the game doesn't care which subtype.** Tom authors the fields; the game just
references the base type and reads it. So a level can use a hand-painted
`Drawable`, a `Group` blend, a `Noise` field, a live `Simulated` fluid, or
anything else — no game-side changes needed. The only contract is: assign a
`VectorFieldComponent` to the level and lay it in the XY plane.

(Reference for whoever authors them: `Drawable` = full manual control; `Group` =
composable building blocks; `Noise` = instant windy course; `Simulated` = wind
that evolves mid-round. All interchangeable to the game.)

The field is read once per frame per player via a **CPU consumer** (register in
`OnEnable`, `EnsureUpToDate()` before sampling) — cheap, and scales to N players.

---

## 5. Scoring & win condition

Concentric **target ring(s)** define scoring zones (bullseye style):

| Zone | Example points |
|---|---|
| Bullseye (inner) | 100 |
| Middle ring | 50 |
| Outer ring | 25 |
| Off-target | 0 |

A shot **scores when the player comes to rest** — speed below `stopThreshold` for
`settleTime` — and points are awarded for whichever zone the resting position
falls in. (Like pétanque/Monkey Target: where you *stop*, not where you pass
through.) Because the coast lingers (§3), settle detection must be patient: only
declare "stopped" once genuinely slow for the full settle window, so a slow
roulette-wheel creep doesn't false-trigger mid-drift.

**Round structure: single flight.** One launch, ride the wind, settle → the
level is scored/ended. No stroke count.

**Islands (in scope, the twist).** A level may place **island** pads. If you
come to rest *on an island*, you get to **take another shot** from there (a fresh
launch), chaining flights across the course toward the final target. Islands turn
a level into a route-planning puzzle: which islands to hop to reach a reachable
line into the ring. (An island is just a resting zone that grants a re-launch
instead of ending the round; the target ring ends it.)

**Failure:** going **out of bounds is a level fail** (see §6) — no reset-to-retry
mid-shot; the run ends.

---

## 6. Course / level design

- A level = a wind field + a start pad + a target ring + optional islands (§5) +
  a **bounds region**. Leaving the bounds = **level fail** (hard boundary, not a
  bounce or reset).
- Good courses read the field visually and reward understanding it: an obvious
  gust that overshoots unless you release early; a swirl you can loop for a tight
  approach; a calm pocket right over the bullseye where coasting-to-stop works;
  an island stranded in fast wind that's tricky to stop on.
- **Obstacles / walls** (stretch): bounce or block; `PolygonVectorField` can even
  make the wind hug them.

---

## 7. Feel, camera, controls

- **Camera:** follows the player, eased; zoom out slightly with speed so fast
  drifts stay readable. Frame both players in multiplayer (shared or split).
- **Controls (single button):**
  - Keyboard: `Space` (P1), `Enter`/RShift (P2) — or per-player keys.
  - Gamepad: one face button per pad. Local MP wants gamepads.
See §7a for the VFX/SFX that sell these moments.

---

## 7a. VFX & SFX (game feel)

Every state change needs an audible + visible response. Driven by events the
player controller fires (`OnLaunch`, `OnCatchStart`, `OnCatchEnd`, `OnSettle`,
`OnScore`) so feedback is decoupled from the physics.

| Moment | VFX | SFX |
|---|---|---|
| **Launch** | charge-up build on the power meter; burst + directional streak on fire | tension wind-up → *thwip* release, pitched by power |
| **Catch (press)** | parachute/canopy *pops open*; a puff at the player; drift trail begins; brief field-lines highlight around you | *fwump* canopy snap + a rising gust bed that stays while held |
| **Release** | canopy closes; trail thins; small puff | gust bed fades out |
| **In flight** | trail whose length/color tracks speed; light lean into velocity | wind whoosh, volume/pitch scaled by `|v|` |
| **Settle / stop** | dust/leaf settle puff; player "plants" | soft *clunk* / thud |
| **Score** | ring pulses in its zone color; points popup; confetti/sparkle scaled by zone | ascending chime, better zone = better sting |
| **Collide (knock)** | impact spark at contact; brief screen-shake scaled by impact; both flash | *clack* / thud pitched by impact speed |
| **Out of bounds** | quick fade/poof | low "miss" tone |

Implementation: a small `WindfallJuice` component subscribes to those events and
triggers ParticleSystems + one-shot AudioSource clips; a `Trail`
(TrailRenderer/particle) reads `Velocity`. Keep clips/prefabs as serialized refs
so they're swappable. The **wind whoosh** and **catch gust** are continuous
sources modulated each frame (volume/pitch from speed & catch state).

---

## 8. Visualizing the field — clear *and* not ugly

**This is a real open problem, not a solved one.** Seeing the flow you're about
to ride *is* the core-loop feedback, so the visualization has to be both
**legible** (you can read where the wind will carry you) and **attractive** (it's
the whole screen). The existing renderers each miss one side of that balance, so
step-0 of the build is a **visualization bake-off** before committing:

What we have and the tradeoff:
- **VectorFieldDebugRenderer** (arrows) — maximally *legible*, but reads as debug
  UI, not art. Candidate for an optional "wind vision" assist toggle, not the
  base look.
- **LIC** shader — the dense "combed along the flow" look; crisp, never washes
  out, genuinely pretty, but static per-frame (no sense of *motion/direction*
  on its own) and can look busy.
- **IBFV / Water Flow** shaders — actually *animate* the flow (streaks drift
  along it), so motion is legible; risk is muddiness / tiling / low contrast.
- **Particles** (`ParticleSystemVectorField`) — drifting motes/leaves carried by
  the field; instantly communicates motion + direction and looks alive; weak at
  showing *magnitude* precisely.

**Proposed direction to prototype:** a **layered** look — a subtle animated flow
surface (IBFV or LIC, low-contrast, gradient-recolored to fit art) as the base,
**plus a particle layer** for motion and life, **plus** the arrow overlay
available as a toggle. Tune each so the composite is calm but readable. Whether
that composite is "not too ugly" is exactly what the bake-off decides — we may
end up authoring a **custom flow shader** (the system exposes the live field
texture for your own shader; see `VECTOR_FIELDS.md`) if none of the stock
renderers land it.

> Action item: build the bake-off scene first (§11 step 0) and screenshot each
> option over the same course so we can judge side by side.

---

## 9. Local multiplayer (future)

**Player-vs-player collision (§3b) is the heart of MP** — knocking rivals off
their line and clattering settled pieces out of the ring. Two modes exploit it
differently:

- **Turn-based (pétanque — recommended first):** players alternate shots on one
  shared course; each settled player stays on the board as a physical piece, so
  your shot can **knock opponents out of the ring** or nudge yours closer.
  Scoring resolves once everyone has shot. Simplest camera (one shared view,
  frame the active flier), and collision still matters via the settled pieces.
- **Simultaneous (Monkey Target party feel):** everyone launches/drifts at once;
  mid-air knocks are constant chaos. Needs a shared/split camera and turn-free
  scoring. More fun, more work — do it after turn-based.

Architect the player as a self-contained controller (own field consumer, own
input source) so 1→N players is just spawning more of them, and collision is
resolved by a small manager iterating the live set. Input via an action map with
per-player bindings/devices.

MVP is single-player (collision dormant); keep the seams clean for MP.

---

## 9a. Items / power-ups (future — party layer)

A pickup/item layer could give this a Monkey Target / Mario Golf party feel.
Because everything routes through the shared wind field, items get to be
*field-native* and show the system off further. Sketches:

- **Field-editing items** (the strong idea — they literally paint the wind):
  - **Gust bomb** — stamp a temporary outward burst / vortex into the field near
    you (drop a transient `Stamp`/burst brush that decays), redirecting anyone in it.
  - **Dead-air / lull** — stamp a low-magnitude patch that kills the wind in a zone.
  - **Slipstream** — paint a short directional lane you (or others) get carried by.
  - These reuse the runtime painting API (`Stamp` / `PaintLine` / a decaying
    drawable layer) directly — no new tech.
- **Self item examples:** extra catch/boost, a brief brake, a re-launch mulligan.
- **Offensive/defensive (MP):** nudge a rival off-line, shield from field-edits.

Scope note: MVP ships **without** items. Design the field as a live, paintable
layer from day one (it already is) so adding them later is additive, not a
rewrite. Tracked as open question #8.

---

## 10. Open questions (let's resolve before building)

1. ~~Movement authority~~ — **resolved: flight is 100% field-driven** (no
   steering). It's a true one-button game. See §2/§3.
2. ~~Round structure~~ — **resolved: single flight, with islands** that grant a
   re-launch. See §5.
3. ~~How a round starts~~ — **resolved: one-button golf launch** — oscillating
   direction sweep (tap to lock), then oscillating power bar (tap to fire). §2.
4. **Coast friction:** decided *low* for roulette-wheel tension (§3); exact value
   is a tuning pass in the settings SO once we can feel it.
5. ~~Bounds behavior~~ — **resolved: out of bounds = level fail.** §6.
6. ~~Field type~~ — **resolved: level takes any `VectorFieldComponent`**; Tom
   authors them. §4.
7. **Theme / character identity:** still open — see §13. Collision now nudges
   toward solid, knockable objects (marbles/conkers/boats); my pick shifted from
   dandelion seed → **marbles/boules**.
8. **Items (see §9a):** still undecided; MVP ships without them.
9. **Collision scope (§3b):** players collide with each other (yes). Do they also
   bounce off islands/walls, or only each other? Leaning islands = no bounce,
   walls (stretch) = bounce.
10. **Collision feel:** how bouncy (`restitution`) and can a knock un-settle a
    scored piece? Leaning yes (that's the pétanque drama) — a tuning pass.

---

## 11. Proposed build order (once the doc settles)

0. **Visualization bake-off** (§8) — one course, screenshot each renderer +
   the layered composite, pick the base look (or decide we need a custom shader).
   Cheap and de-risks the biggest unknown.
1. **Movement prototype** — one player, one Noise field, `WindfallSettings` SO,
   launch + catch/coast integrator. Get the *feel* right first (this is the whole
   game), tuning the SO live in play mode. Grey-box, no scoring.
2. **Scoring** — target ring + patient settle detection + out-of-bounds fail.
3. **Islands** — resting-on-island → re-launch chaining.
4. **Juice** — VFX/SFX events (§7a) + camera.
5. **Real course** — a Tom-authored field + the chosen visualization.
6. **Local multiplayer** — turn-based first, with **player-player collision (§3b)**
   and settled pieces as knockable obstacles (the pétanque payoff).
7. **(Stretch)** items (§9a).

---

## 12. Technical sketch (for step 1)

Scripts (namespace `Windfall`), living in this folder:

- `WindfallSettings` (ScriptableObject, §3a) — all feel constants; tuned live.
- `WindGlider` (MonoBehaviour) — the player. Holds `VectorFieldComponent field`
  and a `WindfallSettings settings`, runs launch + the kinematic integrator (§3),
  exposes `Velocity`, `IsCatching`, `IsResting`, and fires the events in §7a.
  Registers/unregisters as a CPU consumer.
- `WindfallInput` — abstracts per-player input: the launch aim+power and the
  single-button "catch" (keyboard/pad), so MP is just more input sources.
- `WindfallJuice` (§7a) — subscribes to the glider's events; drives VFX/SFX.
- `PlayerCollisionManager` (§3b) — iterates the live player set, resolves
  circle-circle knocks, fires `OnCollide(impactSpeed)`. Dormant with 1 player.
- `TargetRing` — concentric zones; `int ScoreAt(Vector2 worldPos)` + gizmos.
- `IslandPad` — a resting zone that grants a re-launch instead of ending the run.
- `LevelBounds` — the play region; exiting = fail.
- `WindfallGameLoop` (later) — launch → flight → settle → (island re-launch | score
  | fail) state machine; turns for MP.

Reads the field via the CPU path from `VECTOR_FIELDS.md` §"Reading a field from
code". Nothing here needs new engine features — it's a pure consumer of the field
(with items later reusing the runtime *painting* API to edit the field live).

---

## 13. Theme suggestions (pick one — art/SFX/vehicle follow from it)

The mechanic (catch a flow, coast, **knock rivals**, settle) fits any "carried
by a current" fantasy — but **collision changes the weighting**: the vehicle
should read as a *solid thing that clacks into another solid thing*. Soft/wispy
subjects (a lone seed, a wisp of spore) make a hard knock feel wrong. So the
strong themes are ones where bumping is *obviously* part of the fantasy.

**Best fits (collision is native to the fantasy):**
1. **Marbles / boules / curling stones** — the purest pétanque: heavy things that
   *clack* and shove each other out of the ring. The "wind" reframes as a current
   / slope / magnetic field / ice draft carrying the stone. Collision, scoring,
   and knock-out are all built into the real game. *(My pick — the mechanic and
   the collision are the same fantasy.)*
2. **Conkers / acorns / chestnuts on the breeze** — hard seeds (not wispy) that
   knock into each other; keeps the literal *wind* field, adds satisfying *tok*
   collisions. Autumnal, cozy, cheap to look good.
3. **Paper boats / bumper boats** — top-down water; field = currents; boats bump
   and shove. The Water Flow shader is tailor-made for the surface.
4. **Bumper balloons / hot-air balloons** — field = thermals/gusts (most literal
   to the original "parachute catching the wind"); balloons *boing* off each
   other. Collision reads as bouncy rather than heavy.

**Weaker now (collision feels off):**
5. **Dandelion seed / spore** — gorgeous with the flow-vis, but two seeds
   *clacking* to knock each other out of a ring is a hard sell. Demote unless we
   drop or heavily soften collision.

All are mechanically identical — purely art/audio/naming. Given collision, I now
lean **#1 (marbles/boules)** — it makes catch-coast-knock-settle one coherent
fantasy — with **#2 (conkers)** if you want to keep the wind literal.
