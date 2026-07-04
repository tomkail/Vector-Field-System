# UnityX Code Review

Read-only review of every `.cs` file under `Assets/UnityX/Scripts/` (514 files, ~67.5k lines), fanned out across the whole tree. **Nothing was modified.** Findings are grouped per area, each into 5 categories: Bugs, Unity-native / .NET duplication, Refactoring / dead code, Misleading / incorrect comments, Tidying.

Paths are relative to `Assets/UnityX/`. Line numbers are approximate — treat as anchors, confirm before acting.

---

## Components / UI (`Scripts/Components/UI/`)

---

## Components (non-UI) (`Scripts/Components/`)


---

## Editor Tools (`Scripts/Editor Tools/`)

---

## Property Drawers (`Scripts/Property Drawers/`)

### Bugs
PD-1. `EnumButtonGroup/Editor/EnumFlagsButtonGroupDrawer.cs:38-45` — individual-flag writes (`|= mask` / `&= ~mask`) don't mask to defined bits → `Everything`/`-1` round-trips inconsistently.
PD-6. `EnumButtonGroup/Editor/EnumButtonGroupDrawer.cs:75` — the static `Draw` uses `Array.IndexOf(trueNames, names[i])` unguarded to index `typedValues[sortedIndex]` → throws on a stale/removed enum name.
PD-7. `EnumFlag/Editor/EnumFlagDrawer.cs:20` — writes to `property.intValue` via `(int)Convert.ChangeType(...)` → truncates for `long`/`ulong`-backed enums.

### Unity-native duplication
PD-11. `EnumFlag/Editor/EnumFlagDrawer.cs:19` — the drawer just wraps `EditorGUI.EnumFlagsField` (Unity's native C# `[Flags]` field — distinct from `MaskField`, the plain-int/layer masker). Since Unity 2017.3 the default inspector auto-renders `[Flags]` enums this way, so the `[EnumFlag]` attribute is largely obsolete.
PD-13. `Popup` & `PropertyPopup` drawers. *Explained/assessed — keep.* `[Popup]` shows a dropdown from a FIXED option list baked into the attribute; `[PropertyPopup]` shows a dropdown sourced from a sibling serialized array (dynamic, optional "NONE"). Both wrap `EditorGUI.Popup` with the same `getValue`/`setValue`/`validateValue` index boilerplate on string/int/float fields. The "overlap `EnumPopup`" note is misleading: `EnumPopup` only works on enum-typed fields, whereas these target non-enum fields with arbitrary value lists — so they're not redundant with it. The real overlap is between the two drawers themselves, but per the self-contained-drawer rule we don't factor a shared base/helper across drawers. → `## 🅿️ Left as is`.

### Refactoring / dead code
PD-20. `EnumButtonGroupDrawer.cs` & `EnumFlagsButtonGroupDrawer.cs` — substantial copy-paste (label rect, per-button widths, toolbar).
PD-21. `EnumButtons/Editor/EnumButtonsDrawer.cs` vs `EnumButtonGroupDrawer.cs` — overlapping intent (EnumButtonsDrawer is a simpler `GUI.Toolbar` single-select; its `attribute` override is self-referential/broken).

---

## Extensions / UnityEngineX (`Scripts/Extensions/UnityEngineX/`)

### Unity-native duplication
UEX-29. `LayerMaskX.cs:31-33,65-68` — `Includes` == `(mask & (1<<layer))!=0`; `Inverse` == `~mask`.
UEX-33. `AnimationCurveX.cs:372-393` — `EaseInOut` is redundant with `AnimationCurve.EaseInOut` (both zero-tangent S-curves).

---

## Extensions / Geometry (`Scripts/Extensions/Geometry/`)

### Unity-native duplication
GEO-18. `Point/PointRect.cs` — duplicates `RectInt`.

### Refactoring / dead code
GEO-25. `Polygon/Polygon.cs:262-300` — `GetRegularEdgePosition(normalized)` and `GetPositionAtNormalizedArcLength(normalized)` are functionally identical (both = position at normalized arc length; `GetRegularEdgePosition` just inlines the edge-walk the other delegates to `GetPositionAtArcLength`). `GetPositionAtArcLength(absolute)` is the distinct base. The `GetRegularEdgePosition` name + its commented `edgeIndex`/`edgeArcLength` scraps hint it was *meant* to be by-edge-index (evenly per edge) but was implemented as arc-length — so it's a misnamed/unfinished duplicate. Dedup (delegate) or implement the intended by-edge behaviour.

### Tidying
GEO-43. `Point/PointRect.cs` — inconsistent namespacing: `Line`/`Polygon`/`Point`/`PointRect` are global while `Triangle`/`Sphere`/`RegularPolygon`/`StarPolygon` are in `UnityX.Geometry`.

---

## Extensions / Grid + UnityEditorX (`Scripts/Extensions/Grid/`, `Scripts/Extensions/UnityEditorX/`)

---

## Extensions / Algorithms + Camera + Spline

### Refactoring / dead code
ACS-20. `Camera/Shots/CameraShotGeneratorTools.cs:144-181` & `CameraProperties.cs:422-432` — commented-out blocks. *(Assessed: the "custom screen rect" feature is NOT implemented — `SerializableCamera.useCustomScreen`/`customScreenParams` are declared but read by nothing; the commented consumer depends on host-app `Main.Instance` absent here. `CameraProperties.HasNaN` block is superseded by the live `IsValid()`. Safe to delete the dead code; to make custom-rect real would need wiring `customScreenParams` into the camera projection. Awaiting direction.)*

---

## Extensions / Text + Scene Management + Collections + Serializable Components + Audio

---

## Extensions / System (`Scripts/Extensions/System/`)

### Bugs
SYS-2. `FlagsX.cs:98` — `CreateEverything<T>()` does `(T)(object)~0` → InvalidCastException for non-int-backed enums; `:123-125` `Invert<T>` depends on it; `:75-82` `Create<T>` casts via `(int)(object)flags[i]` → same crash.
SYS-3. `FlagsX.cs:154-158` — `GetFlags` zero-named-member branch is unreachable → `GetFlags(0)` never yields the zero member.
SYS-32. *(New)* `EnumX.cs:47-52` — `Random<T>()` does `Enum.ToObject(typeof(T), Random.Range(0, Length<T>()))`, treating the random *index* as the enum's *underlying value*. For any enum not numbered contiguously `0..N-1` (flags, explicit values) it returns wrong/undefined members and can never reach higher ones. Should index into `GetValues<T>()`.

### Unity-native / .NET duplication
SYS-10. `EnumX.cs:14-147` — `Length<T>`/`IsValid`/`ToArray`/`GetEnumerable` duplicate `Enum.GetValues`/`Enum.IsDefined`.
SYS-11. `FlagsX.cs:33-56` — `SetFlag`/`UnsetFlag`/`HasFlag` reimplement `Enum.HasFlag` + bitwise ops.

### Refactoring / dead code
SYS-16. `EnumX.cs:23-76` — `#if !UNITY_WINRT … if(!typeof(T).IsEnum) throw` copy-pasted 5× and dead (the `where T:Enum` constraint already guarantees it).
SYS-17. `EnumX.cs:87-101` — `ToArray<T>` adds nothing over `(T[])GetValues`; `GetEnumerable` boxes.
SYS-18. `FlagsX.cs:14-125` — two parallel families (raw-int vs generic enum) with overlapping duties + inconsistent naming; `:101-107` — `(int)Math.Pow(2,x)` vs `1<<x`.

### Tidying
SYS-28. `FlagsX.cs:167` — leftover `//yield return value;`.
SYS-29. Mixed tabs/spaces + stray blank lines: `FlagsX.cs:108-110`. *(StringX portion resolved — see Done.)*

---

## Extensions / Structures (`Scripts/Extensions/Structures/`)

### Bugs
STR-4. `Island/OwnedIslandDetector.cs:10` + `IslandDetector.cs:9` — `new static islands` hides the base list → base and derived helpers write to different lists → silently dropped results. *(Update: dropped-results hazard resolved — the owned detector no longer calls base helpers; the `new static` shadowing itself remains.)*

---

## Extensions / Spring (`Scripts/Extensions/Spring/`)

### Bugs
SPR-2. `Spring.cs` — `CalculateTimeOfMaximumDisplacement` can return a spurious near-zero "peak" only in a narrow numerical edge (post-step `Velocity` rounding to exactly 0 so `Sign==0`) in a loop-based heuristic (the method isn't closed-form). Normal released-from-rest case is correct. Low priority; left unfixed — not confident of a safe fix without in-editor testing.

---

## Extensions / Easer (`Scripts/Extensions/Easer/`)

### Bugs
*(EAS-3 resolved: `SpringDamper` now delegates to the analytic `Spring` solver (EAS-2/7 fix), which is the exact closed-form response — no explicit-Euler overshoot. See Done.)*

---

## Extensions / Tween (`Scripts/Extensions/Tween/`)

*(TWN-7 iOS event-crash workaround — investigated, likely stale but verify on an iOS IL2CPP build before removing — see `## 🅿️ Left as is`.)*

---

## Extensions / Range + ValuePicker

*(RNG-12 Blender/Selector overlap — assessed: could share a prioritised-source base but the differences (blend-fold vs single-select, opposite comparer direction) make it moderate-value/real-risk; LogicBlender stays separate — see `## 🅿️ Left as is`.)*

---

## Extensions / FlexLayout + NoiseSampler + GLDebug + Property Curve + FSM + Version Control + Timer + MeshBuilder + Texture Transform Utils + misc

### Refactoring / dead code
MISC-7. `NoiseSampler/Editor/NoiseSamplerPropertiesPropertyDrawer.cs:18-124` — `OnGUI`/`Draw` almost identical; three near-identical `DrawNoiseGraph` overloads.
MISC-8. `Texture Transform Utils/TextureTransformUtil.cs` — two parallel pipelines (blit vs `Graphics.DrawTexture`) with heavy copy-paste; `CopyWithSizeAndImageOrientation2` unhelpfully named.
MISC-9. `GLDebug/GLDebug.cs:205-307` — `DrawSquare`/`DrawCube` overload triplets are repetitive boilerplate.

### Tidying
MISC-14. Commented-out lines: `NoiseSamplerPropertyDrawer.cs:32-52`, `NoiseSamplerPropertiesPropertyDrawer.cs:151-336`, `GLDebug.cs:50,62`, `MeshBuilder/AddPlaneParams.cs:45-50`, `FlexLayout/FlexLayout.cs:45`, `Version Control/Editor/VersionBuildPreProcessor.cs:34-44`.
MISC-16. Inconsistent indentation in the nested `GraphGUI` class (`NoiseSamplerPropertiesPropertyDrawer.cs:163-336`).
MISC-17. `NoiseSamplerPropertiesPropertyDrawer.cs:8` — `graphXRange` is `static` but effectively const.

---

## Cross-cutting themes (worth a single sweep)

XC-4. **`enumValueIndex` / mask handling for enums** — `EnumFlagsButtonGroupDrawer` (unmasked flag writes), `EnumButtonGroupDrawer` (unguarded `IndexOf` in the static `Draw`), `EnumFlagDrawer` (`int` truncation for `long` enums).
XC-8. **Widespread commented-out dead code.** *(Progressively cleared across rounds — LineEditor.cs, TouchInputSimulator, Line.cs, BoundingSphere.cs, RangeInt.cs (revived), and the Text Effects scratch all handled. Remaining: the flagged `TextEffectsController` disabled-feature cluster (outline-2 / base-material restore / pre-render hook / `isDirty=false`) — kept for a human decision, see round-16 notes.)*

---

## 🅿️ Left as is (not-a-bug / intentional / won't-do / deferred)

Consolidated here so the sections above show only outstanding, actionable findings. Items below are conscious decisions not to change (or to postpone), with the reasoning.

### Verified — not a bug
- **CMP-5** — lower slab already rejected by the preceding `localBounds.Contains`; line 274 is merely redundant.
- **CMP-6** — `Vector2.normalized` returns zero for a zero vector, no NaN.
- **CMP-8** — release→mutate→create is Unity's required RenderTexture order.
- **CMP-11** — `RemoveOldDeltaTimes` deliberately keeps the frame that tips past `fpsGraphHistoryTime` (retained window is ≥ history); `RemoveRange` semantics correct.
- **ED-5** — `ScreenshotCapturer` renders a chosen camera *subset* to an arbitrary-resolution offscreen RT with format control + callback (a superset of `ScreenCapture.CaptureScreenshotAsTexture`).
- **ED-6** — `EditorTime` pushes a `_EditorTime` shader global that ticks in edit mode; Unity has no built-in editor-time shader global and `_Time` doesn't reliably advance in edit mode.
- **ED-7** — enum→`TextureFormat` mapping is a justified curated subset (only the dead `FormatToDepth` was removed).
- **UEX-26** — `ColorX` blend/utility helpers are convenience API (`Grayscale` wraps `color.grayscale`; `BlendAdditive`/`BlendMultiply` take a `lerp` param; `RandomRGB` ≠ `Random.ColorHSV`).
- **UEX-30** — `RigidbodyX` `Set*`/`Translate` route through `rigidbody.rotation`/`MovePosition`/`MoveRotation` (physics-aware, not thin transform wrappers).
- **SYS-26** — `throw new("…")` is valid C# 9 target-typed `new`; compiles fine (`UNITY_WINRT` is undefined).
- **TWN-2** — `Timer.GetNormalizedTime()` clamps to [0,1], so the timer can't return >1; no overshoot.
- **MISC-3** — `TextureTransformUtil.FlipVertical`'s Graphics-path identity matrix: confirmed by the author it worked as built (`Graphics.DrawTexture` is inherently Y-flipped and the `Normal` case explicitly un-flips). Left as-is.
- **MISC-6** — `PropertyCurve` reimplements `AnimationCurve` on purpose (it's generic over `T`; no built-in to defer to). `FlexLayout` margin accounting is self-consistent (no double-count); `StateMachine.GetStatesInheriting<R>` is loosely typed but not a concrete bug.
- **UEX-34** — false positive: `Vector4X.ToQuaternion` and `QuaternionX.ToVector4` are *inverse* conversions (opposite directions), each defined once in its natural home — not duplicates. Nothing to remove.
- **UEX-62** — `TrailRendererX`'s null-trail and double-clear `Debug.LogError`/`LogWarning` are legitimate misuse diagnostics (each followed immediately by `yield break`), not per-frame noise — kept. (Could downgrade the null-trail one to a warning if quieter behaviour is ever wanted.)

### Intentional / documented (won't change)
- **UI-31** — private `ScrollRect` methods reimplemented out of necessity (originals are private); the local copy is public API.
- **UI-32** — local `CreateEncapsulating` kept so the UI sub-project stays independent of `Extensions/BoundsX` (deliberate portability copy; the `else if` min/max chain is correct).
- **UI-35** — `ExtendedSelectable`/`ExtendedButton` derive from distinct base types.
- **UI-36** — the two line-renderer miter/bevel blocks aren't identical; merging mesh code blind isn't worth it.
- **UI-37** — `Outline8` is a serialized public MonoBehaviour that may be used in external scenes.
- **CMP-26** — `GetColor` is `protected` subclass API, unused by the live batch `RecalculateColors` path.
- **CMP-30** — `EnforceProperties` per-repaint enforcement is intentional (parent tag/layer edits propagate); writes are `!=`-guarded, no dirtying storm.
- **CMP-31** — RegionEditor's flat gizmo `CreatePolygonMesh` is distinct from Region's extruded runtime one.
- **PD-10** — `-standardVerticalSpacing` when hidden is the standard row-collapse idiom (~0 net height).
- **PD-12** — FilePath/FolderPath share only trivial row layout; full merge not worth it.
- **PD-14** — `[Label]` relabels any field; `[InspectorName]` is enum-values-only (not a substitute); `[Label]` is unused in-project.
- **PD-15** — a thin `PasswordField` wrapper is the point.
- **PD-16** — `PreviewTextureDrawer` draws a live sized sub-rect-aware preview (more than `AssetPreview.GetAssetPreview`).
- **PD-18** — no built-in per-axis toggle-mask field; the only real dup (V2/V3) was done as PD-19.
- **PD-22** — `HideInEditMode`/`HideInPlayMode` diverge in draw call + hidden height (not a clean bool-collapse).
- **PD-23** — SetProperty's cache is done (see PD-3); OnChange's by-name `MonoBehaviour.Invoke` is an intentional deferred mechanism; per the self-contained-drawer rule we won't share a reflection helper across the two.
- **PD-25** — `MinMax` vs `SteppedRange` use different snap math (not copy-paste).
- **UEX-7** — `WorldToViewportVector`'s `f(0)−f(vec)` sign form matches the sibling `…ToWorldVector` methods (shared internal sign convention); callers may rely on it — verify intent before ever changing.
- **SPR-4** — the `Force`/`SpringForce`/`DampingForce`/`Acceleration` instance overloads are an intentional overload ladder over the static core; can't be collapsed with optional params (full form has `time` last, convenience form first — reordering breaks the API).
- **EAS-9** — `GetDelta` (`newValue - lastValue`) has no generic form (Unity's runtime lacks generic `-`); each concrete type must override it. EAS-5/6 minimised it to the unavoidable 1-liner.
- **ACS-27** — `CameraPropertiesModifier`'s empty Axis+Multiply branch: `axis` is a `Quaternion`, and "multiply" has no defined scalar semantics for a rotation (the other Multiply branches multiply scalar euler components; rotation composition is already what Additive does). Left as a no-op with an explanatory `// TODO` comment rather than inventing behaviour.
- **UEX-32** — `RectTransformX.GetSize/GetWidth/GetHeight` are trivial `rect.*` getters, but kept for symmetry with the non-trivial `SetWidth`/`SetHeight` (Unity still has no `RectTransform.rect` setter). Not worth changing.
- **UEX-36** — `RandomX.eulerAngle` (`Random.value*360`) is a trivial convenience; `onUnitCircle` is NOT a duplicate (edge of the circle, vs `Random.insideUnitCircle`'s disk — no 2D built-in for the edge). Kept.
- **PD-13** — `[Popup]` (fixed option list) and `[PropertyPopup]` (options from a sibling serialized array) both wrap `EditorGUI.Popup` on string/int/float fields. Not redundant with `EnumPopup` (that's enum-only). The intra-pair overlap isn't factored out because each drawer stays self-contained (portability rule).
- **PD-24** — `LockDrawer`'s `BeginDisabledGroup`/`EndDisabledGroup` wrap overlaps `Disable`/`DisableIf`, but sharing a helper across those three drawers would violate the self-contained-drawer rule; the overlap is a trivial 3-line idiom. Kept separate.
- **UEX-39** — `CanvasGroupsAllowInteraction`/`CanvasGroupsAlpha` are byte-identical in `CanvasGroupX` and `CanvasX` (no external callers). Deliberately kept as separate copies so each extension file stays self-contained/portable — dedup declined by decision.
- **UEX-46** — the shared `static Vector3[] corners` scratch buffer in `RectTransformX`/`CanvasX` has a theoretical re-entrancy aliasing risk, but the fix (per-call local arrays or thread-local state) is declined in favour of keeping each file simple/self-contained/portable. These are main-thread editor/UI helpers not called re-entrantly in practice, so the risk is accepted.
- **GEO-19** — closest-point-on-segment is already consolidated to a single canonical `GetNormalizedDistanceOnLineInternal` within each of `Line`/`Line3D` (the other methods are thin wrappers). The remaining duplication is just the 2D vs 3D split, inherent to `Vector2`/`Vector3`; forcing a shared implementation would change the hot-path numerics/allocations for more risk than the near-zero gain. Left as is.

### Duplication kept (removing breaks callers / invasive / perf / not in netstandard)
- **GEO-24** — `Polygon.ContainsPoint(Vector2[])` and `(List<Vector2>)` are near-identical, but kept as separate concrete overloads for PERFORMANCE: unifying to `IList<Vector2>` would route every per-vertex index access in this hot point-in-polygon loop through a virtual interface indexer (no inlining / no bounds-check elision). The concrete `Vector2[]`/`List<Vector2>` overloads avoid that. Valid perf motivation — kept.
- **GRID-30** — `SquareGridAgent`/`RadialGridAgent` duplicated enter/exit diffing; both extend MonoBehaviour with no common base, so sharing needs an invasive base-class/serialization change for little gain.
- **TXT-16** — `ListX` `ToList`/`First`/`Last`/`Contains`/`IndexOf` duplicate LINQ/`List<T>`, but are widely-used public helpers; removing breaks call sites across the library.
- **TXT-17** — `IEnumerableX` `ToHashSet`/`DistinctBy`/`Chunk`/`Filter`/`Map` duplicate BCL, but are public helpers and `DistinctBy`/`Chunk` aren't in Unity's netstandard2.1.
- **TXT-18** — `WordWobble`'s manual `IndexOf(' ')` word-split reimplements `string.Split`, but is entangled with the char→vertex-index mapping guarded in TXT-10.
- **TXT-20** — `RuntimeSceneSet`'s hand-rolled build-settings collection + manual array-grow: delicate EditorBuildSettings code; correctness handled by TXT-11.
- **TXT-24** — identical `Wobble` + scaffold copy-pasted across VertexWobble/CharacterWobble/WordWobble; sharing needs a new base/helper type — invasive for little gain.

- **XC-6** — the "buggy custom HSV/HSB + easing/curve helpers" theme is resolved via its sub-findings: `Collider.ClosestPoint` → native (UEX-8, done); `AnimationCurve.EaseInOut` redundancy (UEX-33, explained — kept, harmless); the HSV/HSB structs assessed and kept (Unity has RGBToHSV/HSVToRGB conversions but no HSV/HSB *struct*, so ours add value). Nothing further actionable.
- **RNG-12** — `Blender`/`Selector` share a prioritised-source shape and *could* sit on a common base, but they differ in load-bearing ways (blend-fold vs single-select `Value`, opposite `EntryComparer` direction, `Func<T,T>` vs `T` payload, `object` vs generic priority-source), so a merge is feasible-but-moderate-value with real risk of flipping the fold-order/winner semantics — only worth it if that code churns. `LogicBlender` stays standalone (no priorities, Unity-serialized, aggregate-delegate model). Kept separate.

### Deferred (needs editor / larger effort / vendored)
- **TWN-7** — the iOS/IL2CPP "generic-inheritance event crash" workaround (`new event` shadowing + raising-method overrides, ×6 tween subclasses) is almost certainly stale (a ~2013 first-gen AOT bug; modern IL2CPP ships generic-base events fine), but it only manifests in an AOT device build, so removal can't be verified from source/Editor. Kept — verify on an iOS IL2CPP build before removing.
- **CMP-38** — "Decendent" → "Descendent" rename touches a public MonoBehaviour class + files/folder + serialized scene/prefab references; safest via Unity's Project-window rename (preserves GUID). Not done blind without the editor.
- **GRID-24** — `HeightMapMeshGenerator` ~600-line externals/internals × triangles/quad copy-paste; large mechanical refactor best done with in-editor compile + visual verification.
- **Vendored** — `SimplexNoise.cs`, `Noise.cs`, `AStar.cs` appear third-party/vendored — findings are real but likely intentionally kept close to upstream. *(ACS-12 EasingFunction if-chains were converted to `switch` — resolved, see Done.)*
- **XC-8 (residual)** — the `TextEffectsController` disabled-feature cluster (second-outline shader support, base-material save/restore, extra dirty callbacks + `OnPreRenderText` hook, the `isDirty=false` reset that would stop `Refresh()` running every frame) is coherent unfinished work, not scratch — kept for a decision (finish or delete), not blind-deleted.

---

## ✅ Done (branch `unityx-updates`)

Completed findings, moved out of the sections above. IDs are the original finding IDs (stable). Notes call out anything noteworthy discovered during implementation. *(All applied work on this branch was independently re-verified 2026-07-03: the UI-1…28 fixes and the island-detector rewrite are correct with no regressions; the comment passes were comment-only.)*

### Bugs
- **UI-1** `Line/AdvancedUILine.cs` — `RayAdvancedUILineIntersection` now returns `crossings > 0`. *Note:* the review suggested `% 2 == 1`, but this is a ray *intersection* (returns the closest hit point), not a point-in-polygon parity test — an external ray through a convex shape crosses twice (even) yet clearly hit, so "any crossing" is the correct semantic.
- **UI-2** `Line/AdvancedUILine.cs` — `FindClosestPointOnAdvancedUILine` now stores `currentPoint` (the computed point on the edge) instead of `point` (the query), which had made it return the input unchanged.
- **UI-3** `Line/AdvancedUILine.cs` — both static `Scale` overloads now assign the `Vector2.Scale` result back into the vertex (was a discarded no-op); the length-mismatch guard now `return`s the unscaled copy after logging instead of falling through to an index-out-of-range throw.
- **UI-4** `Line/AdvancedUILine.cs` — `GetHashCode` is now a content-based hash consistent with `Equals`/`==` (which use `SequenceEqual`); was returning the array reference hash.
- **UI-5** `Line/AdvancedUILineRenderer.cs` — the inner/outer alpha fades now stay in the float `Color` domain (`color.a *= alpha`), and the outer-fade branch now targets `color2.a`/`color3.a` (the outer verts) instead of `color1.a`/`color4.a`. *Correction:* the review claimed `color.a` was "already 0–255" (Color32) and that the bug was `byte` overflow — but `AdvancedUILineRendererPoint.color` is a float `Color`, so `color1..4` are float. The original `(byte)(color.a * alpha * 255f)` wrote a 0–255 value into a 0–1 float alpha (→ clamped to opaque on the Color→Color32 conversion at `colors[i] = color`); dropping only the `*255f` (my first pass) instead wrote ~0 (→ transparent). Correct fix removes both the `(byte)` cast and the `*255f` and lets the `Color32[]` assignment do the scaling.
- **UI-6** `Polygon/UIPrimitiveBase.cs` — `MapCoordinate` now scales `local` into sprite-rect space (`local * spriteRect / rect`, mirroring Unity's `Image`) so the caller's divide-by-sprite-size normalises to 0..1; was multiplying by rect size → double-scaled alpha-test coordinate.
- **UI-7** `AbsoluteRectTransformController.cs` — `ScreenPointToLocalPointInRectangle` now passes the camera for both `ScreenSpaceCamera` and `WorldSpace` (only `ScreenSpaceOverlay` uses null); WorldSpace canvases were getting a null camera.
- **UI-8** `Background Blur UI/BackgroundBlurUI.cs` — `sigma` now uses float division (`blurRadius/3f`); integer division gave sigma 0 for small radii → NaN Gaussian weights (and the radius≤2 self-disable).
- **UI-9** `CanvasGroupOpacityInteractionEnabler.cs` — **not a bug (false positive), reverted.** The opposite-`ignoreParentGroups` early-outs are intentional: when `ignoreParentGroups == false` the alpha depends on the whole ancestor chain, so `Update()` polls every frame; when `true` it depends only on this group, whose own changes fire `OnCanvasGroupChanged`, so it runs event-driven (no per-frame cost). Both modes do reflect a direct alpha change; the review's "never reflected" premise is incorrect. Left as originally written.
- **UI-10** `CarouselUIView.cs` — `GetActiveItem` now returns null on an empty carousel instead of `%`-dividing by zero.
- **UI-11** `UIMonoBehaviour.cs` — `rootCanvas` now null-checks `canvas` before dereferencing `rootCanvas`.
- **UI-12** `Draggable/Draggable.cs` — the class now implements `IPointerClickHandler`, so `OnPointerClick`/`OnClicked` actually fire.
- **UI-13** `Draggable/Draggable.cs` — `GetPosition` now null-checks the canvas (returns `transform.position` if none).
- **UI-14** `Draggable/Draggable.cs` — revert now restores `m_ContentStartPosition` instead of `m_PointerStartLocalCursor` (was snapping to the cursor).
- **UI-15** `Draggable/MultitouchDraggable.cs` — removed the two dead `pointerStartLocalCursor` locals; the misleading "input tracker was found" warning now only fires in real builds (`#else`), where a duplicate pointer is genuinely anomalous — the editor test path already adds the new input.
- **UI-16** `Draggable/MultitouchDraggable.cs` — `LateUpdate` now null-checks the parent canvas before dereferencing `rootCanvas.worldCamera`.
- **UI-17** `ExtendedScrollRect/ExtendedScrollRect.cs` — a root ScrollRect (no parent) no longer NREs: the always-route sites (`OnInitializePotentialDrag`/`OnScroll`) guard `transform.parent`, and the "route to parent" flags are only set when a parent exists, which makes the downstream `OnEndDrag`/`OnDrag` sites safe.
- **UI-18** `ExtendedScrollRect/ExtendedScrollRect.cs` — `normalizedContentOffset` returns 0 per-axis when `freeMovementSize` is 0 (content fits) instead of dividing to NaN/Inf.
- **UI-19** `Grid Layout/GridLayoutApplier.cs` — auto-fill guards the 0-children case (`numValidChildren - 1 == -1` gave a negative cell count). *Note:* the review also claimed the `XAxis` branch passes the Y cell count as `numCellsX` "wrongly" — it's actually correct: it's a transposed computation via `ArrayIndexToGridCoord`, symmetric with the `YAxis` branch. Left as-is.
- **UI-20** `Grid Layout/GridLayoutItem.cs` — `Refresh()` now returns early if the `gridLayout` field is null.
- **UI-21** `Grid Layout/GridLayout.cs` — `CalculateCellCount` returns 0 for a non-positive item size (e.g. both axes set to `AspectRatio` → `GetItemSize()` 0) instead of dividing by `(0 + spacing)`.
- **UI-22** `ExtendedCanvasScaler/ExtendedCanvasScaler.cs` — the `ScreenDPI` setter now writes `value` (was ignoring it and re-writing `Screen.dpi`).
- **UI-23** `UI Imposter/UIImposterRenderer.cs` — `Render` now null-checks the target's parent canvas (returns early) before dereferencing `rootCanvas`.
- **UI-24** `Saturation UI/UISaturationEffect.cs` — added `OnDisable` that restores the graphic material and destroys the runtime-created `Material` clone (Destroy/DestroyImmediate per play state), matching the `BackgroundBlurUI` pattern; was leaking the material.
- **UI-25** `WorldSpaceUIElement/WorldSpaceUIElement.cs` — the `worldCamera` getter now caches the `Camera.main` fallback in a `[NonSerialized]` runtime field and no longer logs on every access, ending the console flood + serialized-value churn.
- **UI-26** `WorldSpaceUIElement/WorldSpaceUIElement.cs` — `onScreen` now converts the parent-local `targetPosition` into `rootCanvasRT`-local space before the `rect.Contains` test (was comparing mismatched spaces).
- **UI-27** `Swipe View UI/SwipeView.cs` — `GetNormalizedProgress()` returns 0 for ≤1 pages instead of dividing by `pages.Count - 1` (NaN/Inf for 1 page, negative for 0).
- **UI-28** `Swipe View UI/Editor/SwipeViewEditor.cs` — removed the stray `EditorGUI.BeginChangeCheck()` that had no matching `EndChangeCheck()` (unbalanced change-check stack; its result was never read).
- **ED-4 / ED-8** `Icon/IconManager.cs` — replaced the `BindingFlags.NonPublic` reflection with the public Unity 6 APIs: `EditorGUIUtility.SetIconForObject`/`GetIconForObject` (public since 2021.2) and a direct `EditorGUIUtility.IconContent(string)` call. Dropped the now-unused `System`/`System.Reflection` usings and the unchecked `mi` deref. The enum-indexed `GUIContent[]` arrays are kept as-is — the enums are contiguous 0-based so the arrays are the correct structure (a dictionary would be strictly worse); ED-8's substantive concern (reflection-loaded arrays) is resolved.
- **UEX-35** `SystemInfoX.cs` — `IsMacOS`/`IsWinOS` now compare `SystemInfo.operatingSystemFamily` against `OperatingSystemFamily.MacOSX`/`Windows` (enum, no fragile string `.Contains`, no editor/player split); also dropped the unused `using System.Collections;` (part of UEX-65).

### Misleading / incorrect comments
- **UI-55** `AbsoluteRectTransformController.cs` — fixed the inverted warning ("is not null!" → "is null (expected a RectTransform parent)!").
- **UI-56** `CarouselUIView.cs` — `SignedDeltaRepeating` comment now describes the 4-arg signed-shortest-delta-within-range behaviour.
- **UI-57** `Draggable.cs` — the two axis fields now document translate vs rotate axes (both previously said "The drag velocity").
- **UI-58** `MultitouchDraggable.cs` — unified the contradictory multitouch-test notes under a shared `[multitouch-test]` marker.
- **UI-59** `MultitouchDraggable.cs` — `ScaleAround` param doc renamed `scaleFactor` → `newScale` to match the signature.
- **UI-60** `ExtendedScrollRect.cs` — dropped the false "uses this component's rect transform if none specified" claim; de-contradicted the `contentBounds` comment block.
- **UI-61** `AdvancedUILine.cs` — rewrote `GetEdgeCenter`/`GetEdgeCenters` docs (removed copied "diagonal" text + nonexistent `edgeDistance` param) and fixed the duplicated "0,120,240" comment on `GetVertexDegreesInternal`.
- **UI-62** `UILineRenderer.cs` — clarified the "copied from UGUI (type is internal)" note.
- **UI-63** `UIImposterRenderer.cs` — removed the misleading "This is optional" (both branches always destroy).
- **UI-64** `SLayout.cs` — "does two things" → "three things".
- **UI-65** `SAnimatedProperty.cs` — removed the duplicated "for a single property" phrase (and "an instances" → "an instance").
- **UI-66** `SLayout.cs` — `canvasWidth` doc no longer claims scaling-awareness (returns raw rect width). *Note:* the disabled (commented-out) `rootCanvas`/`canvas` caching is a behavioural choice, left as-is.
- **UI-67** `SLayout.cs` — corrected the `CanvasToSLayoutSpace` comment that claimed it was "commented out" (it's live); kept the known-limitation warning.
- **CMP-32** `Input/Gestures/Pinch.cs` — `deltaPinch` comment now describes the projected-onto-center-direction sum (was "sum of the delta of both fingers").
- **CMP-33** `Input/InputPoints/Finger.cs` — `fingerArrayIndex` comment corrected to "enumeration order" (was "index 0 is the active touch").
- **CMP-34** `PolygonRenderer/LineDraw.cs` — `closed` field comment fixed (was a copy of the `miterLimit` comment).
- **CMP-35** `CoroutineHelper.cs` — usage example corrected to `Delay(Method, 1.0f)` matching the `Delay(Action, float)` signature.
- **CMP-36** `Render Texture Creator/RenderTextureCreator.cs` — comment now points at the real fix (`Handles.GetMainGameViewSize()`) instead of calling the `UnityStats.screenRes` hack "necessary".
- **ED-12** `DetectLeaksWindow/Editor/DetectLeaksWindow.cs` — added a class summary clarifying it's an undestroyed-`Object` census, not true memory-leak detection.
- **PD-26** `FakeNullable/FakeNullableAttribute.cs` — clarified that the value field is never cleared; only the companion bool marks the "null" state.
- **UEX-50** `RectX.cs` — replaced the wrong "THIS IS THE SAME AS INTERSECT!" comment on `Encapsulating` (it's a union / bounding rect).
- **UEX-51** `RigidbodyX.cs` — the two `Rotate` overloads now document their real params (`eulerAngles`/`relativeTo`, and `axis`/`angle`/`relativeTo`) instead of copied `point`/`axis`/`angle`.
- **UEX-52** `ColliderX.cs` — `GetClosestPoint` doc now describes the pivot-raycast behaviour and points to `Collider.ClosestPoint`.
- **UEX-53** `AnimationCurveX.cs` — removed the false "tangents between 0 and π" class note; `GetFirstTime`/`GetLastTime` docs now say "time" (were "value").
- **UEX-54** `TransformX.cs` — `GetVertices` local variable names de-inverted (Top now = +y); value-preserving rename, return-array order unchanged.
- **UEX-55** `Checked.cs` — `DoubleChecked`/`DoubleCheckedOne` docs say "double values" (were "float").
- **UEX-56** `DebugX.cs` — fixed stale `<param>` tags on `LogString`/`LogError`/`DrawPoint`, and `DrawCube`'s summary now notes only the front face is drawn.
- **UEX-57** `ScreenX.cs` — class doc corrected (static `[InitializeOnLoad]` class, no GameObject required).
- **UEX-58** `TextureX.cs` — `CopyWithSizeScaled`/`ResizeScaled` `<param>` tags fixed (`src`, removed nonexistent `mode`).
- **UEX-59** `QuaternionX.cs` — added a doc note that `IsValid` only rejects the all-zero quaternion (NaN/non-normalized pass).
- **UEX-60** `RayX.cs` / `BoundsX.cs` / `PlaneX.cs` — added notes flagging the name/semantics mismatches (distance-to-centre; segment-not-ray; ignored `Raycast` bool).
- **GEO-30** `Point/Point.cs` — corner docs de-hexed: "cell's four corners", `0..3` index, removed the bogus `<param name="first">` on parameterless `Corners()`.
- **GEO-31** `Polygon/Polygon.cs` — `centroid` doc now reads "area centroid (centre of mass)" (was the wrong "perpendiculars intersect" definition).
- **GEO-32** `Line/Line.cs` — removed the stale commented formula using an invalid single-arg `Vector2.Distance`.
- **GEO-33** `Polygon/Polygon.cs` — `Add` doc fixed ("adds/unions to the first", was "from the first").
- **GEO-34** `Polygon/Polygon.cs` — `AngleValue` doc rewritten (removed the tautology comparing identical arg sets).
- **GEO-35** `Polygon/Polygon.cs` — removed the false "return 360" comment on the degenerate case (value `360f/9f` left unchanged — behavioural).
- **GEO-36** `Point/PointRect.cs` — `ClampPoint` param docs fixed (were `<param name="r">The red component.</param>`).
- **GRID-33** `TypeMap.cs` & `TypeMap3D.cs` — Resize doc no longer references nonexistent "entities" ("does not raise OnChangeGridPoint callbacks"). *(The unconditional `DebugX.LogList` on resize is a separate tidying item.)*
- **GRID-34** `Grid 2D/Map Types/Grid.cs` — class doc corrected to "X and Y axes" (was "X, Y and Z").
- **GRID-35** `TypeMap.cs` — removed the commented-out `return` in the `void SetValuesAtGridPosition`.
- **GRID-36** `HeightMapMeshGenerator.cs` — reworded the two informal notes into proper `//TODO` comments.
- **GRID-37** `Grid.cs` — no change needed: the `<param>` names at the cited anchors actually match their signatures (descriptions are terse but not wrong).
- **GRID-38** `ScenePathDrawer.cs` — error message now says "use a string field" (the drawer requires a string, not an Object).
- **GRID-39** `AssetDatabaseX.cs` — removed the orphan stray `// Texture2D.CreateExternalTexture()` comment.
- **ACS-21** `Camera/Shots/CameraShotTools.cs` — added a note that the `...InScreenSpace...` methods return WORLD-space (public method names kept — portable API).
- **ACS-22** `Camera/Shots/CameraShotGeneratorTools.cs` — `GetVerticesFromTransform` Top/Bottom locals de-inverted (value-preserving; return-array order unchanged).
- **ACS-23** `Camera/Camera Properties/CameraProperties.cs` — misleading `halfHeight`/`halfWidth` locals renamed `fullHeight`/`fullWidth` (they hold the full frustum size due to the `* 2`).
- **ACS-24** `Spline System/SplineBezierPoint.cs` — "This lerp" comments corrected to "This slerp" (both sites use `Vector3.Slerp`).
- **TXT-29** `Collections/ListX.cs` — `GetAllOfType` doc rewritten (was just "G"); notes the `removeType` param is actually the type to KEEP; fixed bogus `<typeparam Q>`.
- **TXT-30** `Text Effects/BaseTextMeshProEffect.cs` — `OnDisable` comment now says it clears unconditionally (no dirty check).
- **TXT-31** `Text Effects/TextEffectsController.cs` — comment now matches the `!=` guard (was "always true").
- **TXT-32** `Text Effects/TextFader/GradientArea.cs` — conical-gradient stub note clarified (falls back to linear; Conical is the default mode).
- **TXT-33** `Scene Set/RuntimeSceneSet.cs` — BroadcastMessage docs de-copied from `GameObject.BroadcastMessage` (correct `methodName`/`parameter` params); `IsCurrentlyUniquelyLoaded` now notes it's order-sensitive (SequenceEqual).
- **SYS-21** `StringX.cs` — `Before`/`BeforeLast`/`AfterFirst` summaries corrected (all previously said "Get string value after…").
- **SYS-22** `ByteFormatter.cs` — fixed the "1 Indexed. 3 is MB" comment (SI is 0-indexed; MB = 2). *(The commented-out `FromToSize` stub is a separate tidying item.)*
- **SYS-23** `BoolX.cs` — `ToBool` param doc fixed to `_int` (was `_bool`). *(`ToInt`'s docs were already correct, contrary to the finding.)*
- **SYS-24** `EnumX.cs` — `GetEnumerable` doc no longer says "array" (returns `IEnumerable<T>`).
- **SYS-25** `EnumX.cs` — the 5 "Argument {0} is not an Enum" exception messages no longer contain an unformatted `{0}` (now "Argument is not an Enum: <type>").
- **STR-13** `Island/OutlineDetector.cs` — reworded the "safe while loop" comment to warn the 1000-cap can return a partial outline.
- **STR-14** `Island/OutlineDetector.cs` — moved the algorithm-description comment to before the loop it describes.
- **STR-15** `Island/OutlineDetector.cs` — `GetOutlineCoords` doc now describes the signed ring distance (0 = edge, + = outside, − = inside).
- **SPR-7** `Spring.cs` — merged the two inconsistent `_response` comments into one (period-of-oscillation / stiffness-as-duration).
- **SPR-8** `Spring.cs` — "high epsilon" corrected to "very tight epsilon (1e-7)".
- **SPR-9** `Spring.cs` — `IsDone` comment fixed (returns whether settled; was "Get the settling duration").
- **SPR-10** `Spring.cs` — the overdamped-branch `omegaD` comments no longer call it "frequency of damped oscillation" (overdamped springs don't oscillate).
- **EAS-12** `FloatMoveTowardsEaser.cs` / `FloatSmoothStepDamper.cs` — `ToString` labels fixed to the real type names (were "[BaseEaser]").
- **EAS-13** `MoveTowards/MoveTowardsEaser.cs` — summary now says it wraps `MoveTowards` (was copied "SmoothDamp" text).
- **EAS-14** `SmoothDamp/SpringDamper.cs` — softened the "This is always smooth!" claim (fixed step trades exactness for stability).
- **TWN-11** `Types/Base/TypeTween.cs` — typo "fro" → "for" in the easing-curve param docs (2 sites).
- **TWN-12** `Types/Base/TypeTween.cs` — `<param>` tags renamed `myTargetTime` → `myTweenTime` to match the signatures (4 sites).
- **RNG-14** `Range/Range.cs` — `CreateEncapsulating` docs de-copied from a Rect class (floats, not "rect"/"vectors"); local `vector` renamed `value`.
- **RNG-15** `Range/Range.cs` — removed the "if one is null" comment inside the struct `operator ==` (a struct can't be null).
- **RNG-16** `Range/Range.cs` — reworded the "Remove!" musing on `SignedDistance` into a proper review note.
- **RNG-17** `ValuePicker/Selector.cs` — added a note explaining the `TPrioritySource` type param (and fixed a "the the" typo).
- **MISC-10** `Regex/RegexHelper.cs` — added a note that `emptyOrWhiteSpace`'s pattern matches uppercase+whitespace (public const value left unchanged — portable API).
- **MISC-11** `Property Curve/PropertyCurve.cs` — `RemoveKeysBetween` doc now says "strictly between (exclusive)" to match `IsBetween`.
- **MISC-12** `Serialized Scriptable Singleton/SerializedScriptableSingleton.cs` — comment corrected to EditorPrefs (PlayerPrefs at runtime).
- **MISC-13** `Version Control/VersionControlX.cs` — noted the 42-char upper bound is a loose guard (git SHA-1 is 40).

### Grid + UnityEditorX
- **GRID-1** `Grid3D.ClampGridPoint` — z now clamped from `z` (was `y`).
- **GRID-2** `Grid3D` — `ArrayIndexToGridPoint` decode is now the exact inverse of the x-major `GridPointToArrayIndex` (`x=idx/(h·d); y=(idx%(h·d))/d; z=idx%d`); static params + the instance / `TypeMap3D.Resize` callers updated to `(height, depth)`. Round-trip verified on non-cubic grids. *(Static param semantics changed — but the old decode was buggy and has no in-project callers.)*
- **GRID-3** `Point3.ToString` — prints `z` (was `y`).
- **GRID-4** `Point3` — `sqrMagnitude`=x²+y²+z², `magnitude`=`Sqrt(sqrMagnitude)` (now `float`), `normalized`=`((Vector3)this).normalized` (now `Vector3`), matching the 2D `Point`. *(Return types changed from the broken int stubs; no in-project usages.)*
- **GRID-5** `Point3.operator==` — value equality (dropped the struct-can't-be-null `ReferenceEquals` checks).
- **GRID-6** `Grid.ArrayIndexToGridPoint` (instance + static) — integer division `index%width` / `index/width` (was float `FloorToInt`).
- **GRID-7** `TypeMap.SetValuesAtGridPosition` — added `return` in the whole-number branch (no longer clobbers 3 neighbours with `default(T)`).
- **GRID-8** `TypeMap.GetTrimmed(Rect,resolution)` — now writes each sample into the trimmed map and returns it (was building an unused map, discarding samples, returning the untrimmed one). *Note:* the sample-position→rect scaling was left as originally written (intent unclear; method has no live callers).
- **GRID-9** `HeightMapMeshGenerator` — skip-zero test now checks the 4 distinct quad corners (the 2nd term duplicated the 1st; the 4th used `z+1` as the column).
- **GRID-10** `HeightMapMeshGenerator` — right/front edge vertex indexing now uses the correct axis (`sizeMinusOne.x` column / `.y` row), in range on non-square maps.
- **GRID-11** `GridRenderer.cellSize` — z-component uses `1f/gridSize.y` (was a copy-paste `.x`).
- **GRID-12** `Grid`/`Grid3D` `RandomNormalizedPosition` — `Random.Range(0f,1f)` (was int `Range(0,1)` → always 0).
- **GRID-13** `DeleteEmptyFolders` — `.Where` (was `.Select`, so the `.meta` filter was lost) and `GetDirectories(path,"*",…)` (was `string.Empty`).
- **GRID-14** `HandlesX.BeginMatrix` — pushes/pops `Handles.matrix` consistently (was pushing `GUI.matrix`).
- **GRID-15** `UGroup` Ungroup — null-guards the grandparent (scene root) and uses the parent's sibling index (was the grandparent's → NRE / wrong index).
- **GRID-16** `UnityEditorX/SelectionX` — `Except` now compares objects-with-objects (was mixing `objects`/`gameObjects`); `activeObject` setter added the missing `else` so a null value no longer NREs.
- **GRID-17** `SerializedPropertyX.Contains` — `object.Equals` value comparison (was reference `==`, which broke value types).
- **GRID-18** `ScenePathDrawer` — the non-full-path name is now derived from the newly-picked asset (was the stale `property.stringValue`).
- **GRID-19** `SceneDrawer` — the "No Scenes" early return now calls `EndProperty` (balances `BeginProperty`).
- **GRID-21** `AssetDatabaseX.LoadAllAssetsAtPath` — `FindAssets("t:Object", …)` (was `FindAssets("")`).
- **GRID-25** `EditorGUILayoutX` — deleted the commented property-path block + the dead `DrawPropertyViaReflection`.
- **GRID-26** `ExtendedScriptableObjectDrawer` — filled the empty `if(isExpanded){}` (it drew nothing) with `DrawScriptableObjectChildFields`.
- **GRID-27** `Vector2Map` — deleted two commented-out operator blocks.
- **GRID-28** `Point3` — deleted ~240 lines of commented-out `Int3`.
- **GRID-29** `SceneDrawer`/`ScenePathDrawer` — deleted commented `findMethod` blocks + dead `SetSceneNumbers`/`GetSceneIndexes`.
- **GRID-31** `Grid`/`Grid3D` — removed the predicate-less outer `Filter(Filter(…))` no-op wrap.
- **GRID-32** `HandlesX.DrawWheelHandle` — removed commented `Handles.matrix` lines + `Debug.Log` remnants.
- **GRID-40** `TypeMap`/`TypeMap3D` — removed the leftover `DebugX.LogList(values)` on `Resize`.
- **GRID-41** `HierarchyX` — "Collapse All" now recursively collapses via `SetExpandedRecursive` (was a no-op re-set of expanded scenes). *Needs in-editor verification — undocumented internal `SceneHierarchyWindow` API via reflection.*
- **GRID-42** `TransformEditorUtils` — paste-validator MenuItem path aligned to `%&v` (matches the command).
- **GRID-43** `GridRenderer` — removed commented-out debug lines.
- **GRID-44** `ConsoleX` — removed the commented-out `[MenuItem]` (kept the LogEntries reflection — no public equivalent).
- **GRID-45** `TypeMap3D` ctor — removed the redundant second `values` allocation (`Clear()` already allocates).
- **GRID-46** `TypeMap3D.values` — removed `[NonSerialized]` to match 2D `TypeMap` (subclasses no longer lose data).
- **GRID-47** `EditorApplicationX.IsRetina` — culture-invariant `float.TryParse` (was locale-dependent `float.Parse`).
- **GRID-20 / GRID-22 / GRID-23** — verified fine, no change (justified `Path.Combine` wrapper / public API / standard destroyed-GO mesh-cache trick).

### Text + Scene + Collections + Serializable + Audio
- **TXT-1** `Collections/IEnumerableX.cs` — `CompareSize` now counts elements and returns `count >= targetSize` (was true on the first element / false for empty).
- **TXT-2** `Collections/IEnumerableX.cs` — `Min`/`Max<T>(selector)` throw `InvalidOperationException` on an empty sequence (matching `Enumerable.Min/Max`) instead of the ambiguous `-1`. *(Behavior change on empty; audited callers don't rely on `-1`.)*
- **TXT-3** `Serializable Components/SerializableTransform.cs` — content-accumulate `GetHashCode` (`hash*31 + field`) so a zero component hash no longer collapses it to 0.
- **TXT-4** `Text/TextMeshProUtils.cs` — renamed `WorldToScreenRect` params to match caller order; rect output unchanged (naming/clarity only).
- **TXT-5** `TextFader/TextRevealAnimatorCalculatedParams.cs` — divide by `Mathf.Max(1, numCharacters-1)` (no div-by-zero for 1-char text).
- **TXT-6** `WorldSpaceTextGradient.cs`/`GradientArea.cs` — null guards on `gradientArea`/`gradient` under `[ExecuteInEditMode]`.
- **TXT-7** `TextDuplicator.cs` — re-enabled the `duplicated = Instantiate(...)` assignment (was commented → NRE every frame).
- **TXT-8** `StackedTextEffectsController.cs` — destroys the throwaway GameObject and continues when the source isn't a TMP type (was NRE + per-frame GO leak).
- **TXT-9** `BaseTextMeshProEffect.cs` — `OnDisable` null-checks `m_TextComponent` before unsubscribing.
- **TXT-10** `WordWobble.cs`/`CharacterWobble.cs` — skip `!characterInfo.isVisible` and bounds-check `vertexIndex` before indexing mesh verts/colors (robust to rich text / spaces / changed text).
- **TXT-11** `RuntimeSceneSet.cs` — `IsIncludedInBuildSettings()` returns true only when all scenes ARE included (`== 0`); the single caller inverted to keep the same UI meaning.
- **TXT-12** `RuntimeSceneSetLoadTask.cs` — `UnloadSoft` marks a scene for unload only if contained by NO loaded set (computes contained-by-any first); no longer unloads a scene still used by another set.
- **TXT-13** `RuntimeSceneSetLoader.cs` — cancelled branch fires `OnCompleteTaskQueue` with the last non-cancelled task (was the cancelled `lastLoadTask`); edit-mode broadcast sends to each child (was the root every iteration).
- **TXT-14** `RuntimeSceneSet.cs` — `LoadInEditor()` early-returns on empty setup (was `GetSceneAt(-1)`).
- **TXT-15** `RuntimeSceneSetEditor.cs` — null guards on `sceneAssets`/`scenePaths`.
- **TXT-19** `GradientArea.cs` — `Clamp1Infinity` body simplified to `Mathf.Max(value, 1)`.
- **TXT-21** `Collections/ArrayX.cs` — `GetShiftedRepeating` delegates to the identical `ListX` impl.
- **TXT-22** `Collections/ShuffleBag.cs` — both `Shuffle` overloads delegate to `ListX.Shuffle` (verified identical Fisher-Yates + seed handling).
- **TXT-23** `Collections/ProbabilityList.cs` — dropped the needless `.ToArray()`; non-generic `GetEnumerator()` delegates to the generic one (was yielding `null`).
- **TXT-25** `WordHighlightTextEffect.cs` — removed the fully commented-out dead body (kept a clean override).
- **TXT-26** `CurvedWorldTextEffect.cs` — hoisted the invariant TRS + inverse out of the per-vertex loop (private helper signature updated; output identical).
- **TXT-27** `GradientArea.cs` — deleted the dead `CreateGradient` family + `Radians2Vector2`/`Degrees/RadiansBetween` (no callers).
- **TXT-28** `RuntimeSceneSetLoader.cs` — removed the dead `_debugLogging` field (live `debugLogging` untouched); the two `BroadcastMessageScene` overloads left (not a trivial dedup).
- **TXT-34 / TXT-35** — removed commented-out dead blocks across `TextMeshProUtils.cs`, `SerializableCamera.cs`, `Audio/*`, the Text Effects files, and the scene loader.
- **TXT-36** `RuntimeSceneSetLoader.cs` — gated the previously-raw `Debug.Log`s on `debugLogging`.
- **TXT-37** `RuntimeSceneSet.cs` — fixed "includesed"→"included" typos; removed commented `// [SceneAttribute]` and the redundant empty ctor.
- **TXT-16 / TXT-17 / TXT-18 / TXT-20 / TXT-24** — left as-is (BCL-overlapping public helpers used library-wide, delicate build-settings code, and invasive dedups).

### Geometry
- **GEO-1** `Point/PointRect.cs` — `Equals(PointRect)` now compares `width`/`height` (was using `y` for both), matching `==`.
- **GEO-2** `Point/PointRect.cs` — `operator *`/`/` call `Multiply`/`Divide` (were `Add`/`Subtract`).
- **GEO-3** `Point/PointRect.cs` — `max` setter places the corner at the value (`xMax = value.x` → `width = value.x - x`), symmetric with `min` (was adding extents on top).
- **GEO-4** `Polygon/StarPolygon.cs` — `new Vector2(Sin, Cos)` at all three sites (was `Sin, Sin` → collinear).
- **GEO-5 / GEO-44** `Polygon/StarPolygon.cs` — `rotation` now applied in both `RegularPolygonToPolygon` and `StarPolygonToPolygon` (previously ignored in every path).
- **GEO-6** `Polygon/Polygon.cs` — `FindPointInDirection` sets `bestScore = score` (was `score = bestScore` → returned the last vertex).
- **GEO-7** `Polygon/Polygon.cs` — `centroid` loop wraps to include the closing edge (`b = vertices[(i+1)%n]`); `f*3` divisor left correct.
- **GEO-8** `Polygon/Polygon.cs` — `Scale(Polygon)`/`Scale(Vector2)` assign the `Vector2.Scale` result back (were no-ops).
- **GEO-9** `Polygon/Polygon.cs` — `GetVertexDegreesInternal(int)` returns `Vector2.Angle(leftDir, rightDir)` (the true interior angle).
- **GEO-10** `Polygon/Polygon.cs` — `RayPolygonIntersection` seeds `bestDistance = float.MaxValue` (was `ray.magnitude` → rejected hits past distance 1).
- **GEO-11** `Polygon/Polygon.cs` — `PointInPolyFromIndex` uses the negative-safe `Mod` (was raw `%` → possible negative index).
- **GEO-12** `Polygon/Polygon.cs` — `poleOfInaccessibility` gated on a `_hasComputed` flag (not `magnitude==0`); `computePoleOfInaccesibility` seeds `maxX/maxY = float.MinValue`. *(Still compute-once/no-invalidation, matching prior behavior.)*
- **GEO-13** `Sphere/Sphere.cs` — `CreateFromBounds`/`CreateFromPoints` (+ internal `CalculateWelzl` overloads) made `static` (they ignored `this`). *(Public signature change; no in-project callers — the camera tools use the separate `BoundingSphere` class.)*
- **GEO-14** `Sphere/Sphere.cs` — `CreateFromBounds` radius = `bounds.extents.magnitude` (encloses the corners; was the max single extent).
- **GEO-15** `Sphere/Sphere.cs` — `CalculateWelzl` no longer early-returns after the first out-of-sphere point (continues the scan, matching the working `BoundingSphere`); dropped the reachable `LogError`; 2-support overload writes a local, not the instance field. *(CreateFromPoints has no in-project callers.)*
- **GEO-16** `Line/Line3D.cs` — `LineIntersectionPoint` interpolates z along line1 at the solved XY parameter so the point lies on the line (was always z=0). *(Still an XY-projection solve — documented limitation for genuinely non-intersecting 3D lines.)*
- **GEO-17** — left as-is: `Point/Point.cs` largely duplicates `Vector2Int`, but `Point` is a pervasive public type used across the library; replacing it would be a massive breaking change.

### Algorithms + Camera + Spline
- **ACS-1** `Spline System/SplineBezierPoint.cs` — `SetAuto`/`SetAutoDistance`/`CreateAuto` fall back to the existing neighbour when one endpoint is null (was unboxing null → NRE at spline ends; `Spline.CreateFromPoints` threw for any real spline).
- **ACS-2** `Algorithms/Noise/NoiseSample.cs` — `operator /(float, NoiseSample)` returns `a / b.value` with derivative `-a·b'/b²` (was `b.value / a`).
- **ACS-3** `Algorithms/Noise/NoiseSample.cs` — `operator /(NoiseSample, NoiseSample)` derivative is now the quotient rule `(a'b − ab')/b²`.
- **ACS-4** `Camera/Camera Properties/CameraPropertiesModifier.cs` — Axis+Additive rotates by `properties.axis` (was `properties.targetPoint`).
- **ACS-5** `Algorithms/UpscaleTools.cs` — `IsOnVisibilityMap` bounds check restored (was always true → IndexOutOfRange at edges); the `fill` flag now gates the interior-cell block.
- **ACS-6** `Algorithms/UpscaleTools.cs` — the interior `pointRect` is only built for cells with right/down neighbours (via the `fill` gate), keeping indices within `colorMapSize`.
- **ACS-7** `Algorithms/Pathfinding/AStar.cs` — target fast-path compares `currTestEntry == _targetEntry` (was `GraphEntry` vs `GraphElement` → always false). *(Vendored — local fix.)*
- **ACS-8** `Algorithms/Pathfinding/AStar.cs` — async assert guards on `solutionList.solution` (was `solutionList` → NRE when no path found). *(Vendored — local fix.)*
- **ACS-9** `Algorithms/Noise/SimplexNoiseGenerator.cs` — `oneMinusContrast` clamped via `Mathf.Max(…, 1e-6f)` to avoid divide-by-zero when `contrast == 1`. *(Vendored — minimal guard.)*
- **ACS-10** `Camera/Camera Properties/CameraModifierZone.cs` — `LateUpdate` null-checks `target` before dereferencing `target.position`.
- **ACS-11** — left as-is: `CameraProperties.GetPitch`/`GetYaw` return signed angles ([-90,90]/[-180,180]); `Quaternion.eulerAngles` wraps to [0,360), so `LookRotation(dir).eulerAngles` is NOT equivalent (would change results for negative angles).

### Island detector — iterative rewrite
- **STR-1 / STR-2** — hang fixed. Both detectors now walk a fixed collection and *pop* seeds (`foreach` / `Queue.Dequeue`) instead of peeking `[0]`; an island is created only for a valid seed, so no empty `OwnedIsland`s.
- **STR-3** — the dead "re-add already-connected point" branch is gone (replaced by the shared `FloodFill`). Residual `static`-collection reentrancy is tracked by STR-11.
- **STR-7** — recursion replaced by an explicit `Stack<Coord>` → no stack-overflow risk on large islands.
- **STR-9** — removed the redundant `this.GetAdjacentPoints = GetAdjacentPoints` in the owned ctor (base already sets it).
- **STR-10** — the `*WithSameOwner` copy-paste is gone; the owned detector reuses base `FloodFill` via a `canJoin` predicate + an `onValidSkip` callback that re-seeds differently-owned neighbours (preserving the discover-adjacent-owner-regions behaviour).
- **STR-20** — `List` work-set (O(n²) at grid scale) replaced by `HashSet` membership + a local `Stack`/`Queue` → O(n).
- ⚠️ Behavioural rewrite; **not compile-verified** (the community "MCP for Unity" server is down). Public API (ctors + `FindIslands()` signatures) is unchanged.

### UI (duplication / dead code)
- **UI-29** `UIGradient.cs` — radial-gradient distance now computes `dx*dx + dy*dy` directly instead of `Mathf.Pow(Abs(...),2f)`; `Pow` is a general exp/log call (slower than a multiply, and per-vertex here), and squaring makes the `Abs` redundant. Faster and clearer, same result.
- **UI-30** `Line/AdvancedUILineRenderer.cs` — `AddQuad` now triangulates along the **0–2 diagonal** `(0,1,2)+(2,3,0)`, matching `VertexHelper.AddUIVertexQuad` (was the 1–3 diagonal). For the convex line quads this produces the same visible fill; it was an oversight, and matching Unity's standard also removes the divergence noted in UI-30/36.
- **UI-41** `RoundRect/RoundRect.cs` — removed the dead `_debugBool` serialized field, the never-called `AddMiddle`, and the unused `corner1/2`/`pivot` locals in `CalcOuterGeom`; `MakeRoundRectOutlineGeometry` now uses its own `vertices`/`indices` params (callers already pass `vertexBuffer`/`indexBuffer`, so behaviour-identical) instead of reaching for the fields, consistent with `AddCorner`/`AddEdge`/`MakeHardQuad`.
- **UI-42** `ExtendedScrollRect/*` — promoted the runtime's nested `EnumToFlagValue` local function to a `public static ExtendedScrollRect.EnumToFlagValue`; `IsEdgeFlagSet` and the editor now share it (editor's verbatim copy deleted). Kept UI-local rather than pointing at `Extensions/FlagsX.EnumToFlagValue` (portability).
- **UI-43** `Grid Layout/*` — extracted the size+position placement math into `GridLayout.ApplyToRectTransform(rt, coord)`, now called by both `GridLayoutItem` and `GridLayoutApplier` (was copy-pasted); merged `GridLayoutApplier`'s two separate `if XAxis/else YAxis` blocks into one.
- **UI-45 (+ boxedValue sweep)** — replaced the fragile `GetValueFromObject<T>` reflection with the built-in `SerializedProperty.boxedValue` (Unity 2022.1+) everywhere it's a clean equivalent (a SerializedProperty reading its own value): `Grid Layout/Editor/GridLayoutEditor.cs` (`GetBaseProperty<T>` + call site), `Extensions/UnityEditorX/Editor/SerializedPropertyX.cs` (both `GetBaseProperty` extensions), and `Property Drawers/EnumFlag/Editor/EnumFlagDrawer.cs` (`GetBaseProperty<T>`). All use `boxedValue is T t ? t : default` to preserve the old "return `default(T)` on type mismatch" behaviour (e.g. `EnumFlagDrawer.IsSupported` relies on `null` for a non-enum). *Deliberately NOT converted — not clean `boxedValue` cases:* `ReflectionX.GetValueFromObject(object, path)` (walks an arbitrary object graph, no SerializedProperty), `ButtonDrawer` (needs the live containing instance via `propertyPath.BeforeLast(".")` to invoke a method — `boxedValue` would hand back a boxed struct copy), and `BaseIfAttributeDrawer` (reads a sibling bool at a computed path, not the property's own value). The legacy public `GetValueFromObject<T>` helpers are left in place (superseded).
- **UI-48** `Draggable/MultitouchDraggableTouchSimulator.cs` — the verbatim `ScaleAround`/`ScaleAroundRelative` copies now forward to `MultitouchDraggable`'s (same folder); public signatures kept, duplicated bodies gone.
- **UI-49** `Draggable/Draggable.cs` — collapsed the `dragVelocity` property (whose setter guard `if(_dragVelocity==value) return;` skipped nothing — no side effect) into a plain private field. *Left unchanged: `GetPosition` (protected virtual) and `distanceFromTarget` (public) — they're public API used by consuming project code, not dead.*
- **UI-51** `Background Blur UI/BackgroundBlurUI.cs` — cached `Shader.Find` in a static field (was re-found on every `shader` access); dropped the redundant second `Mathf.Clamp(stepSize,…)` (already clamped two lines earlier).
- **UI-52** `AlphaHitTestThresholdSetter.cs` — the `image` getter now caches `GetComponent<Image>()` in a field (`_image != null ? _image : (_image = …)`), robust across domain reload and destroyed components; was calling `GetComponent` every `Update`.
- **UI-53** `Swipe View UI/SwipeView.cs` — `OnEnable` now calls `base.OnEnable()` (Unity convention; base is an empty `UIBehaviour` stub so behaviour is unchanged, but the omission wasn't by design).
- **UI-44** `Grid Layout/GridLayout.cs` — **finished the centering** (it was unfinished, not a spare param): `CalculatePositionForGridCoord(...pivot)` now adds `+ itemSize*pivot`, so `pivot` shifts within the cell (0 = origin, 0.5 = centre, 1 = far edge) and `GetCenterPositionForGridCoord` actually centres. No in-project callers pass a non-zero pivot except the `Center*` variants, and the `pivot=0` default path (GridLayoutItem/Applier) is unchanged, so no behaviour change for existing usage — it just makes the `Center`/pivot overloads work.
- **UI-46** `SLayout/SLayoutAnimation.cs` — the two `ThenAnimateInternal` overloads now delegate to one shared body with an `Action<SLayoutAnimation> configureEasing` (sets `_customCurve` or `_easingFunction`). Behaviour-identical; public parameterized ctor untouched.
- **UI-47** `SLayout/SLayout.cs` — all `Animate`/`AnimateCustom`/`After` overloads route through one private `StartNewAnimation(...)` factory. **Behaviour change (approved):** edit-mode `CompleteImmediate()` — previously only on the `AnimationCurve` overload — now runs on every entry point, so easing/custom/`After` animations also settle to their end state in edit mode.
- **UI-50** `AbsoluteRectTransformController.cs` — dropped the redundant per-frame `Update()` `Refresh()`; kept the `LateUpdate()` one (runs after layout → final rects, still catches parent movement). Resizes are already handled immediately by the `OnRectTransformDimensionsChange` override. A full input-signature dirty-flag was rejected as unsafe (Refresh reads untracked screen/safe-area/parent state).
- **UI-54** `SLayout/SLayout.cs` — extracted `PivotOffsetX`/`PivotOffsetY`/`PivotOffsetToTopY` static helpers; `GetPivotPos` and the ~18 inline `pivot.x*rect.width` / `pivot.y*rect.height` / `(1-pivot.y)*rect.height` sites across the layout getters now use them. Pure-equivalent substitutions (verified per line); the one hairy `* 2` compound line in the "confuses me" block was left untouched.
- **UI Tidying** — **UI-68** removed `new Color[size];;` double semicolon; **UI-69** dropped unused `System.Collections`(`.Generic`) usings in `UIMonoBehaviour`, `MarkLayoutElementForRebuild`, `SLayoutCanvasTimeScalar`; **UI-70** `Math.Abs` → `Mathf.Abs` in `UIGradient` (nested `Type` enum left — renaming is a public-API change); **UI-71** `scaleMultipler` → `scaleMultiplier` in `ExtendedCanvasScaler` (+`[FormerlySerializedAs]` to keep serialized data, editor string updated); **UI-72** removed dead commented blocks / fixed the `#pragma warning disable` (added `restore`) in `ExtendedScrollRect`, `GridLayout`, `GridLayoutApplier`, `GridLayoutEditor`, `UIAdvancedLineRendererEditor`, `UIPolygonEditor`; **UI-73** dropped the redundant self-namespace import in `AdvancedUILineRenderer`; **UI-74** typos (`neededCpacity`, `Resoloution`→property rename `Resolution`, `primatives`, `Caluclates`); **UI-75** removed the dead commented `// [SerializeField]` cruft in `RoundRectPolygonUI` (the `-1`s are the correct ILayoutElement "unset" values — left non-serialized, documented); **UI-76** removed unused `static int targetPage`, the empty disabled-group and superseded commented blocks in `SwipeViewEditor`; **UI-77** kept `WorldSpaceUIElement._OnDrawGizmos` (dev debug gizmo) with a clear "DEVELOPMENT/DEBUG, rename to enable" comment; **UI-78** made `SLayoutAnimator._instance` private, removed commented ctor calls, fixed "the the"/"simplied". *Per request, disabled/commented **gizmo** debug code (SwipeView `OnDrawGizmos`, WorldSpaceUIElement `_OnDrawGizmos`) was KEPT and labelled as development-only rather than deleted.*
- **UI-33** `SLayout/SLayoutProperty.cs` — `SLayoutFloatProperty.Lerp` now calls `Mathf.LerpUnclamped(v0,v1,t)` (was the hand-rolled `v0 + t*(v1-v0)`, which is identically the unclamped lerp; the "allow extrapolation, not clamped" comment is preserved).
- **UI-34** `Draggable/MultitouchDraggable.cs` + `MultitouchDraggableTouchSimulator.cs` — dropped the `sqrMagnitude == 1 ? direction : direction.normalized` guard; `Vector2.normalized` already returns the unit vector (and returns zero for a zero-length vector, which the guard didn't change).
- **UI-38** `Line/UILineRenderer.cs` — `GetDrawingPoints1` now forwards to `GetDrawingPoints0`; they were two implementations of the same fixed-segments-per-curve Bézier sampling with identical output (verified: same curve count, same endpoint handling). Kept as a public method so the `BezierType.Improved` switch case still resolves.
- **UI-39** `Line/AdvancedUILine.cs` — removed the dead private helpers `AddVert`, `PointInPolyFromIndex`, `GetIndexInPolyAtPoint`, `GetIndexInPolyLyingOnLineBetween` and the `epsilon` const only they referenced (no other in-project callers).
- **UI-40** `Line/AdvancedUILineRenderer.cs` — removed the `vertexBuffer`/`indexBuffer` static fields and the commented-out `AddUIVertexStream` block in `AddQuad` that were their only references.
- ⚠️ **Not compile-verified in-editor** (community "MCP for Unity" server down). Most changes are behaviour-preserving; ones worth an in-editor glance: **UI-30** (quad triangulation diagonal), **UI-44** (grid centering now applies a pivot offset — check `Center`/pivot overloads if used), **UI-47** (edit-mode `CompleteImmediate` now on all SLayout animate paths), **UI-51** (blur caching).

### Components (non-UI) + Cross-cutting + Misc
- **CMP-1** `PolygonRenderer/BasePolygonRenderer.cs` — `OnEnable` collapsed the dead `if(Application.isPlaying) DestroyMesh() else mesh.Clear()` (unreachable inside `if(!isPlaying)`) to just `mesh.Clear()`.
- **CMP-2** `BasePolygonRenderer.cs` — removed the redundant inner `if(colorMode == ColorMode.Shape)` nested inside `else if(colorMode == ColorMode.Shape)` (always true).
- **CMP-3** `Input/InputPoints/Mouse/MouseInputButton.cs` — `ToString` now prints `down` under the "Down" label (was `held`).
- **CMP-4** `Input/InputX.cs` — `acceptInput` setter collapsed the identical if/else `ResetInput()` branches to one call.
- **CMP-7** `Input/InputPoints/InputPoint.cs` — `UpdateDeltaMovement` dropped the `lerpedMovement += deltaPosition.magnitude` that double-counted on top of the lerp.
- **CMP-9** `Render Texture Creator/Editor/RenderTextureCreatorEditor.cs` — null-guarded the `RenderTexture` before reading `.width/.height` (`rt != null ? new Vector2Int(...) : Vector2Int.zero`).
- **CMP-10** `FPSManager/FPSManager.cs` — `averageFrameTime` guards divide-by-zero (`averageFPS > 0 ? 1f/averageFPS : 0f`).
- **CMP-12** `HideFlags/Editor/SetChildHideFlagsEditor.cs` — the undo and change-check loops now call `ApplySettings()` on each selected target (`(t as SetChildHideFlags)?.ApplySettings()`), not just the primary.
- **CMP-13** `GUIDrawer.cs` — `OnGUI` iterates a snapshot (`new List<>(drawActions.Values)`) so a callback that Start/StopDrawing mid-iteration can't throw "Collection was modified".
- **CMP-14** `ChangeCheckers/TransformChangeChecker.cs` — the edit-mode `!useInEditMode` path now also `enabled = false` (symmetric with the play-mode path) instead of returning every frame.
- **CMP-15** — `Gesture.CompleteGesture` clears `inputPoints` instead of nulling it (no post-completion NRE); `TextBackgroundHighlightEffect` auto-wires `text` in `OnEnable`/`OnValidate` (GetComponent → GetComponentInChildren) when unassigned; `MonoInstancer.CompileReset` now also invalidates the edit-mode cache on `EditorApplication.hierarchyChanged` and `EditorSceneManager.sceneOpened` (mark-dirty only; the FindObjects rescan stays lazy).
- **CMP-19** — *reverted; left as `UnityStats.screenRes`.* `Handles.GetMainGameViewSize()` returns the *logical* Game-view size, whereas `UnityStats.screenRes` reports the actual rendered backbuffer resolution; they diverge under the Game-view Scale slider / Low Resolution Aspect Ratios. Since this sizes a RenderTexture and the "locale-fragile" concern is weak (the `x` delimiter is fixed), not a safe drop-in — kept the original with an explanatory comment.
- **CMP-41** `BasePolygonRenderer.cs` — `GetMesh` reuse branch now compares `meshFilter.sharedMesh?.name` (was the GameObject/component `name`, so reuse never triggered).
- **MISC-1** `NoiseSampler/Editor/NoiseSamplerPropertyDrawer.cs` — "Create" assigns `new NoiseSampler()` (was `new SpringHandler(...)`).
- **MISC-2** `GLDebug/GLDebug.cs` — `matZOff` getter fixed to use `_matZOff`; `OnPostRender` line lists un-swapped (`matZOn`↔`linesZOn`, `matZOff`↔`linesZOff`).
- **MISC-4** `Version Control/VersionControlX.cs` — `GetGitBranch` strips `refs/heads/` (keeps slashes in branch names) with a last-slash fallback.
- **MISC-5** `Property Curve/PropertyCurve.cs` — `AddKey` inserts at the order-preserving index (`closestIndex+1` when the closest key is earlier) and returns after an exact-time replace (was inserting out of order / duplicating).
- **MISC-15** `GLDebug/GLDebug.cs` — `DrawCircle` connects consecutive points and closes the loop.
- **XC-1** `GetHashCode` — `Range` and `CameraProperties` now `hash = hash*31 + field` (were `hash *= field`, which collapses to 0 on any zero-hash field); `Polygon` now content-hashes its vertices to match `SequenceEqual`. (`SerializableTransform`/`Point`/`PointRect`/`AdvancedUILine` already correct.)
- **XC-2** — already resolved via GEO-8/UI-3 (both `Scale` methods assign the `Vector2.Scale` result back).
- ⚠️ **Not compile-verified in-editor** (community MCP down). Behaviour-preserving except the intended fixes; worth an in-editor glance: **CMP-1/CMP-41** (polygon mesh lifecycle/reuse), **CMP-14** (self-disable in edit mode), **MISC-2/MISC-15** (GLDebug visuals).

### CMP refactors + Property Drawers + UEX bugs
- **CMP-20** `Events/TriggerListener.cs` — 12 collision/trigger handlers → generic `Dispatch<T>(collider, layer, UnityEvent<T>, Action rawEvent)` one-liners; C# events fired via `() => Xxx?.Invoke(c)` (custom delegate types).
- **CMP-21** `ChangeCheckers/TransformChangeChecker.cs` — the 4 change checks share a `FireChange(specificDelegate, suffix)` tail; per-field `!=` checks stay inline (preserves Unity's approximate Vector3/Quaternion equality).
- **PD-17** `SetCase/Editor/SetCaseDrawer.cs` — `BeginProperty` + change-check so `stringValue` is only written on edit (was every repaint → dirtied objects / clobbered multi-select).
- **PD-19** `Property Drawers/Editor/BaseVectorToggleDrawer.cs` (new) — shared base looping N axes; `Vector2ToggleDrawer`/`Vector3ToggleDrawer` reduced to `GetAxes`/`SetAxes` + `IsSupported` overrides. Behaviour-identical.
- **UEX bugs** — **UEX-1** Vector2Curve self-recursion; **UEX-3** TextureX GPUScale keeps the temp RT active until ReadPixels (try/finally restores active + releases); **UEX-4** Vector3X.Reflect sign; **UEX-5** SelectionX objects self-assign + activeObject null; **UEX-6** Rigidbody2DX torque direction; **UEX-8** ColliderX → Collider.ClosestPoint; **UEX-9** ImmediateAncestors `(-1,-1)`; **UEX-10** BetterBroadcastMessage per-child; **UEX-11** AnimationCurveX EaseIn tangent / EaseOutInvert delegate / ks[0] tangents; **UEX-12** FindIndexPosition single-element; **UEX-13** RepeatInclusive min==max; **UEX-14** EventSystemX null pointerEvent; **UEX-15** PhysicsX degenerate up; **UEX-16** RayX sphere radius; **UEX-17** GeometryX planes.Length + null; **UEX-18** PlaneX honours Raycast bool; **UEX-19** ReflectionX path bounds guards + no-op removed (struct limitation documented); **UEX-20** TextureX Create textureFormat + mismatch early-return. *(Done by 4 parallel agents; every diff reviewed here.)*
- ⚠️ **Not compile-verified in-editor** (community MCP down). New file `BaseVectorToggleDrawer.cs` ships with a hand-authored `.meta` (Unity will accept/normalise it on import).

### Color (ColorX / HSVColor / HSBColor) + UEX-19 proper fix
- **UEX-19 (proper fix)** `ReflectionX.SetValueFromObject` fully rewritten: a recursive `SetValueRecursive` walks the path, sets the leaf on its real parent, and re-assigns boxed **struct** intermediates back up the chain (nested structs now work — the old version's `fieldInfo.SetValue(obj, …)` targeted the root and rarely fired). Also now supports **list-element** paths (`field.Array.data[i]…`), which the old loop never handled. No in-project callers, so purely an improvement.
- **UEX-2 + UEX-25 + UEX-38** `HSVColor`/`HSBColor` — `FromRGBA`/`ToRGBA` now delegate to Unity's `Color.RGBToHSV`/`HSVToRGB` (h scaled to/from degrees to keep the public convention), killing the hand-rolled conversion, the unreachable trailing `else`, and the h-convention muddle. `Lerp` now actually interpolates hue (`Mathf.LerpAngle(a.h,b.h,t)` → `h = angle`; was stuck at 0/red). No in-project callers — external API shape (fields, static methods, operators) unchanged.
- **UEX-61 (ColorX part)** `ColorX.BlendOverlay` now implements a real per-channel overlay (`base<0.5 → 2·base·blend, else 1−2(1−base)(1−blend)`) instead of the commented-out stub that returned `color2`.
- **UEX-62 (ColorX/HSBColor parts)** `ColorX.Average` returns `Color.clear` on an empty list (was `LogError` then divide-by-zero → NaN); removed `HSBColor.Test()` debug scaffolding.
- **UEX-26** — assessed as convenience API (not a bug); kept. See active note.
- ⚠️ **Not compile-verified in-editor** (community MCP down). Behaviour changes worth a glance if used: `HSVColor`/`HSBColor` conversions now use Unity's math (semantically equivalent, minor float differences possible) and `Lerp` now interpolates hue instead of returning red.

### CMP refactors + tidying (round 2)
- **CMP-22** `ChangeCheckers/GameObjectChangeChecker.cs` — extracted the duplicated play/edit-mode guard into `ShouldRun()`, called by `Update`/`OnDestroy`.
- **CMP-23** `Input/InputX.cs` — the two ID *item*-getters now delegate to the *index*-getters (`TryGetTouchByID`→`GetTouchIndexByID`, `GetFingerByID`→`GetFingerIndexByID`), removing 2 of the 4 duplicated search loops (no generics/allocations). Left the 6 mouse handlers as-is — they're genuinely non-uniform (Left has fake-finger logic, Click calls `Tap`, Right/Middle are plain pass-throughs), so a shared template would obscure rather than clarify.
- **CMP-39** `ViewAnimator/ViewAnimationEvent.cs` — the "starts after ending" warning now includes the event name and start/end times.
- **CMP-40** `Audio/AudioSource/AudioSourceManager.cs` — fixed `OnPause!= null` spacing; the `clip`-null short-circuit before `clip.samples` was **verified safe** (`clip` is the captured `audioSource.clip`, and `clip == null ||` short-circuits) — switched the later access to the captured local for clarity.
- ⚠️ **Not compile-verified in-editor** (community MCP down). InputX is a CRLF file; edits applied via perl.

### Editor Tools + Property Drawers
- **ED-1** `SerializedEditorSettings.cs` — EditorPrefs key uses `typeof(T).FullName` (was `.Name`, so same-named types in different namespaces collided). *Persisted-key change: old saved settings won't be found on first load — a fresh default is created; harmless.*
- **ED-2** `CommentComponentEditor.cs` — `Save()` now `Undo.RecordObject(data,…)` before the write and `EditorUtility.SetDirty(data)` after (edits are undoable and persist).
- **ED-3** `GameLayersClassGenerator.cs` — added `SanitizeIdentifier` (strips non-identifier chars, prefixes `_` if empty/leading-digit) so a layer like "2D Collider" generates `_2dCollider` instead of the uncompilable `2dCollider`.
- **ED-7** `ScreenshotSaverTextureFormat.cs` — removed the dead, self-admitted-unused `FormatToDepth`. (The curated enum→`TextureFormat` mapping is intentional — kept.)
- **ED-9** `CameraInfoWindow.cs` — the two `new GUIStyle(...)` allocated every OnGUI are now cached in lazily-initialised static fields (constructed inside OnGUI, as GUIStyle requires). Label rows left as-is (their widths/controls differ).
- **ED-10** `ScreenshotExporter` — extracted the identical timestamp filename into `ScreenshotExporter.DefaultFileName()`, used by both `ScreenshotSaverComponent` and `ScreenshotSaverWindow` (the export path itself was already shared via `ScreenshotExporter.Export`; the rest legitimately differs — component is runtime/unfinished, window is editor).
- **ED-11** `ScreenshotSaverWindow.cs` — removed the unconditional `Repaint()` at the top of `OnGUI` (continuous-repaint / 100%-CPU anti-pattern); the window still repaints on interaction and `Update()`.
- **ED-13** `CommentComponent.cs` — removed the unused `using System.Collections;`.
- **PD-2** `BaseVectorToggleDrawer.cs` — reads "on" as `axes[i] != 0` (was `== 1`) and only writes on an actual toggle (`BeginChangeCheck`/`EndChangeCheck` + `BeginProperty`), so a pre-existing non-0/1 value is shown as on and preserved rather than destroyed on the next repaint.
- **PD-3** `SetPropertyDrawer.cs` — (a) `ApplyModifiedProperties()` now runs *before* the C# property setter, so it observes the applied value (not the stale pre-apply one); (b) `PropertyInfo` resolved with `NonPublic` binding and cached (keyed on type + name).
- **PD-4** `RegexDrawer.cs` — null/empty pattern is skipped, `Regex` compilation is cached by pattern string and wrapped in try/catch (invalid pattern logs once, draws the field normally, no per-repaint throw).
- **PD-5** `OnChangeDrawer.cs` — `ApplyModifiedProperties()` before invoking the change callbacks, so they see the new value (still play-mode-gated by `MonoBehaviour.Invoke` — known limitation).
- **PD-9** `PositionHandleDrawer.cs` — bare `catch {}` → `catch(Exception e) { Debug.LogException(e); }`.
- ⚠️ **Not compile-verified in-editor** (community MCP down). Done by 2 parallel agents + manual Screenshot-cluster edits; every diff reviewed here (and hardened the PD-3 `PropertyInfo` cache to key on the property name too, guarding drawer-instance reuse).

### CMP refactors (round 3) + PD/UEX
- **CMP-24** `Input/InputPoints/KeyboardInput.cs` — extracted `IsDirectionKeyHeld(arrow, wasd, alsoUseWASD)`; the cardinal/combined direction methods delegate (signatures unchanged).
- **CMP-25** `PolygonRenderer/Editor` — new generic `BasePolygonRendererEditor<T> : BaseEditor<T> where T : BasePolygonRenderer` holds the (previously byte-identical) editor body; `PolygonRendererEditor`/`PolygonOutlineRendererEditor` are now one-line subclasses. (Component editors for a shared-base family — not property drawers — so the drawer-portability rule doesn't apply.)
- **CMP-27** `ViewAnimator/ViewAnimator.cs` — the 3 cancel methods share a `CancelEventsWhere(predicate, completeEvents)` reverse-loop (null predicate = cancel all).
- **CMP-28** `Transform Copier/TransformCopier.cs` — shared `ShouldCopy()` guard for `OnEnable`/`Update`/`FixedUpdate`; each method keeps its extra per-loop guard.
- **CMP-29** `CoroutineHelper.cs` — `Delay`/`DelayRealtime`/`DelayFrame` share `ExecuteAndReturn(routine)`; signatures unchanged; added a comment flagging the "returned coroutine is already running" gotcha.
- **PD-8** `Info/Editor/InfoDrawer.cs` — help-box height now measured via `EditorStyles.helpBox.CalcHeight` (was a fixed 38 that clipped multi-line text). Self-contained.
- **PD-30** `EnumButtonGroup/Editor/EnumButtonGroupDrawer.cs` — added the `_propertyPath != property.propertyPath` re-init guard (copied inline from its sibling, not shared) so a reused drawer instance rebinds per array element.
- **UEX-22** `ScreenX.cs` — guarded `int.Parse(UnityStats.screenRes.Split('x'))` (length + `TryParse`, falls back to `Screen.width/height`).
- **UEX-23** `OnGUIX.DrawCircle` — early-out when `numPoints < 2` (was /0 at `numPoints==1`).
- **UEX-24** `GizmosX` — added the missing `return` after the degenerate full-circle draw in `DrawWireArc`/`DrawWireArcSegment`.
- **UEX-27** `ColliderX.GetClosestPoint` — resolved by UEX-8 (now delegates to `Collider.ClosestPoint`); stale summary comment corrected.
- **UEX-28** `HashSetX.AddRange` — now `hashSet.UnionWith(toAdd)` (kept the public API, delegates to the built-in).
- ⚠️ **Not compile-verified in-editor** (community MCP down). Done by 2 parallel agents + manual edits; every diff reviewed. New file `BasePolygonRendererEditor.cs` ships with a hand-authored `.meta`.

### CMP-37 commented-block sweep + UEX fixes (round 4)
- **CMP-37** — swept every commented-out block across the enumerated files. Deleted the abandoned-alternative / dead-cruft blocks: `TouchInputSimulator` (old single-`Pinch` finger-sim + `UpdateTest` harness + matching OnGUI), `Pinch` (`CheckForPinchEnd` referencing removed fields), `InputX` (dead `OnDrag`, verbose diagnostic logs, disabled pinch-start guard, stray fragments), `PolygonOutlineRenderer` (dup `tintColor`, old mitered-quad meshing), `PolygonRenderer` (double-sided branch), `LineDraw` (JS-port auto-close leftovers + alt round-cap), `CoroutineHelper` (speculative note; kept usage examples), `LockTransformEditor` (PropertyFields on now-nonexistent fields — verified), `FPSManager` (per-call log), `RenderTextureCreator` (old `screenSize` + disabled `ReleaseRenderTexture`; kept the multi-display TODO). `EnforceDecendent`'s disabled auto-reload hook → replaced with a one-line note on why it's off. `ScriptableSingleton`'s editor-fallback stub kept (a real feature stub, not cruft — Unity's `UnityEditor.ScriptableSingleton` is editor-only and not a replacement for this runtime one).
- **CMP-37 (diagnostics → toggles)** — the three commented gizmo/debug-visualisation blocks were revived as opt-in `[SerializeField] bool drawDebugGizmos;` toggles under `#if UNITY_EDITOR` (first line `if(!drawDebugGizmos) return;`), after verifying every referenced symbol still compiles: `PolygonOutlineRenderer.OnDrawGizmos` (vert corner spheres), `PolygonRenderer.OnDrawGizmosSelected` (UV extent + axis arrows), `TextBackgroundHighlightEffect.OnDrawGizmos` (TMP line-metrics; its `using UnityEditor;` is now `#if UNITY_EDITOR`-guarded).
- **UEX-43** `AnimationCurveX.cs` — `RemoveKeysBetween` now exclusive (`> start && < end`), `RemoveKeysBetweenAndIncluding` inclusive (`>= start && <= end`); they were byte-identical.
- **UEX-44** `Vector3Curve.cs` — `EstimateClosestTimeToValue` implemented (100-sample scan over the key time range, returns the sample time minimising `(Evaluate(t)-vector).sqrMagnitude`; empty-curve → 0). `Vector2Curve` has no such method to port, so implemented fresh. Removes the `Debug.Log("TODO")` (also closes UEX-62's Vector3Curve item).
- **UEX-45** `PhysicsX.cs` — verified NOT a real dup: sphere-cast rays are **parallel** (offset around a circle), cone-cast rays **diverge** from one origin toward a circle at `distance`. Added clarifying comments on both methods; no logic change.
- **UEX-47** `ComponentX.cs` — `GetInterfacesInChildren` now returns `Enumerable.Empty<T>()` instead of `null` (was NRE-in-`foreach`), matching `GetInterfaces`.
- **UEX-48** `SceneManagerX.cs` — `GetCurrentSceneNames/Paths` reimplemented as `GetCurrentScenes().Select(...).ToArray()`.
- **UEX-49** `ScreenX.cs` — the misplaced nested `PlayerLoopUtils` was a verbatim twin of the existing top-level `Components/Screen/PlayerLoopUtils.cs`, so removed the nested copy and repointed the one reference to the top-level type (promoting it would have been a CS0101 dup); also dropped the now-unused `using System.Text;`.
- ⚠️ **Not compile-verified in-editor** (community MCP down). Done by 3 parallel agents + manual review; every diff reviewed here (verified no duplicate gizmo methods and that all revived-toggle symbols resolve).

### UEX-61/63/65/66/67 + GEO dead-code/dup sweep (round 5)
- **UEX-61** — swept the commented-out dead blocks: `RayX` (two dead method bodies), `OnGUIX` (old rotate/DrawTexture `DrawLine` impl), `GizmosX` (dead `mesh` field/destroy lines), `TextureX` (`GPUScale` orphan block), `ScreenX` (dead `gameViewDpiMultiplierDirty` DPI block + a commented PlayerLoop line + a dead early-out). `ReflectionX` had no distinct block (only single-line fragments interleaved with live logic) — left as-is. **RectTransformX "OLD STUFF (built for 80 Days)"**: the whole block was live, correct RectTransform helpers (anchor/pivot/size/edge-position extension methods, no dups of the curated section) — promoted them (removed the untrusted-caveat divider → a normal section header) and deleted the one genuinely-dead commented `SetRectInWorldSpace` stub.
- **UEX-63** — renamed the misspelled `frustrum`→`frustum` across `CameraX` (4 public method families × 2 overloads) + doc prose + the `Cmera`→`Camera` param typo; updated all repo callers (`CameraShotGeneratorTools`, `SerializableCamera` wrappers + calls, `CameraShotTools` comment, `WorldSpaceUIElement` local). Clean rename, no `[Obsolete]` forwarders; `grep frustrum Assets/` now empty.
- **UEX-65** — removed unused `using System.Collections;` from `SpriteX.cs`.
- **UEX-66** — `DebugX` error-level methods (`LogError`×2, `LogErrorMany`) no longer gated by the `debug` flag — errors always emit (so `Assert` surfaces too); `Log`/`LogWarning`/`LogMany` stay gated.
- **UEX-67** — `TextureX` color-texture size mismatch now `Debug.LogWarning` instead of `MonoBehaviour.print`.
- **GEO-20** — kept `PointToLineSegmentSquaredDistance` standalone (it's an allocation-free squared distance used per-edge in a hot loop, not dead code); added a comment explaining why it doesn't defer to `Line.GetClosestPointOnLine`.
- **GEO-21** — deleted the ~250 lines of commented grid-traversal graveyard in `Line.cs` (voxel `PointsOnLine(float)`, Lua `getHelpers` pseudocode, two `Traverse` transliterations, `DrawLineNoDiagonalSteps`); the only coherent one was functionally covered by live `PointsOnLine(int)`/`Plot`. Live `GetCrossedCells`/`PointsOnLine(int)`/`Plot` preserved.
- **GEO-22** — deleted `Polygon/Editor/LineEditor.cs` + `.meta` (entirely commented, superseded by the live `PolygonEditorTool`/`PolygonEditorInstance` which handles open polylines via `closed=false`; only commented references remained).
- **GEO-23** — deleted the lowercase `intersectsWithPolygon`/`whollyContainsOtherPolygon` (byte-identical dups) and repointed the 4 `CombinePolygons` callers to the canonical `WhollyContainsOtherPolygon`.
- **GEO-26** — deleted the orphaned commented `HullCull`/`GetMinMaxBox`/`GetMinMaxCorners` (referenced a non-existent `Point` type; also removed the dangling `//points = HullCull(points)` call in the live `MakeConvexHullPoints`); revived one working `GetSimplifiedVerts` (exact collinear-vertex removal, distinct from the live RDP simplifier) after verifying its `Line`/`SqrDistance` symbols exist; dropped the second overload's unfinished `minDot` variant.
- **GEO-27** — deleted the two commented non-compiling XNA-port `Intersects(Ray)` bodies and added one clean Unity-idiom `Sphere.Intersects(Ray)`.
- **GEO-28** — value-based `GetHashCode` (already satisfied by an earlier commit; confirmed consistent with `Equals`/`SequenceEqual`).
- **GEO tidying** — GEO-37 (`(x1, y10`→`(x1, y1)`), GEO-38 (`sinze`/`calcuate teh`), GEO-39 (removed a `Debug.Log`+its guard-`if` inside `Scale`), GEO-41 (edge-normal off-by-one `%(len-1)`→`%len`), GEO-42 (`GetRandomPointInPolygon`'s shared `static List<int> tris` → per-call local, thread/re-entrancy safe).
- ⚠️ **Not compile-verified in-editor** (community MCP down). Done by 5 parallel agents + manual review/salvage; every diff reviewed here (brace balance checked on the geometry files; revived `GetSimplifiedVerts`/`Intersects(Ray)` symbols verified present). GEO-43 left open by request. `LineEditor.cs`+`.meta` deleted.

### System extension sweep (round 6) — all SYS except Flags/Enum items
Context: project is .NET Standard 2.1, but UnityX must also build on .NET Framework — so BCL-duplicate items kept the custom impl (fixed) rather than deleting for netstandard2.1-only APIs.
- **SYS-1** `PathX.ReplaceIllegalCharacters` — now splits on BOTH separators (`/` and `\`) and sanitises each filename segment independently (via a `SanitiseFileNameSegment` helper), preserving separators; fixes the macOS/Linux bug where every `/` became `_`. Dedup now collapses runs of the replacement char, not just one pair. Null-guarded.
- **SYS-4** `ByteFormatter.ToSizeAuto` — returns `double` (was `long`), so 1.5 KB no longer truncates to "1 KB"; matches `ToSize`.
- **SYS-5** `StringX.IsWhiteSpace` — returns false for null/empty and uses `char.IsWhiteSpace` (covers `\r`, vtab, form-feed, Unicode).
- **SYS-6** `StringX.Truncate` — null source → null; negative length clamped to 0.
- **SYS-7** `StringX.AfterFirst`/`After` — a match at the end now yields `""` (removed the guard that returned the whole original).
- **SYS-8** `DirectoryX.GetRelativePath` — reimplemented without `Uri` (absolute-path segment comparison + `..` stepping), fixing the `UriFormatException` on relative input and the `#`-as-fragment mis-parse.
- **SYS-9** `StringX.Contains(string, StringComparison)` — kept as a portability shim (.NET Framework lacks the BCL overload); commented. (`IsWhiteSpace` overlap resolved by the SYS-5 fix.)
- **SYS-12** `PathX.GetFullPathWithoutExtension` — body now `Path.ChangeExtension(path, null)`; public API kept.
- **SYS-13** `DirectoryX.GetRelativePath` — kept custom (not `Path.GetRelativePath`, netstandard2.1-only); commented.
- **SYS-14** `BoolX.ToBool`/`ToInt` — verified correct; kept as convenience wrappers over `System.Convert` (commented).
- **SYS-15** `SystemX.OpenInFileBrowser` — comment clarified (runtime shell reveal vs editor-only `EditorUtility.RevealInFinder`).
- **SYS-19** `StringX` `Before`/`BeforeLast`/`AfterFirst`/`After` — comparisons standardised to `StringComparison.Ordinal`.
- **SYS-20** `SystemX` mac/win file-browser methods — refactored onto a shared `RunFileBrowserProcess` helper; both public wrappers + all platform differences (separators, `open` vs `explorer.exe`, arg quoting) preserved.
- **SYS-27** implemented `ByteFormatter.FromToSize(double from, SI, SI)` (order→order conversion via `1024^(from-target)`); removed the old stub.
- **SYS-30** `SystemX` — dropped the `e.HelpLink = ""` unused-var hack; the shared helper's `catch (Win32Exception)` no longer binds the variable.
- **SYS-31** `StringX.LowercaseFirstCharacter` — now a `this` extension, matching `UppercaseFirstCharacter`.
- **SYS-29** — normalised StringX indentation/blank-lines around the edits (FlagsX portion left open).
- ⚠️ **Not compile-verified in-editor** (community MCP down). Done by 3 parallel agents + manual review; every diff reviewed here (brace balance checked on all six files). Flags/Enum items (SYS-2/3/10/11/16/17/18/28/32 + FlagsX half of SYS-29) left open by request.

### Structures / Island tidying (round 7)
- **STR-16** `Island/OwnedIsland.cs` — deleted the ~88-line commented-out inner `OutlineSolver` (an abandoned island-outline tracer that never compiled — generic `<Coord>` shadowing, untyped `new OutlineSolver(this)`, `Coord`/`Point` confusion); it's superseded by the live, generic `OutlineDetector.GetOutlinePoly<Coord>`. Left a one-line breadcrumb comment.
- **STR-17** `Island/OutlineDetector.cs` — removed the commented-out dead lines (old `hexPoints` cast, `startCoord`/`startRotIndex`, the `if(outlineDistance…)` scaffolding around the live `outline.Add`, the old `yield` loop, `pointsToSearch`) and dropped a stray `;` empty-statement after the point-collection `foreach`'s closing brace. No live logic changed.
- **STR-18** `Structures/Shape.cs` — normalised the mixed tab/space indentation in `CreateContiguous` to tabs; renamed the confusing `TypeMap<bool> shape` local (collided with the returned `Shape` type) to `shapeMap` and updated all references.
- **STR-19** — removed unused usings: `System.Collections`+`System.Linq` from `Island/Island.cs`; `System.Collections`+`System.Linq`+`UnityX.Geometry` from `Island/OwnedIslandDetector.cs`.
- ⚠️ **Not compile-verified in-editor** (community MCP down). Done by 1 agent + manual (STR-16); every diff reviewed here (brace balance checked on all five files).

### Remaining STR + SPR editor (round 8)
- **STR-8** `Structure.cs` — `Contains(Func)` now delegates to `points.Any(checker)` (public API kept; `using System.Linq` added).
- **STR-11** `IslandDetector`/`OwnedIslandDetector` — `islands`/`testedPoints` changed from `protected static`/`static new` to instance fields, so two detectors (or a re-entrant call) no longer clobber shared state. No external static access existed; behaviour unchanged for normal single-use.
- **STR-12** `OutlineDetector.GetOutlinePoly` — removed the redundant per-`testCoord` `found = true;` (dead for `numCorners>0` since it's re-set per corner; the only case it mattered — `numCorners==0` — divides by zero downstream anyway). Verified behaviour-preserving; added a clarifying comment on the load-bearing per-corner init.
- **SPR-5** `Editor/SpringPropertyDrawer.cs` — `OnGUI` now delegates to `Draw(…, 1, 0, 0, null)` (bodies were identical bar the `BeginProperty`/`EndProperty` wrapper); deleted the unused `DrawYAxisLabel`/`DrawXScaleLabel`/`DrawYMinMaxScaleLabels` (grep-confirmed no callers; the sibling `NoiseSampler` drawer's own copy left untouched per the self-contained-drawer rule).
- **SPR-6** `Editor/SpringContextMenuPresets.cs` — removed unused `using System.Collections;`.
- **SPR-13** `Editor/SpringPropertyDrawer.cs` — deleted the dead commented fragments (old Damp-Ratio PropertyField, disabled tick-lines); reindented the nested `GraphGUI` to tabs. **Kept** the commented `_settlingDurationMarkerIcon` accessor — it's a plausible unfinished "mark the settling point on the graph" feature (mirrors the live `_currentTimeMarkerIcon`), flagged for a future decision rather than deleted.
- **SPR-14** `Editor/SpringHandlerPropertyDrawer.cs` — unwrapped six `new GUIContent(new GUIContent("…"))` double-wraps.
- ⚠️ **Not compile-verified in-editor** (community MCP down). STR by manual edits, SPR by 1 agent; every diff reviewed (brace balance checked on all touched files). SPR-4 assessed as intentional (kept — see active list).

### Easer + Tween + Range (round 9)
- **EAS-1** `Vector2/Vector3MoveTowardsEaser` — `MoveTowards` now scales by `deltaTime` (`maxDelta * deltaTime`), matching the sibling `QuaternionMoveTowardsEaser`; was frame-rate dependent.
- **EAS-4** `BaseEaser` — `target`/`current` setters use `EqualityComparer<T>.Default.Equals` instead of `_field.Equals(value)` (null-safe for reference `T`).
- **EAS-5/6** `FloatSmoothDamper`/`FloatMoveTowardsEaser` — rewritten to derive from `SmoothDamper<float>`/`MoveTowardsEaser<float>` like every sibling (~136/108 lines → ~18 each), mirroring `Vector2*` exactly. `FloatMoveTowardsEaser`'s correct `maxDelta * deltaTime` step preserved. No external usages; no external `.delta =` writes (the `delta` setter tightening to `protected` breaks nothing). Field names unchanged so serialized data migrates.
- **EAS-9** — assessed as inherent (see active list); EAS-5/6 reduced it to the unavoidable 1-line `GetDelta` override per type.
- **EAS-10** — removed unused `using UnityEngine.UI;` from Quaternion/Vector2/Vector3 `SmoothDamper`.
- **EAS-11** `Vector3SmoothDamper` — ctor param `target` → `current` (matched its role and the `Vector2` sibling).
- **EAS-15** `FloatSmoothStepDamper` — removed commented `// this.initial =`; `var targetVelocity = 0` → `0f`.
- **EAS-16** `SmoothDamper` — doc-comment/attribute ordering fixed; `[DisableAttribute]` → `[Disable]`.
- **TWN-1** `TypeTween.Update` — advances the timer BEFORE sampling (was one frame stale); guards against re-sampling/overwriting on the completing frame. Completion + OnStart semantics preserved.
- **TWN-3** `QuaternionTween.SetDeltaValue` — `current * last` → `current * Quaternion.Inverse(last)` (world-space delta; `deltaValue` is unused in-repo, order chosen to match the sibling additive convention).
- **TWN-4** `TypeTween.GetValueAtTime` — guards null timer / `targetTime <= 0` (returns the end value) instead of dividing by zero.
- **TWN-5** — documented the instant (zero-duration) tween firing OnStart+OnComplete as intentional (no logic change).
- **TWN-6** `RectTween` — `Vector4.Lerp` → `Vector4.LerpUnclamped` for consistency with `FloatTween`. (Vector2/Vector3Tween remain clamped — flagged, out of this scope.)
- **TWN-9** `TypeTween` — `AnimationCurve.Linear(0,0,1,1)` cached in a `static readonly` (was allocated per default tween). Verified read-only usage; caveat noted if external code mutates a default curve's keys.
- **TWN-10** `TweenProperties` — ctors now set `setStartValue`/`setEasingCurve` so start-value/easing-curve ctors are honoured (were silently ignored). The single-value `(startValue, tweenTime)` ctor is inherently under-specified (no target → tweens to `default(T)`); documented in a comment and flagged for review rather than guessing a different intent.
- **TWN-13/14** — deleted commented-out blocks in `FloatTweenDrawer`/`TimerDrawer`.
- **TWN-15** — added the missing space in `if(OnComplete != null) OnComplete();` across all 6 subclasses (kept the null-check form, not `?.Invoke()`, due to the `new event` shadowing); normalised `TypeTween` indentation.
- **RNG-10** `Range.Intersection` (instance) — delegates to the static overload.
- **RNG-11** `Blender` — removed the dead local `T previous`.
- **RNG-13** `Selector` — extracted `NotifyIfChanged(Action)` for the 3× capture/recompute/fire-onChange pattern; preserves the existing `!previous.Equals(current)` semantics exactly.
- ⚠️ **Not compile-verified in-editor** (community MCP down). Done by 4 parallel agents + manual (EAS-5/6 refactor, TWN-10 caveat); every diff reviewed here (brace balance checked on all 23 touched files). Left open by scope: EAS-2/3/7/8, TWN-7/8, RNG-1..9/12/18..20.

### Cross-cutting (XC) + Algorithms/Camera/Spline (ACS) + doc reorg (round 10)
- **XC-3** `SetPropertyDrawer` — already resolved in an earlier commit (`GetProperty` binding includes `NonPublic`, so private/protected setters fire; `PropertyInfo` cached by declaring type + `attribute.Name`). Verified, no change needed.
- **XC-5** — null-guarded the `GetComponentInParent<Canvas>().rootCanvas` cluster: `RectTransformX.GetRootCanvas` returns null instead of NRE (also applied by a concurrent linter pass — reconciled), `GetCanvasEventCamera` and the private `GetCanvasRenderCamera` guard the now-nullable canvas, and `CanvasX.GetRenderCamera` guards a null `canvas` arg. No shared cross-file helper (portability).
- **XC-7 (+GEO-29)** — removed the impossible `(object)struct == null` / `ReferenceEquals` checks from `Line`, `Line3D`, `PointRect` equality members (rewrote `Equals(object)` as `obj is T other && …`, dropped the always-false null branches in `operator ==`); value-equality preserved (incl. `Line3D`'s order-independent compare). `Point3` was already clean.
- **XC-9** — the "frustrum" method rename was done in round 5 (UEX-63); fixed remaining doc typos ("simultaniously" → "simultaneously" in `TypeMap`/`TypeMap3D`). The "Decendent" class/folder rename stays deferred (CMP-38 — serialized refs).
- **ACS-25** `CameraShotGeneratorTools` — the per-call `Debug.LogWarning` now fires only for the genuinely-unhandled scaling mode (was spamming every call). **ACS-26** `CameraModifierZone` — empty `if(!isPlaying){}else{…}` inverted to `if(isPlaying){…}`. **ACS-28** `CameraPropertiesTween` — now lerps `orthographicSize` (`Mathf.Lerp`), `axis` (`Quaternion.Slerp`), and `orthographic` (midpoint snap), matching the canonical `CameraProperties.LerpUnclamped`. **ACS-29** `UpscaleTools` — removed the unused local `i`. **ACS-30** — deleted the commented `BinarySearch`/index lines in `SplineBezierCurve`/`Spline`. **ACS-31** `CameraPropertiesBuilderQueue` — the 2-arg `Add` now re-sorts like the other overloads; `Update` null-checks the delegate (`?.Invoke`).
- **Doc reorg** — added the `## 🅿️ Left as is` section and moved every "verified-not-a-bug / intentional / won't-do / deferred" note there (UI-31/32/35/36/37, CMP-5/6/8/11/26/30/31/38, ED-5/6/7, PD-10/12/14/15/16/18/22/23/25, UEX-7/26/30, GRID-24, ACS-12/27 + vendored, SYS-26, SPR-4, EAS-9, TWN-2, MISC-3/6, GEO-20), so the per-area sections now show only outstanding, actionable findings. MISC-3 (`FlipVertical`) reclassified as verified-working per the author.
- ⚠️ **Not compile-verified in-editor** (community MCP down). Done by 3 parallel agents + manual (XC-5 `GetCanvasEventCamera` guard, doc reorg); every diff reviewed (brace balance checked on all 15 touched files).

### ACS-12/13/14/17/18 + ED-14 + CMP-16/17 + UEX-21/31/37 (round 11)
- **ACS-12** `EasingFunction` — converted both `GetEasingFunction`/`GetEasingFunctionDerivative` if-chains to `switch` (dispatch only; every enum case maps to the same body, `default` = the old `return null`). Behaviour-identical. *(Caveat: this file is vendored-ish, so a future upstream sync will need re-merging.)*
- **ACS-13** `SplineBezierControlPoint` — `GetAutoDistanceOut` delegates to `GetAutoDistanceIn` (identical by magnitude symmetry).
- **ACS-14** `BoundingSphere` — deleted the ~815-line commented XNA/SharpDX block (uncompilable here; live Welzl class covers all real usage). File truncated to 246 lines.
- **ACS-17** `SplineEditor` — deleted the commented block referencing the obsolete `RiverBezierPoint` (grep-confirmed the type is gone).
- **ACS-18** `SimplexNoiseGenerator` — extracted the byte-identical pre-loop setup of `Generate`/`GenerateRepeating` into a private `GetSetup(...)`; the differing per-sample bodies (incl. `GenerateRepeating`'s wrap) stay inline.
- **ACS-19** — no change: the `GetHashCode` was already a correct rolling hash consistent with `Equals` (stale finding).
- **ED-14** `CreateCustomTextureWindow` — deleted the trailing commented `EditorGrid` class (~90 lines); no external refs.
- **CMP-16** `Region` — inlined the one-call-site `SqrDistance` helper to `(a-b).sqrMagnitude`. **CMP-17** — `Vector3.Normalize(x)` → `x.normalized`.
- **UEX-21** `SelectionX` — fixed the `CompareWithLastSelection` copy-paste bug: the object loops now compare `.objects` against `.objects` (were comparing against `.gameObjects`, mis-firing `OnSelect/DeselectObject` for non-GameObject `Object`s). Did NOT touch the `if(Length==0)` save guard — verified it's actually fine (the line-101 mutation maintains selection order/`instanceIDs` for the non-empty case), so the "inverted logic" half of the finding is a false positive.
- **UEX-31** `UIBehaviourX` — kept `GetRectTransform`/`GetParentCanvas` as public convenience wrappers (added comments). **UEX-37** `MeshRendererX.SharedMaterialsContains` — body now `Array.IndexOf(...) != -1` (public API kept).
- **UEX-34** — assessed as NOT a duplicate (inverse conversions) — no change; see `## 🅿️ Left as is`.
- ⚠️ **Not compile-verified in-editor** (community MCP down). Done by 2 parallel agents + manual (ACS-14 truncation, UEX-21); every diff reviewed (brace balance checked on all 10 touched files).

### PD-27/28 + review dispositions (round 12)
- **PD-27** — normalised the mixed tabs/spaces (→ tabs) in `PopupDrawer.cs` and `PropertyPopupDrawer.cs` (whitespace-only; commented blocks untouched).
- **PD-28** — added `[AttributeUsage(AttributeTargets.Field)]` to the 39 property-drawer attribute classes that lacked it (matching `EnumFlagAttribute`/`EnumButtonsAttribute`), plus `using System;` where missing. All in-repo usages are on fields (grep-verified); `DisableIf`/`VisibleIf` inherit it from `BaseIfAttribute`. *(Caveat: this restricts each attribute to fields — a theoretical breaking change only for external code that applied one to a non-field, which wouldn't have worked anyway since these are field-drawn `PropertyAttribute`s. Fixed two accidental duplicate `using System;` the pass introduced.)*
- **CMP-16/17, ED-14** — were already fixed in round 11; struck from the active list here (doc catch-up).
- **UEX-33** — explained (`AnimationCurveX.EaseInOut` builds the same zero-tangent 2-key curve as `AnimationCurve.EaseInOut`, differing only in param order + extra convenience overloads); left as-is pending a decision.
- **UEX-41** — confirmed already resolved (RayX blocks removed in round 5 / UEX-61).
- **UEX-46** — won't-do (portability); **UEX-62** kept (legitimate diagnostics); **GEO-19** left (already consolidated per-file) — all → `## 🅿️ Left as is`.
- **Reviewed, awaiting go-ahead** (no code touched): **PD-29** (dead scratch safe to delete; the `EnumFlagsButtonGroupDrawer` 77-87 stub is superseded but hints at a *real* missing feature — filtering 0/composite flags from the static `Draw` overloads, per the live TODO), **UEX-39** (genuine byte-identical `CanvasGroups*` copy, no external callers — dedup vs keep-for-portability), **UEX-40** (safe `BoundsX` refactor), **UEX-42** (delete the unused `(object,string,Type)` overload + inline scraps; keep the two load-bearing overloads + `SetValueFromObject`).
- ⚠️ **Not compile-verified in-editor** (community MCP down). PD-27/28 by 1 agent + manual dup-using fix; review by 1 read-only agent; every diff reviewed (whitespace-only confirmed for PD-27, brace balance checked, BOM integrity verified).

### UEX-40/42 refactors + UEX-64 typos/renames (round 13)
- **UEX-40** `BoundsX` — extracted `Encapsulate(ref min, ref max, v)` shared by the three `CreateEncapsulating` overloads (the `params` one now delegates to the `IList` overload); `ClosestPointOnPerimeter`'s six repeated face blocks collapsed into a `Consider(face)` local over the six faces. All public overloads kept; behaviour-identical.
- **UEX-42** `ReflectionX` — extracted a private `WalkPath(obj, path, earlyOut, out aborted)` shared by all three `GetValueFromObject` overloads; each keeps its exact type-filtering (generic `is T`→default, `Type t` exact→null, `object` unfiltered) and its public signature — **no public functionality removed** (per request), including the unused `(object,string,Type)` overload and `SetValueFromObject`. Removed the inline commented-out dead scraps.
- **UEX-64** — renamed the misspelled PUBLIC `TransformX` methods `GetAllDescendents`→`GetAllDescendants`, `IsDescendentOf`→`IsDescendantOf`, `GetHeirarchyIndex`→`GetHierarchyIndex` and updated all callers (`VirtualKeyboardManager` in UnityX; **`GroupVectorFieldComponent` under `Assets/Vector Fields/`** — a required cross-project caller update, staged with this commit). Fixed `ImageX` error strings to name their actual methods (`GetTightLocalCorners`/`GetTightWorldCorners`). Fixed the `matricies`→`matrices` typo everywhere it appears — `GizmosX` (private field), `OnGUIX` (public field, no external callers), `HandlesX` (private field, beyond the finding's list), and a `Spline.cs` comment — plus `reassinging`→`reassigning`. Renamed the misnamed `ScreenXEditorWindow` MenuItem handler `OpenSpriteEditorWindow`→`OpenScreenXEditorWindow`.
- ⚠️ **Not compile-verified in-editor** (community MCP down). Done by 2 parallel agents + manual (matricies field renames); every diff reviewed (brace balance checked; old names + `matricies` grep-confirmed gone repo-wide). *Caveat: `OnGUIX.matricies` is public — renaming it is a breaking change for any external caller, but there are none in-repo.*

### PD-29 + doc restructure (round 14)
- **PD-29** — deleted the confirmed-dead commented blocks: `SetPropertyDrawer`'s `//setProperty.IsDirty` line (superseded by `attribute.IsDirty`), `PropertyPopupDrawer`'s two commented reflection blocks (the `GetParentObjectOfProperty`/`fi.GetValue` approach, superseded by the live `SerializedPropertyX.FindPropertyRelative` path), and `EnumFlagsButtonGroupDrawer`'s commented `BeginProperty`/`EndProperty` lines + the non-functional `bitCount`/`continue` stub. The stub pointed at a **real gap** (the static `Draw` overloads still render `0`/composite enum values as buttons) — captured as a live `// TODO` merged with the existing one rather than a revived stub.
- **UEX-33** — explained (again): `AnimationCurveX.EaseInOut` produces the same zero-tangent 2-key curve as `AnimationCurve.EaseInOut`; differs only in param order + the no-arg/`(width,height)` convenience overloads. Left active pending a decision.
- **GEO-24** — assessed as a valid PERFORMANCE duplicate (concrete `Vector2[]`/`List<Vector2>` overloads avoid `IList<T>` interface-dispatch in the hot point-in-polygon loop) → `## 🅿️ Left as is`.
- **GEO-25** — explained: `GetRegularEdgePosition` and `GetPositionAtNormalizedArcLength` are functionally identical (both position-at-normalized-arc-length); `GetPositionAtArcLength` is the distinct base. Refined the active finding; the "Regular" name/commented scraps suggest an unfinished by-edge-index intent.
- **PD-27/28** — confirmed done in round 12; struck from the active list.
- **Doc restructure** — removed all 34 "resolved / see Left-as-is" pointer breadcrumbs from the top half, relocated the 6 full inline "Left as-is" findings (GRID-30, TXT-16/17/18/20/24 + GEO-24) into the `## 🅿️ Left as is` section (new "Duplication kept" subsection), and collapsed the resulting empty subsection headers. The top half now shows only outstanding, actionable findings.
- ⚠️ **Not compile-verified in-editor** (community MCP down). PD-29 by 1 agent (comment-only deletions, brace balance even); doc restructure by reviewed script + manual, full diff checked.

### Range class (RNG-1/2/3/8/18/19) (round 15)
- **RNG-1** `Range.ShrunkToExclude` — the guard `value > min || value < max` was always true (dead rejection); changed to `&&` so it only shrinks when the value is strictly *inside* the range.
- **RNG-2** `Range.ExpandedFromPivot` — the max endpoint was computed from `min`; fixed to `max + expansion*(1-pivot)` (total expansion now correctly splits across the pivot).
- **RNG-3** `Range.RemoveRange` — added a defensive clamp of `rangeToRemove` to `[min,max]` so the emitted sub-ranges can never invert (behaviour unchanged for already-contained input; addresses the "fragile" flag).
- **RNG-4** — no change: `GetHashCode` was already a value-based rolling hash consistent with `Equals` (the multiply-to-zero concern was already resolved). Stale finding.
- **RNG-8** — `Auto` simplified to `Mathf.Min`/`Max`. `CreateEncapsulating` left as its single-pass loop (deliberately better than 2-pass LINQ `Min()`/`Max()`, so not "deduped").
- **RNG-18** — deleted the commented-out `RangeTests` MonoBehaviour block.
- **RNG-19** — fixed the `trunctationValue` → `truncationValue` typo (`ShrunkToExclude` param + uses; no external/named-arg callers).
- ⚠️ **Not compile-verified in-editor** (community MCP down). Manual edits; brace balance even (80/80), typo grep-confirmed gone from live code (only the fully-commented `RangeInt.cs`/RNG-9 still contains it). RNG-9 (RangeInt.cs entirely commented) left as a separate delete-vs-revive decision.

### ValuePicker + STR + EAS + TWN + ACS + RangeInt + XC (round 16)
- **RNG-5** — `Blender`/`LogicBlender`/`Selector` change-detection now uses `EqualityComparer<T>.Default.Equals` (null-safe). **RNG-6** — `LogicBlender` source lookups unified to `EqualityComparer<object>.Default.Equals` (`Set`/`Remove`/`TryGetValueForSource` were mixing `.Equals`/`==`). **RNG-7** — `Selector`'s `desiredValue == null` kept (correct for reference `T`; a value type is never null so `nullRemovesValue` legitimately can't apply) + commented. **RNG-20** — deleted the commented assert + "creates garbage" alt-impl.
- **RNG-12** — assessed (Blender/Selector mergeable but moderate-value/risky; LogicBlender separate) → `## 🅿️ Left as is`.
- **STR-5** — `Shape.OnChangePoints` `pointBounds` now floors the min / ceils the max and clamps extents to ≥1, fixing single-point (zero-size) and negative-origin (truncate-toward-zero) bounds. **STR-6** — `CreateContiguous` got a `numPoints<1` guard, a clamped seed (works for `numPoints==1`), and an attempt cap on the inner `do/while` (bails gracefully instead of hanging); normal-case shape output unchanged.
- **EAS-2/EAS-7** — `DampedSpring`/`CriticallyDampedSpring` now sub-step the caller's real `deltaTime` in fixed ≤1/60 chunks (capped at 1s) instead of hard-coding `1/60`: stable *and* framerate-independent, so the `deltaTime` params/`Time.deltaTime` overloads are meaningful. ⚠️ Springs now run at correct real-time speed at all framerates (previously slow below 60fps) — affects Boat Game's `ThumbstickUI`. **EAS-8** — `AddImpulse` now has the same NaN/Inf assert as `AddForce` (param renamed `impulse`, doc clarifies instant-Δvelocity vs over-time force). (EAS-3 improved by the smaller steps; analytic solver would be exact — left low-priority.)
- **TWN-8** — removed `FloatTween`'s extra `new OnStart` shadow + `TweenStart` override (nothing subscribed; now consistent with the other 5 subclasses). **TWN-7** — investigated, likely stale, kept pending iOS verification (→ Left as is).
- **ACS-15** — the commented `UpscaleTools.Test` demo fixed to compile against the live API (`instance`→`Instance` on `MonoSingleton`), left commented per request. **ACS-16** — removed the dead uncalled private `SubdivideInCurve` (superseded by the live arc-length estimator), the two dead `var r` locals, and the commented `RoughEstimateBestCurveT`/`roughLength`/`GetCurveStartingWith` blocks.
- **RNG-9** — revived `RangeInt` (a min/max int span with real methods, distinct from Unity's `start+length` `RangeInt`); fixed the same bugs the live `Range` had (the `||`→`&&` guard, multiply-collapse `GetHashCode`, dead struct null-checks, `RemoveRange` clamp). ⚠️ Its global name shadows `UnityEngine.RangeInt` — a footgun (no in-repo callers of either today); recommend renaming (`IntRange`) or namespacing.
- **XC-6** — resolved via sub-findings (→ Left as is). **XC-8** — deleted 3 dead-scratch lines in `TextEffectsController`/`TextBouncer`; flagged+kept the coherent `TextEffectsController` disabled-feature cluster (→ Left as is).
- ⚠️ **Not compile-verified in-editor** (community MCP down). Done by 6 parallel agents + manual (SpringDamper); every diff reviewed (brace balance checked on all 11 touched files). `Spline.cs` also carries a concurrent `namespace SplineSystem` wrap (consistent with its already-namespaced siblings). RNG-7's `==null` kept by design; RangeInt name-collision + TWN-7 iOS + the TextEffects cluster flagged for decisions.

### Spring analytic delegation + SPR fixes + HierarchyX (round 17)
- **EAS-2/7/EAS-3 (re-done)** — per feedback, `SpringDamper` now **delegates to the analytic `Spring.Update`** (closed-form damped-spring solver) instead of the round-16 explicit-Euler sub-stepping. This is deterministic + framerate-independent by construction (exact solution at any `deltaTime`), needs no fixed-step hack or sub-stepping, unifies the two spring systems, and removes the explicit-Euler overshoot (resolves EAS-3). Unit mass (mass=1); `CriticallyDampedSpring` maps to `damping = 2·√stiffness`. ⚠️ Spring feel changes (now the exact analytic response) — affects Boat Game's `ThumbstickUI`; worth an in-editor check of the velocity/sign convention round-trip since `Spring.Update` is a SmoothDamp-style step.
- **SPR-1** `Spring.SettlingDuration` — added an `if (dampingRatio <= 0) return Mathf.Infinity;` guard (undamped springs never settle), removing the `/-omegaZeta` div-by-zero that could yield `+Infinity` or NaN (corner case where the log numerator is also 0).
- **SPR-11** — typos fixed: "oscellate/oscellation" → "oscillate/oscillation" (all sites), "Contructors" → "Constructors", "my be specified" → "may be specified".
- **SPR-12** — deleted the commented-out alternative velocity formula (the live line 181 is the canonical, self-contained analytic derivative and is what's used/tested; the commented variant reused `displacement` and my expansion showed a sign discrepancy making it a dubious "reference") and fixed the un-indented comment in the overdamped branch. *(Answer: the live formula is better.)*
- **HierarchyX** (compile fix) — `SetExpandedRecursive` (reflected internal Unity API) still takes an `int` id; per Unity's EntityId migration docs there's no lossless `EntityId`→`int` conversion, so reverted to `GetInstanceID()` with `#pragma warning disable 618` (documented stopgap for legacy int APIs) — resolves the CS0619 from the earlier `(int)GetEntityId()` cast.
- **SPR-2** left unfixed (narrow numerical edge in a heuristic loop; not confident of a safe fix without in-editor testing). **TWN-7** left in place — online validation was *inconclusive* (IL2CPP generic/AOT issues, esp. value-type generics which these tweens use, remain an active concern in 2026; the specific 2013 event-crash isn't confirmed fixed and only manifests in an iOS AOT device build), so I'm not confident enough to remove it per your "if you're sure" gate.
- ⚠️ **Not compile-verified in-editor** (community MCP down). Manual edits; brace balance checked (SpringDamper 17/17, Spring 72/72, HierarchyX 6/6). RangeInt naming decision still open (recommend `IntRange`).

### HierarchyX reflected instance-id + RangeInt namespacing (round 18)
- **HierarchyX** — switched from `GetInstanceID()` + `#pragma` to fetching the instance id **via reflection** (`typeof(Object).GetMethod("GetInstanceID")`). `SetExpandedRecursive` still keys the tree by the int instance id (confirmed in current UnityCsReference), and Unity has no lossless `EntityId`→`int` (a hash code isn't unique, so it wouldn't match) — but reflecting the accessor removes the compile-time dependency on the soon-removed API: the tool keeps compiling regardless and simply no-ops (guarded `if (getInstanceIDMethod == null) return;`) if the accessor is ever removed. Rest of the project already uses the modern patterns (`EntityId.ToULong` for keys, `GetEntityId().GetHashCode()` for hashing) — HierarchyX was the only int-requiring holdout.
- **RangeInt** — namespaced under `UnityX` (an existing project namespace) rather than renamed, so it keeps the natural `RangeInt` name alongside `UnityEngine.RangeInt` (qualify as `UnityX.RangeInt` if both are in scope). Zero callers, so no churn.
