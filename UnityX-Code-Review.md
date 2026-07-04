# UnityX Code Review

Read-only review of every `.cs` file under `Assets/UnityX/Scripts/` (514 files, ~67.5k lines), fanned out across the whole tree. **Nothing was modified.** Findings are grouped per area, each into 5 categories: Bugs, Unity-native / .NET duplication, Refactoring / dead code, Misleading / incorrect comments, Tidying.

Paths are relative to `Assets/UnityX/`. Line numbers are approximate — treat as anchors, confirm before acting.


---


## Components / UI (`Scripts/Components/UI/`)

### Bugs
*(All UI bugs UI-1 … UI-28 fixed — see the `## ✅ Done` section.)*

### Unity-native duplication
*(**Portability principle** — this repo is being split into independently-portable sub-projects. Duplication of a **Unity/.NET built-in** is safe to collapse onto the built-in (the framework is always present). Duplication of another **UnityX** sub-project's helper (`Extensions/*`, etc.) is a **deliberate local copy** — collapsing it would create a cross-sub-project dependency, so it's left as-is. Scanned the whole doc for the latter: UI-32 is the only case where a "fix" would have added such a dependency; the rest of the duplication findings target framework built-ins or same-sub-project code.)*
UI-32. `UI Imposter/UIImposterRenderer.cs` — local `CreateEncapsulating(Vector3[])` mirrors `Extensions/BoundsX.CreateEncapsulating`. *(Left as-is / earlier consolidation reverted: the local copy is intentional so the UI sub-project stays independent of Extensions. The `else if` min/max chain is correct — the review's "latent bug" was a false positive.)*

### Refactoring / dead code
UI-31. `ExtendedScrollRect/ExtendedScrollRect.cs` — *removed: not a bug.* The reimplemented private `ScrollRect` methods are unavoidable (originals are private), and the local `CalculateOffset()`/`InternalCalculateOffset` pair is public API that may be used externally; nothing actionable.
UI-35. `Extended Button/ExtendedSelectable.cs` — near-verbatim copy of `ExtendedButton.cs`. *(Left as-is: genuinely useful — `ExtendedButton : Button` vs `ExtendedSelectable : Selectable` are distinct base types (a Selectable has no button click/navigation behaviour). C# single-inheritance means the pointer/select event boilerplate can't be shared without a common interface + helper; the duplication is the lesser evil.)*
UI-36. `Line/AdvancedUILineRenderer.cs` vs `UILineRenderer.cs` — duplicated miter/bevel join block + duplicated `MIN_MITER_JOIN`/`MIN_BEVEL_NICE_JOIN` constants. *(Won't-do per request: the two blocks are NOT identical — different loop/wrap logic (Advanced supports loop joins via `startIndex`/`endIndex`), different miter-distance source (`LineThickness` vs `Vector2.Distance` between edge verts), and (previously) different quad triangulation. Merging is a mesh-generation change with no in-editor/visual verification — not worth the risk.)*
UI-37. `Outlines/Outline8.cs:7-27` — a special case fully subsumed by `BoxOutline`. *(Explained/left as-is: `Outline8` draws the 8 neighbours of a 3×3 grid (centre excluded) at ±`effectDistance` — exactly `BoxOutline` with `halfSampleCountX/Y == 1`. No in-project references, but it's a serialized public `MonoBehaviour` that may be attached in external scenes, so removing it would break those. Kept as the n=1 convenience component.)*

*(UI-44/46/47/50/54 and all Tidying UI-68…78 resolved — see the `## ✅ Done` section.)*

---

## Components (non-UI) (`Scripts/Components/`)

### Bugs
*(CMP-1/2/3/4/7/9/10/12/13/14/15/41 fixed — see the `## ✅ Done` section.)*
CMP-11. `FPSManager/FPSManager.cs:92-102` — `RemoveOldDeltaTimes`. *(Verified NOT a bug: it accumulates newest-first and `RemoveRange(0, currTimeIndex)` keeps `[currTimeIndex..end]`, i.e. it deliberately retains the frame that tips the total past `fpsGraphHistoryTime` so the kept window fully covers the graph history (≥, not <). `RemoveRange(index, count)` semantics are correct. Left as-is.)*

*Verified — not bugs: CMP-5 (lower slab already rejected by the preceding `localBounds.Contains`; line 274 merely redundant); CMP-6 (`Vector2.normalized` returns zero for a zero vector, no NaN); CMP-8 (release→mutate→create is Unity's required RenderTexture order).*

### Unity-native duplication
CMP-16. `Region/Region.cs:269-271` — `SqrDistance` duplicates `(a-b).sqrMagnitude`.
CMP-17. `Region/Region.cs:431` — `Vector3.Normalize(...)` where `.normalized` is idiomatic.
CMP-18. `Input/InputUtils.cs:8-16` — `HoveringOverUI`. *(Low risk, left as-is. The `EventSystem`/`GUIUtility.hotControl`/iOS-touch paths are intentional. The one real gotcha: the `screenPos` param is ignored — the function always tests the **current** EventSystem pointer (the position-specific raycast fallback is commented out). Fine if callers pass the current pointer (they do); misleading if someone expects a hover test at an arbitrary point. Not worth a public-signature change to drop the param.)*

### Refactoring / dead code
CMP-20. `Events/TriggerListener.cs:81-154` — 12 collision/trigger handlers repeat the same block. *(Proposal: a generic `Dispatch<T>(T arg, int layer, UnityEvent<T> evt, Action<T> cb)` — `if(ignoreLayers.Includes(layer)) return; evt.Invoke(arg); cb?.Invoke(arg);` — turns each handler into a one-liner e.g. `void OnCollisionEnter(Collision c) => Dispatch(c, c.gameObject.layer, OnCollisionEnterEvent, CollisionEnter);`. Behaviour-identical, works because the event fields derive from `UnityEvent<T>`. Not yet applied — say the word.)*
CMP-21. `ChangeCheckers/TransformChangeChecker.cs:43-72` — 5 change checks share one structure. *(Proposal: a `CheckChanged(bool changed, TransformDelegate specific, string messageSuffix)` helper (or a comparison-delegate variant) collapsing the assign+fire-events+SendMessage block. **Caveat:** the checks use Unity's `!=` on `Vector3`/`Quaternion`, which is approximate — a generic `EqualityComparer<T>` would change that to exact equality, so the helper must keep the per-type `!=` comparison (pass a `bool changed` or a comparer). Not yet applied.)*
CMP-22. `ChangeCheckers/GameObjectChangeChecker.cs:22-41` — play/edit-mode guard boilerplate duplicated across `Update`/`OnDestroy`.
CMP-23. `Input/InputX.cs:338-374` — 4 near-identical linear-search-by-ID methods; `376-413` — 6 mouse handlers on one template.
CMP-24. `Input/InputPoints/KeyboardInput.cs:24-49` — cardinal/combined direction methods share most logic.
CMP-25. `PolygonRenderer/Editor/PolygonRendererEditor.cs` & `PolygonOutlineRendererEditor.cs` — essentially identical.
CMP-26. `PolygonRenderer/BasePolygonRenderer.cs:142-170` — `GetColor` is unused by the live path (`RecalculateColors`); it's `protected` in a public abstract class, so it may be intended for external subclasses.
CMP-27. `ViewAnimator/ViewAnimator.cs:95-117` — 3 cancel methods share a backward-loop body.
CMP-28. `Transform/Transform Copier/TransformCopier.cs:23-35` — `Update`/`FixedUpdate` repeat guard checks.
CMP-29. `CoroutineHelper.cs:28-82` — `Execute`/`Delay`/`DelayRealtime`/`DelayFrame` heavily templated; `Delay*` start+return a coroutine (confusing).
CMP-30. `EnforceDecendentGameObjectProperties/Editor/…:11,16` — `EnforceProperties()` runs on `OnEnable` and every `OnInspectorGUI`, re-walking the subtree and writing tag/layer/isStatic every repaint. Drive it from state changes instead: `OnValidate` on the component, the existing `OnTransformChildrenChanged`, and the save-time processor (drop the per-`OnInspectorGUI`/`OnEnable` calls).
CMP-31. `Region/Editor/RegionEditor.cs:117-141` — `CreatePolygonMesh` duplicates double-sided mesh construction in `Region.cs`.

### Tidying
CMP-37. Large commented-out blocks: `Input/TouchInputSimulator.cs:9-179`; `PolygonRenderer/PolygonOutlineRenderer.cs` (11-23, 89-160, 216-233); `PolygonRenderer/PolygonRenderer.cs` (55-84, 158-194); `PolygonRenderer/LineDraw.cs` (58-66, 200-219); `TextMeshPro/TextBackgroundHighlightEffect.cs:50-145`; `Input/Gestures/Pinch.cs:91-113`; `InputX.cs`; misc in `ScriptableSingleton.cs`, `CoroutineHelper.cs`, `Transform/LockTransform/Editor/…`, `EnforceDecendent…cs:39-46`, `FPSManager.cs:28`, `RenderTextureCreator.cs:32,67`.
CMP-38. Typo "Decendent" → "Descendent" throughout the `EnforceDecendentGameObjectProperties` folder (folder/file/class names).
CMP-39. `ViewAnimator/ViewAnimationEvent.cs:31` — warning "Event starts after ending!" has no context.
CMP-40. `Audio/AudioSource/AudioSourceManager.cs` — inconsistent spacing (`if(OnPause!= null)`); also verify `clip`-null short-circuit before `clip.samples` (~170).

---

## Editor Tools (`Scripts/Editor Tools/`)


### Bugs
ED-1. `SerializedEditorSettings/SerializedEditorSettings.cs:15` — EditorPrefs key is `string.Format("{0} Settings ({1})", typeof(T).Name, …)`; `typeof(T).Name` is the short name → same-name types in different namespaces collide.
ED-2. `CommentComponent/Editor/CommentComponentEditor.cs:54` — `Save()` writes `data.text` directly with no `Undo.RecordObject`/`EditorUtility.SetDirty` → edits aren't undoable and may not persist.
ED-3. `GameLayersClassGenerator/Editor/GameLayersClassGenerator.cs` — a layer like "2D Collider" becomes `2dCollider` via `ToCamelCase`, still an invalid identifier (leading digit) → generated file wouldn't compile. (Currently moot: the generator's `[InitializeOnLoad]` hook and ctor call are commented out.)

### Unity-native duplication
ED-5. `Screenshot Exporter/ScreenshotCapturer.cs` — the multi-camera render-to-RT + `ReadPixels` path overlaps `ScreenCapture.CaptureScreenshotAsTexture` (though it adds per-camera selection the built-in lacks).
ED-6. `EditorTime/Editor/EditorTime.cs` — layers over `Time.realtimeSinceStartup` + `EditorApplication.update` to push a `_EditorTime` shader global; overlaps Unity's editor time.
ED-7. `Screenshot Exporter/ScreenshotSaverTextureFormat.cs` — enum+switch maps to `TextureFormat`; the actual PNG/JPG encoding (`EncodeToPNG`/`JPG`, i.e. `ImageConversion`) lives in `ScreenshotExportSettings`, not here.

### Refactoring / dead code
ED-9. `CameraUtilities/Editor/CameraInfoWindow.cs` — ~7 near-identical `GUILayout.Label(...)` calls per row; also allocates `new GUIStyle(...)` every OnGUI.
ED-10. `Screenshot Exporter/ScreenshotSaverComponent.cs` vs `ScreenshotSaverWindow.cs` — overlapping capture-setup/export logic (the component delegates capture to `ScreenshotCapturer`).
ED-11. `Screenshot Exporter/Editor/ScreenshotSaverWindow.cs` — long `OnGUI` fanning to many `Draw*` helpers; calls `Repaint()` every frame.

### Tidying
ED-13. Unused using: `CommentComponent/CommentComponent.cs` — `using System.Collections;` is unused.
ED-14. Commented-out code: `Texture Creator/Editor/CreateCustomTextureWindow.cs` (large `EditorGrid` block near the end).

---

## Property Drawers (`Scripts/Property Drawers/`)


### Bugs
PD-1. `EnumButtonGroup/Editor/EnumFlagsButtonGroupDrawer.cs:38-45` — individual-flag writes (`|= mask` / `&= ~mask`) don't mask to defined bits → `Everything`/`-1` round-trips inconsistently.
PD-2. `Vector2Toggle/Editor/Vector2ToggleDrawer.cs` (& `Vector3ToggleDrawer.cs`) — toggling an axis writes literal `0`/`1` and reads `== 1`, so any prior non-0/1 magnitude is lost.
PD-3. `SetProperty/Editor/SetPropertyDrawer.cs:38` — `type.GetProperty(attribute.Name)` uses public-only binding, so a fully non-public property isn't found (logs an error); a public property with a private *setter* still works. The real issue is the stale value: `IsDirty` is set and cleared in the same `OnGUI`, so the setter runs on the pre-apply field value; it also re-does `GetProperty` uncached each dirty frame.
PD-4. `Regex/Editor/RegexDrawer.cs` — when `attribute.regex` is null the pattern is compiled per-call with no try/catch → an invalid pattern throws on every repaint.
PD-5. `OnChange/Editor/OnChangeDrawer.cs` — the callback fires via `MonoBehaviour.Invoke(name)` before any explicit apply, so it can observe a pre-change value (and requires a no-arg method; won't run in edit mode).
PD-6. `EnumButtonGroup/Editor/EnumButtonGroupDrawer.cs:75` — the static `Draw` uses `Array.IndexOf(trueNames, names[i])` unguarded to index `typedValues[sortedIndex]` → throws on a stale/removed enum name.
PD-7. `EnumFlag/Editor/EnumFlagDrawer.cs:20` — writes to `property.intValue` via `(int)Convert.ChangeType(...)` → truncates for `long`/`ulong`-backed enums.
PD-8. `Info/Editor/InfoDrawer.cs` — help box uses a fixed `helpBoxHeight = 38` rather than measuring the text → long multi-line text clips.
PD-9. `PositionHandle/Editor/PositionHandleDrawer.cs:54` — swallows all exceptions in a bare `catch {}`. (Undo *is* recorded via `ApplyModifiedProperties()`, so the earlier 'no Undo' claim was wrong.)
PD-30. *(New)* `EnumButtonGroup/Editor/EnumButtonGroupDrawer.cs:10` — `OnGUI` only initializes when `_properties == null` and never re-initializes on `property.propertyPath` change, so a drawer instance reused across elements of an enum array/list binds element 0's cached state to every element (toggles read/write the wrong element). Same reused-drawer bug its sibling `EnumFlagsButtonGroupDrawer` was already fixed for (see its `_propertyPath != property.propertyPath` guard); apply the same guard here.

*Verified — not a bug: PD-10 (`-standardVerticalSpacing` when hidden is the standard row-collapse idiom → ~0 net height, no overlap).*

### Unity-native duplication
PD-11. `EnumFlag/Editor/EnumFlagDrawer.cs:19` — the drawer just wraps `EditorGUI.EnumFlagsField` (Unity's native C# `[Flags]` field — distinct from `MaskField`, the plain-int/layer masker). Since Unity 2017.3 the default inspector auto-renders `[Flags]` enums this way, so the `[EnumFlag]` attribute is largely obsolete.
PD-12. `FilePath` & `FolderPath` drawers — near-identical text-field+browse pattern.
PD-13. `Popup` & `PropertyPopup` drawers — both reimplement `EditorGUI.Popup`; overlap `EnumPopup`.
PD-14. `Label/Editor/LabelDrawer.cs` — duplicates `[InspectorName]` / GUIContent label.
PD-15. `Password/Editor/PasswordDrawer.cs` — thin wrapper over `EditorGUI.PasswordField`.
PD-16. `PreviewTexture/Editor/PreviewTextureDrawer.cs` — duplicates the object-field thumbnail idea (draws via `EditorGUI.DrawPreviewTexture`/`GUI.DrawTextureWithTexCoords`, not `AssetPreview.GetAssetPreview`).
PD-17. `SetCase/Editor/SetCaseDrawer.cs` — upper/lower duplicate `string.ToUpper()`/`ToLower()` (there is no title-case branch).
PD-18. `Vector2Toggle`/`Vector3Toggle` drawers — hand-rolled rows over `Vector2/3Field` + mask.

### Refactoring / dead code
PD-19. `Vector2Toggle` & `Vector3Toggle` drawers/attributes — near-identical; share a base looping over N axes.
PD-20. `EnumButtonGroupDrawer.cs` & `EnumFlagsButtonGroupDrawer.cs` — substantial copy-paste (label rect, per-button widths, toolbar).
PD-21. `EnumButtons/Editor/EnumButtonsDrawer.cs` vs `EnumButtonGroupDrawer.cs` — overlapping intent (EnumButtonsDrawer is a simpler `GUI.Toolbar` single-select; its `attribute` override is self-referential/broken).
PD-22. `HideInEditMode/*` & `HideInPlayMode/*` — mirror-image pairs; collapse to one bool.
PD-23. `OnChange` & `SetProperty` drawers — both reflect a member by name uncached, though via different mechanisms (`MonoBehaviour.Invoke` by string vs `GetProperty` + deferred `IsDirty`). SetProperty re-does `GetProperty` every dirty frame.
PD-24. `Lock/Editor/LockDrawer.cs` — `BeginDisabledGroup`/`EndDisabledGroup` wrap overlaps Disable/DisableIf; share a helper.
PD-25. `MinMax` & `SteppedRange` — both step-snap, but the math differs (MinMax `Round(v/step)*step`, SteppedRange `RoundToNearest`) — not literally copy-pasted.

### Tidying
PD-27. Mixed tabs/spaces: `PropertyPopupDrawer.cs`, `PopupDrawer.cs`.
PD-28. Inconsistent `[AttributeUsage]` presence across attribute files (e.g. `EnumFlagAttribute` has it; most others don't).
PD-29. Commented-out code: `SetPropertyDrawer.cs` (one line); larger commented blocks in `PropertyPopupDrawer.cs` and `EnumFlagsButtonGroupDrawer.cs`.

---

## Extensions / UnityEngineX (`Scripts/Extensions/UnityEngineX/`)

### Bugs
UEX-1. `AnimationCurve/CurveTypes/Vector2Curve.cs:30-32` — `AddKey(time,x,y)` recurses into itself → stack overflow. Should call the `AddKey(time, Vector2)` override.
UEX-2. `Color/HSBColor.cs:181` & `HSVColor.cs:182` — hue-interp line `h = angle/360f` commented out → `Lerp` always returns hue 0 (red).
UEX-3. `Texture/TextureX.cs:58-79` — `GPUScale` calls `RenderTexture.ReleaseTemporary` (and never restores `RenderTexture.active`) before returning, but callers `CopyWithSizeScaled`/`ResizeScaled` `ReadPixels` *after* that release → reads from a freed/stale target, so scaled output is unreliable.
UEX-4. `Vector3X.cs:71-73` — `Reflect` returns `2·Project(d,n) − d`, the negation of a true reflection (Unity's is `d − 2·Project`). No callers in the project, so fixing it is internally safe (public API, so external callers could depend on the current sign).
UEX-5. `ObjectX/SelectionX.cs:131-134` — `objects` setter assigns to its own property when `value==null` → infinite recursion; `:119-122` — `activeObject` setter lacks `else` → NRE on `value.GetEntityId()`.
UEX-6. `Rigidbody2DX.cs:5` — `Mathf.DeltaAngle(target, current)` gives `current−target` → torque drives away from target (positive feedback). Args reversed.
UEX-7. `CameraX.cs:38-40` — `WorldToViewportVector` uses a `f(0)−f(vec)` sign form negated vs a mathematically-correct viewport vector. It *matches* the sibling `…ToWorldVector` methods (shared internal sign convention), so verify intent before changing — callers may rely on it.
UEX-8. `ColliderX.cs:11-23` — `GetClosestPoint` raycasts toward the pivot, not the surface; falls back to the pivot. Duplicates + worse than `Collider.ClosestPoint`.
UEX-9. `ComponentX.cs:146-151` — `ImmediateAncestorsExcludingSelf` passes child-search params `(1,1)` instead of `(-1,-1)` → searches children.
UEX-10. `ComponentX.cs:41-50` — `BetterBroadcastMessage` edit-mode branch sends to root N times, never to children.
UEX-11. `AnimationCurveX.cs:351` (EaseIn drops `inTangent`), `:409-411` (`EaseOutInvert` delegates to `EaseInInvert`), `:419/:467` (reads/writes `ks[1]` before assigned).
UEX-12. `MathX.cs:630-645` — `FindIndexPosition` accesses `list[1]`/`list[^2]` unguarded → IndexOutOfRange on single-element list.
UEX-13. `MathX.cs:39-61` — `RepeatInclusive` divides by zero when `min==max`.
UEX-14. `EventSystemX.cs:149-156` — `ForceStartDrag` derefs `pointerEvent` despite `= null` default → NRE.
UEX-15. `PhysicsX.cs:18,37` — `LookRotation(dir, Vector3.up)` is degenerate when `dir ∥ up`.
UEX-16. `RayX.cs:63-66` — `GetClosestDistanceToSphere` ignores `sphereRadius` → distance to center, not surface.
UEX-17. `GeometryX.cs:29-34` — `TestPlanesPoint` hard-codes `i < 6` → IndexOutOfRange for <6 planes, ignores extras; null-refs on null.
UEX-18. `PlaneX.cs:14-30` — `GetHitPoint`/`GetDistanceToPointInDirection` ignore `Plane.Raycast`'s bool → return points behind the ray.
UEX-19. `ReflectionX.cs:34-36,80-81,126-127,164-165` — `parts[i+2]` unguarded → IndexOutOfRange on malformed paths; `SetValueFromObject` (222) ends with a no-op `value = val;` and doesn't work on structs.
UEX-20. `TextureX.cs:104-107` — `Create` logs mismatch then proceeds → `SetPixels` throws; `:95` — overload silently drops `textureFormat`.
UEX-21. `SelectionX.cs:64-66,86-100` — save-last-selection logic inverted; `CompareWithLastSelection` mixes `objects` vs `gameObjects`.
UEX-22. `ScreenX.cs:283,295` — `int.Parse(UnityStats.screenRes.Split('x'))` unguarded → FormatException/IndexOutOfRange in a per-frame getter.
UEX-23. `OnGUIX.cs:97` — `DrawCircle` divides by `numPoints-1` (`2π/(numPoints-1)`) → divide-by-zero when `numPoints==1`. (The ring itself does close — the last sample lands at 2π = the first.)
UEX-24. `GizmosX.cs:277,301` — `DrawWireArc`/`DrawWireArcSegment` missing `return` after the degenerate circle draw.
UEX-68. *(New)* `ReflectionX.cs:199-202` — in `SetValueFromObject`, if `obj` itself is the target `T` on the first path segment, `fieldInfo` is still null when `fieldInfo.SetValue(obj, val)` runs → NullReferenceException. (Separate from the dead `value = val;` / struct issues already noted in UEX-19.)

### Unity-native duplication
UEX-25. `Color/HSVColor.cs` & `HSBColor.cs` — reimplement (buggily) `Color.RGBToHSV`/`HSVToRGB`.
UEX-26. `ColorX.cs` — `BlendMode.Normal` == `Color.Lerp`; additive/multiply at 1 == `+`/`*`; `Grayscale` == `Color.grayscale`; `RandomRGB` overlaps `Random.ColorHSV`.
UEX-27. `ColliderX.cs:11` — duplicates `Collider.ClosestPoint`/`ClosestPointOnBounds`.
UEX-28. `HashSetX.cs:5-9` — `AddRange` duplicates `HashSet.UnionWith`.
UEX-29. `LayerMaskX.cs:31-33,65-68` — `Includes` == `(mask & (1<<layer))!=0`; `Inverse` == `~mask`.
UEX-31. `UIBehaviourX.cs:6-14` — `GetRectTransform` == cast; `GetParentCanvas` == `GetComponentInParent<Canvas>()`.
UEX-32. `RectTransformX.cs:459-467` — `GetSize/Width/Height` are trivial getters over `rect.*`; low value but harmless — they mirror the non-trivial `SetWidth`/`SetHeight` (there is still no native `RectTransform.rect` setter). Not worth changing.
UEX-33. `AnimationCurveX.cs:372-393` — `EaseInOut` is redundant with `AnimationCurve.EaseInOut` (both zero-tangent S-curves).
UEX-34. `Vector4X.cs:19-21` / `QuaternionX.cs:72-74` — duplicate implicit Vector4↔Quaternion conversion.
UEX-36. `RandomX.cs:37` — `eulerAngle` == `Random.value*360` (≈ `Random.Range(0f,360f)`), a trivial convenience. (`onUnitCircle` is NOT a duplicate — it returns a point on the edge; `Random.insideUnitCircle` is inside the disk, and there is no 2D built-in for the edge — so it stays.)
UEX-37. `MeshRendererX.cs:6-9` — `SharedMaterialsContains` == `Array.IndexOf`.

*Verified — not a bug: UEX-30 (`RigidbodyX` `Set*`/`Translate` route through `rigidbody.rotation`/`MovePosition`/`MoveRotation` — physics-aware, not thin transform wrappers, and not equivalent to the Transform versions).*

### Refactoring / dead code
UEX-38. `Color/HSVColor.cs` & `HSBColor.cs` near-identical; both have an unreachable trailing `else`.
UEX-39. `CanvasGroupX.cs`/`CanvasX.cs` — `CanvasGroupsAllowInteraction`/`CanvasGroupsAlpha` duplicated verbatim. (`GetRenderCamera` is only in `CanvasX`, not `RectTransformX` — that half of the original claim was wrong.)
UEX-40. `BoundsX.cs:21-76` — three copy-pasted `CreateEncapsulating` scans; `:111-169` — a 5-line face block repeated six times.
UEX-41. `RayX.cs:30-61,68-85` — two large commented-out method bodies (dead).
UEX-42. `ReflectionX.cs` — three near-identical `GetValueFromObject` overloads; `SetValueFromObject` largely dead.
UEX-43. `AnimationCurveX.cs:188-214` — `RemoveKeysBetween`/`RemoveKeysBetweenAndIncluding` are byte-identical (one is wrong per its name). (The `curve.keys` array is captured once, not re-read in the loop — that part of the original note was wrong.)
UEX-44. `Vector3Curve.cs:142-145` — `EstimateClosestTimeToValue` is an unimplemented `public` stub (`Debug.Log("TODO"); return 0`); `Vector2Curve`/`Vector3Curve` diverge.
UEX-45. `PhysicsX.cs:14-46` — `FakeSphereCastRays`/`FakeConeCastRays` near-identical.
UEX-46. `RectTransformX.cs:4` / `CanvasX.cs:45` — shared static `Vector3[] corners` scratch buffer → reentrancy aliasing.
UEX-47. `ComponentX.cs:335-374` — `GetInterfaces` returns `Enumerable.Empty` but `GetInterfacesInChildren` returns `null` → NRE in `foreach`.
UEX-48. `SceneManagerX.cs:12-24` — `GetCurrentSceneNames/Paths` duplicate `GetCurrentScenes` + `.Select`.
UEX-49. `ScreenX.cs:454-529` — `PlayerLoopUtils` misplaced inside the `ScreenRectProperties` data class.

### Tidying
UEX-61. Commented-out dead blocks: `RayX.cs:30-85`, `OnGUIX.cs:66-190`, `ReflectionX.cs`, `GizmosX.cs:38,42`, `RectTransformX.cs:219-223,380-392` ("OLD STUFF, built for 80 Days"), `ColorX.cs:208-220` (`BlendOverlay` commented → returns `color2`, a stub), `TextureX.cs:59-61`, `ScreenX.cs:105-121,172,411`.
UEX-62. Debug logs in shipping code: `Vector3Curve.cs:143` per call; `ColorX.cs:95` LogError then /0 → NaN; `TrailRendererX.cs:20,25` on error paths (null trail / double-clear); `HSBColor.cs:188-211` `Test()` scaffolding.
UEX-63. Pervasive typo `CameraX.cs` "frustrum" → "frustum" baked into ~14 public method names; also "Cmera" (`:131`).
UEX-64. Typos: `TransformX.cs` "Heirarchy"/"Descendents"; `GizmosX.cs` "matricies"/"reassinging"; `OnGUIX.cs` "matricies"; `ImageX.cs:20,40` error strings name the wrong method; `ScreenXEditorWindow.cs:12` method name mismatch.
UEX-65. Unused usings: `SpriteX.cs:2`. *(SystemInfoX.cs:2 resolved with UEX-35.)*
UEX-66. `DebugX.cs:16` — `debug=true` gates `LogError` too (errors suppressed when false).
UEX-67. `TextureX.cs:106` — `MonoBehaviour.print` instead of `Debug.LogWarning`.

---

## Extensions / Geometry (`Scripts/Extensions/Geometry/`)


### Unity-native duplication
GEO-18. `Point/PointRect.cs` — duplicates `RectInt`.
GEO-19. `Line/Line.cs:218-263` & `Line3D.cs:145-197` — closest-point-on-segment duplicated 2D/3D and reimplemented several times.
GEO-20. `Polygon/Polygon.cs:1499-1524` — `PointToLineSegmentSquaredDistance` duplicates `Line` closest-point logic.

### Refactoring / dead code
GEO-21. `Line/Line.cs:274-527` — ~250 lines of commented-out dead code (voxel/Bresenham attempts, Lua pseudocode).
GEO-22. `Polygon/Editor/LineEditor.cs` — entire file (5-375) commented out.
GEO-23. `Polygon/Polygon.cs:1053-1068` — `intersectsWithPolygon`/`whollyContainsOtherPolygon` are lowercase duplicates of the `Is…`/`Wholly…` methods (569-584).
GEO-24. `Polygon/Polygon.cs:854-880` — `ContainsPoint(Vector2[])` and `(List<Vector2>)` identical; share via `IList`.
GEO-25. `Polygon/Polygon.cs:281-300` — `GetRegularEdgePosition`/`GetPositionAtArcLength`/`GetPositionAtNormalizedArcLength` overlap heavily.
GEO-26. `Polygon/Polygon.cs:1734-1800,1922-1972` — large commented-out `HullCull`/`GetMinMaxBox` and two `GetSimplifiedVerts` impls.
GEO-27. `Sphere/Sphere.cs:202-264` — two large commented-out `Intersects(Ray)` impls.
GEO-28. `Polygon/Polygon.cs:996-999` — `GetHashCode` returns reference hash, inconsistent with value-based `Equals`.
GEO-29. Structs (`Line`, `Line3D`, `PointRect`) — `operator ==`/`Equals` do `(object)left == null` checks that can never be null for a struct.

### Tidying
GEO-37. `Line/Line.cs:571` — doc typo "to (x1, y10".
GEO-38. `Polygon/Polygon.cs:1837` "sinze"; `:1135` "calcuate teh direction".
GEO-39. `Polygon/Polygon.cs:207` — `Debug.Log` inside `Scale` (a pure math method).
GEO-40. `Sphere/Sphere.cs:129` — `Debug.LogError("Should never get here")` (reachable given the bugs). *(Resolved incidentally by GEO-15 — that `LogError` was removed.)*
GEO-41. `Polygon/…PolygonEditorTool.cs:301` — `(i+1)%(vertices.Length-1)` off-by-one in edge-normal debug draw.
GEO-42. `Polygon/Polygon.cs:1433` — shared mutable `static List<int> tris` used by `GetRandomPointInPolygon` (not thread-safe).
GEO-43. `Point/PointRect.cs` — inconsistent namespacing: `Line`/`Polygon`/`Point`/`PointRect` are global while `Triangle`/`Sphere`/`RegularPolygon`/`StarPolygon` are in `UnityX.Geometry`.

---

## Extensions / Grid + UnityEditorX (`Scripts/Extensions/Grid/`, `Scripts/Extensions/UnityEditorX/`)

### Refactoring / dead code
GRID-24. `Grid 2D/Mesh Generator/HeightMapMeshGenerator.cs` — ~600 lines of near-identical externals/internals × triangles/quad copy-paste. *(Deferred: large mechanical refactor, best done with in-editor compile + visual verification.)*
GRID-30. `Grid 2D/Grid/SquareGridAgent.cs` & `RadialGridAgent.cs` — duplicated enter/exit diffing. *(Left as-is: both extend MonoBehaviour with no common base; sharing would need an invasive base-class/serialization change for little gain.)*

---

## Extensions / Algorithms + Camera + Spline


### Unity-native duplication
ACS-12. `Algorithms/EasingFunction.cs:793-1127` — ~160-line if-chains; a dictionary/switch would replace both. (vendored)
ACS-13. `Spline System/SplineBezierControlPoint.cs:30-31` — `GetAutoDistanceIn`/`Out` identical.

### Refactoring / dead code
ACS-14. `Camera/Shots/Bounding Sphere/BoundingSphere.cs:253-1067` — ~815 lines of commented-out XNA-style alternate impl.
ACS-15. `Algorithms/UpscaleTools.cs:2-48` — commented-out `Test` MonoBehaviour.
ACS-16. `Spline System/Spline.cs:448-463` — `SubdivideInCurve` never called; `:490-497` — `var r` computed, never used; `:270-289,179-187,237-243` — commented-out duplicates.
ACS-17. `Spline System/Editor/SplineEditor.cs:368-452` — commented-out block referencing an obsolete `RiverBezierPoint`.
ACS-18. `Algorithms/Noise/SimplexNoiseGenerator.cs:39-88` — `Generate`/`GenerateRepeating` duplicate setup.
ACS-19. `Camera/Camera Properties/CameraProperties.cs:607-620` — `GetHashCode` multiplies each field hash → zero-hash field zeroes all.
ACS-20. `Camera/Shots/CameraShotGeneratorTools.cs:144-181` & `CameraProperties.cs:422-432` — sizeable commented-out blocks.

### Tidying
ACS-25. `Camera/Shots/CameraShotGeneratorTools.cs:249` — `Debug.LogWarning` fires every call in the shot hot path.
ACS-26. `Camera/Camera Properties/CameraModifierZone.cs:33-36` — empty `if(!isPlaying){}else{...}`.
ACS-27. `Camera/Camera Properties/CameraPropertiesModifier.cs:47-48` — empty Axis+Multiply branch (silent no-op).
ACS-28. `Camera/Camera Properties/CameraPropertiesTween.cs:113-131` — tween never lerps `axis`/`orthographic`/`orthographicSize` (behavioural gap).
ACS-29. `Algorithms/UpscaleTools.cs:262-271` — unused local `i`.
ACS-30. `Spline System/SplineBezierCurve.cs:108-121`, `Spline.cs:206` — leftover commented `BinarySearch`/index lines.
ACS-31. `Camera/Camera Properties/CameraPropertiesBuilderQueue.cs:34-36` — 2-arg `Add` doesn't re-sort; `Update` invokes the delegate with no null check.

*Note:* `EasingFunction.cs`, `SimplexNoise.cs`, `Noise.cs`, `AStar.cs` appear third-party/vendored — findings are real but may be intentionally kept close to upstream.

---

## Extensions / Text + Scene Management + Collections + Serializable Components + Audio

### Unity-native / .NET duplication
TXT-16. `Collections/ListX.cs` — `ToList`/`First`/`Last`/`Contains`/`IndexOf` duplicate LINQ/`List<T>`. *(Left as-is: widely-used public helpers; removing breaks call sites across the library.)*
TXT-17. `Collections/IEnumerableX.cs` — `ToHashSet`/`DistinctBy`/`Chunk`/`Filter`/`Map` duplicate BCL. *(Left as-is: public helpers, and `DistinctBy`/`Chunk` aren't in Unity's netstandard2.1.)*
TXT-18. `Text/Text Effects/WordWobble.cs` — manual `IndexOf(' ')` word-split reimplements `string.Split`. *(Left as-is: entangled with the char→vertex-index mapping guarded in TXT-10.)*
TXT-20. `Scene Management/Scene Set/RuntimeSceneSet.cs` — hand-rolled build-settings collection + manual array-grow. *(Left as-is: delicate EditorBuildSettings code; correctness handled by TXT-11.)*

### Refactoring / dead code
TXT-24. `Text/Text Effects/VertexWobble/CharacterWobble/WordWobble` — identical `Wobble` + scaffold copy-pasted. *(Left as-is: sharing would need a new base/helper type — invasive for little gain.)*

---

## Extensions / System (`Scripts/Extensions/System/`)

### Bugs
SYS-1. `PathX.cs:31-54` — `ReplaceIllegalCharacters` splits only on `\` → on macOS/Linux (this project) the whole forward-slash path is treated as one filename and every `/` is replaced with `_`. Dedup (50-51) collapses only one doubled pair.
SYS-2. `FlagsX.cs:98` — `CreateEverything<T>()` does `(T)(object)~0` → InvalidCastException for non-int-backed enums; `:123-125` `Invert<T>` depends on it; `:75-82` `Create<T>` casts via `(int)(object)flags[i]` → same crash.
SYS-3. `FlagsX.cs:154-158` — `GetFlags` zero-named-member branch is unreachable → `GetFlags(0)` never yields the zero member.
SYS-4. `ByteFormatter.cs:21-31` — `ToSizeAuto` returns `long`, truncating fractions (1.5 KB → "1 KB"), inconsistent with the double-based `ToSize`.
SYS-5. `StringX.cs:5` — `IsWhiteSpace` returns true for empty; misses `\r`/vertical-tab/form-feed/Unicode whitespace.
SYS-6. `StringX.cs:57` — `Truncate` throws on negative length; NREs on null source.
SYS-7. `StringX.cs:89-113` — `AfterFirst`/`After`: match at end returns the whole original instead of empty.
SYS-8. `DirectoryX.cs:12-19` — `GetRelativePath` feeds raw paths to `new Uri(...)` → UriFormatException on relative input; `#` mis-parsed as a fragment.
SYS-32. *(New)* `EnumX.cs:47-52` — `Random<T>()` does `Enum.ToObject(typeof(T), Random.Range(0, Length<T>()))`, treating the random *index* as the enum's *underlying value*. For any enum not numbered contiguously `0..N-1` (flags, explicit values) it returns wrong/undefined members and can never reach higher ones. Should index into `GetValues<T>()`.

### Unity-native / .NET duplication
SYS-9. `StringX.cs:5,33-41` — `IsWhiteSpace` overlaps `string.IsNullOrWhiteSpace` (but is buggy — see SYS-5); `Contains(string, StringComparison)` duplicates the BCL overload (only on the netstandard2.1 API-compat level, not .NET Framework).
SYS-10. `EnumX.cs:14-147` — `Length<T>`/`IsValid`/`ToArray`/`GetEnumerable` duplicate `Enum.GetValues`/`Enum.IsDefined`.
SYS-11. `FlagsX.cs:33-56` — `SetFlag`/`UnsetFlag`/`HasFlag` reimplement `Enum.HasFlag` + bitwise ops.
SYS-12. `PathX.cs:9-11` — `GetFullPathWithoutExtension` == `Path.ChangeExtension(path, null)`.
SYS-13. `DirectoryX.cs:12-19` — duplicates `Path.GetRelativePath` (available only on the netstandard2.1 API-compat level, not .NET Framework 4.x).
SYS-14. `BoolX.cs:10-20` — `ToBool`/`ToInt` wrap `Convert.*`.
SYS-15. `SystemX.cs:10` — partially duplicates `EditorUtility.RevealInFinder` (comment notes this).

### Refactoring / dead code
SYS-16. `EnumX.cs:23-76` — `#if !UNITY_WINRT … if(!typeof(T).IsEnum) throw` copy-pasted 5× and dead (the `where T:Enum` constraint already guarantees it).
SYS-17. `EnumX.cs:87-101` — `ToArray<T>` adds nothing over `(T[])GetValues`; `GetEnumerable` boxes.
SYS-18. `FlagsX.cs:14-125` — two parallel families (raw-int vs generic enum) with overlapping duties + inconsistent naming; `:101-107` — `(int)Math.Pow(2,x)` vs `1<<x`.
SYS-19. `StringX.cs:67-114` — `Before`/`BeforeLast`/`AfterFirst`/`After` share structure but mix Ordinal vs culture-sensitive comparison.
SYS-20. `SystemX.cs:25-81` — `OpenInMacFileBrowser`/`OpenInWinFileBrowser` near-identical.

### Tidying
SYS-27. `ByteFormatter.cs:8` — commented-out `FromToSize` stub.
SYS-28. `FlagsX.cs:167` — leftover `//yield return value;`.
SYS-29. Mixed tabs/spaces + stray blank lines: `StringX.cs`, `FlagsX.cs:108-110`.
SYS-30. `SystemX.cs:53,77` — `e.HelpLink = ""` "silence warning" hack.
SYS-31. `StringX.cs:119 vs 126` — `UppercaseFirstCharacter` is a `this`-extension but `LowercaseFirstCharacter` is a plain static.

*Verified — not a bug: SYS-26 (`throw new("…")` is valid C# 9 target-typed `new`; compiles fine — `UNITY_WINRT` is undefined).*

---

## Extensions / Structures (`Scripts/Extensions/Structures/`)

### Bugs
STR-4. `Island/OwnedIslandDetector.cs:10` + `IslandDetector.cs:9` — `new static islands` hides the base list → base and derived helpers write to different lists → silently dropped results. *(Update: dropped-results hazard resolved — the owned detector no longer calls base helpers; the `new static` shadowing itself remains.)*
STR-5. `Shape.cs:33` — `pointBounds` truncates via `(int)`; single point → zero-size bounds; negative origins mislocated.
STR-6. `Shape.cs:54-68` — `CreateContiguous` `do/while(!valid)` has no attempt cap → stall risk; seed at (1,1) assumes `numPoints >= 2`.

### Unity-native duplication
STR-8. `Structure.cs` — `Contains(Func)` duplicates `Enumerable.Any`.

### Refactoring / dead code
STR-11. `Island/IslandDetector.cs:9-11` — static scratch collections should be instance fields. *(Update: the `List` work-set is gone and the flood-fill `Stack`/owned seed-`Queue` are now locals; `testedPoints` and `islands` remain `static`.)*
STR-12. `Island/OutlineDetector.cs:16-17` — `found = true` set twice; the outer is dead except in the degenerate `numCorners==0` case (inner loop never runs).

### Tidying
STR-16. `Island/OwnedIsland.cs:14-101` — ~88-line commented-out `OutlineSolver` block.
STR-17. `Island/OutlineDetector.cs:8,36-37,83,94-107` — multiple commented-out lines; `:100` — stray `;` after a `foreach`.
STR-18. `Shape.cs:41-89` — mixed tabs/spaces; a `TypeMap<bool>` local named "shape" collides with the returned `Shape` type.
STR-19. Unused usings in `Island/Island.cs`, `OwnedIslandDetector.cs`.

---

## Extensions / Spring (`Scripts/Extensions/Spring/`)

### Bugs
SPR-1. `Spring.cs:253` — undamped `SettlingDuration` divides by `-omegaZeta` (zero when `dampingRatio == 0`) → division by zero yielding `+Infinity`; the spring never settles.
SPR-2. `Spring.cs:327-331` — `CalculateTimeOfMaximumDisplacement` can return a spurious near-zero "peak" only in a narrow edge (post-step `Velocity` rounding to exactly 0 so `Sign==0`); the normal released-from-rest case is handled correctly. Low priority.

### Unity-native duplication
SPR-3. `Spring.cs:145-150` — *removed: not an issue.* `Update(value, target, ref velocity)` deliberately mirrors the `Mathf.SmoothDamp` signature so a `Spring` is a drop-in alternative; this is by design, not accidental duplication. Nothing to do.

### Refactoring / dead code
SPR-4. `Spring.cs:213-231,431-480` — large block of near-identical 3-line forwarders for `Force`/`SpringForce`/`DampingForce`/`Acceleration`.
SPR-5. `Editor/SpringPropertyDrawer.cs:22-60` — `OnGUI` and `Draw` almost identical; `:330-348` — `DrawYMinMaxScaleLabels`/`DrawXScaleLabel`/`DrawYAxisLabel` unused.
SPR-6. `Editor/SpringContextMenuPresets.cs:1` — unused `using System.Collections;`.

### Tidying
SPR-11. Pervasive "oscellate"/"oscellation" (→ oscillate); "Contructors" (82); "my be specified" (117,125).
SPR-12. `Spring.cs:183-184` — commented-out alternative velocity formula; `:261` — un-indented comment.
SPR-13. `Editor/SpringPropertyDrawer.cs:138,173-179,296,332-333` — commented-out fragments; nested `GraphGUI` uses a different indent scheme.
SPR-14. `Editor/SpringHandlerPropertyDrawer.cs:39,42,64-67` — `new GUIContent(new GUIContent("…"))` double-wrapping.

---

## Extensions / Easer (`Scripts/Extensions/Easer/`)

### Bugs
EAS-1. `MoveTowards/Vector2MoveTowardsEaser.cs:11` & `Vector3MoveTowardsEaser.cs:11` — `Vector2/3.MoveTowards(current, target, maxDelta)` ignore `deltaTime` → frame-rate dependent (should be `maxDelta * deltaTime`).
EAS-2. `SmoothDamp/SpringDamper.cs:79,94` — `DampedSpring`/`CriticallyDampedSpring` overwrite `deltaTime = 1f/60f`, discarding the caller's value → the `deltaTime` param and `Time.deltaTime` overloads are inert/misleading.
EAS-3. `SmoothDamp/SpringDamper.cs:92-105` — `CriticallyDampedSpring` uses explicit Euler with a fixed step → overshoots for stiff springs (not truly critically damped).
EAS-4. `BaseEaser.cs:13,24` — `_target.Equals(value)`/`_current.Equals(value)` NRE for reference `T` when the field is null (unreachable for the value-type instantiations here).

### Unity-native / .NET duplication
EAS-5. `SmoothDamp/FloatSmoothDamper.cs` & `MoveTowards/FloatMoveTowardsEaser.cs` — reimplement the whole `BaseEaser`/`SmoothDamper<T>`/`MoveTowardsEaser<T>` scaffolding instead of deriving from the generics (~130/135 lines each).

### Refactoring / dead code
EAS-6. The two `Float*` easers should derive from the generics like every sibling (~15-30 lines).
EAS-7. `SmoothDamp/SpringDamper.cs:74` — the no-`deltaTime` `DampedSpring` overload passes `Time.deltaTime`, which line 79 then overwrites with `1f/60f` (pointless).
EAS-8. `SpringDamper.cs:31-33` — `AddImpulse` vs `AddForce` semantics overlap; `AddImpulse` bypasses the NaN/Inf assert.
EAS-9. `GetDelta` bodies duplicated across the SmoothDamp/MoveTowards types.
EAS-10. Unused `using UnityEngine.UI;`: `QuaternionSmoothDamper.cs:4`, `Vector2SmoothDamper.cs:4`, `Vector3SmoothDamper.cs:4`. (Not `ColorSmoothDamper` — it has no such using.)
EAS-11. `Vector3SmoothDamper.cs:10` — ctor param named `target` but passed as `current`.

### Tidying
EAS-15. `FloatSmoothStepDamper.cs:77,83` — commented-out `// this.initial =`; `:96` — `var targetVelocity = 0` infers int.
EAS-16. Attribute-before-doc-comment ordering; mixed tabs/4-space; `[DisableAttribute]` vs `[Disable]` inconsistency.

---

## Extensions / Tween (`Scripts/Extensions/Tween/`)

### Bugs
TWN-1. `Types/Base/TypeTween.cs:242-243` — `Update` samples at the OLD time then advances the timer → value lags one frame (stale at start and just before completion).
TWN-3. `Types/QuaternionTween.cs:16-18` — `SetDeltaValue` computes `current * last` (a delta should be `current * Inverse(last)`).
TWN-4. `Types/Base/TypeTween.cs:279-281` — `GetValueAtTime` divides by `tweenTimer.targetTime` with no null/zero check (reachable via the zero-time path).
TWN-5. `Types/Base/TypeTween.cs:206-224` — zero-time branch fires `OnStart` then `OnComplete` in the same call. This is the expected semantics of an instant (zero-duration) tween — likely by-design, not a defect.

*Verified — not a bug: TWN-2 (`Timer.GetNormalizedTime()` clamps to [0,1], so the timer can't return >1; no overshoot).*

### Unity-native duplication
TWN-6. `Types/RectTween.cs:12-13` — packs a Rect into Vector4 for `Vector4.Lerp`; also clamped `Lerp` vs FloatTween's `LerpUnclamped` → inconsistent clamping across types.

### Refactoring / dead code
TWN-7. `Types/{Color,Float,Quaternion,Rect,Vector2,Vector3}Tween.cs` — the "iOS generic inheritance event crash workaround" (`new event …`, overrides) is copy-pasted verbatim ×6.
TWN-8. `Types/FloatTween.cs:24-36` — additionally redeclares `new OnStart` + overrides `TweenStart` (the other 5 don't) → inconsistent.
TWN-9. `Types/Base/TypeTween.cs:155,163` — `AnimationCurve.Linear(0,0,1,1)` allocated on every default Tween; cache a static readonly.
TWN-10. `TweenProperties.cs:5-11` — `setStartValue`/`setEasingCurve` flags never set by the ctors → the dispatch always takes the `!setStartValue` branch (start-value ctors ignored).

### Tidying
TWN-13. `Types/Editor/FloatTweenDrawer.cs:46-73` — large commented-out block incl. a `Debug.Log`.
TWN-14. `Types/Editor/TimerDrawer.cs:17-40` — commented-out rect/PropertyField lines.
TWN-15. `Types/*Tween.cs` — `if(OnComplete != null)OnComplete();` (missing space) in every subclass; mixed indentation in `TypeTween.cs:198-223`.

---

## Extensions / Range + ValuePicker

### Bugs
RNG-1. `Range/Range.cs:117` — `ShrunkToExclude` guard `if (value > min || value < max)` is almost always true → the intended out-of-range rejection is dead.
RNG-2. `Range/Range.cs:130` — `ExpandedFromPivot` computes the max endpoint from `min` (should be `max + expansion*(1-pivot)`).
RNG-3. `Range/Range.cs:173-174` — `RemoveRange` doesn't clamp `rangeToRemove` to `[min,max]`; fragile, but the `>`/`<` guards keep both emitted ranges non-inverted for intersecting input (no concrete wrong output found).
RNG-4. `Range/Range.cs:280-287` — `GetHashCode` multiplies → any zero endpoint (very common) → hash 0; also loses ordering.
RNG-5. `ValuePicker/Blender.cs:9,85` & `LogicBlender.cs:82` & `Selector.cs:167` — `previousValue.Equals(current)` NREs for reference `T` when previous is null.
RNG-6. `ValuePicker/LogicBlender.cs` — `Set` uses `.Equals` (`:27`) while `Remove` (`:45`) and `TryGetValueForSource` (`:70`) use `==` → inconsistent equality.
RNG-7. `ValuePicker/Selector.cs:70,140` — `desiredValue == null` on unconstrained generic `T` → `nullRemovesValue` silently does nothing for value types.

### Unity-native duplication
RNG-8. `Range/Range.cs:28-31` — `Auto` could use `Mathf.Min`/`Max`; `:45-69` — `CreateEncapsulating` ≈ LINQ `Min`/`Max`.

### Refactoring / dead code
RNG-9. `Range/RangeInt.cs` — the entire file is commented out.
RNG-10. `Range/Range.cs:148-158` — instance `Intersection` duplicates the static one.
RNG-11. `ValuePicker/Blender.cs:101-108` — dead local `T previous`.
RNG-12. `ValuePicker/Blender.cs` vs `Selector.cs` — `Set`/`AddPriority`/`Remove`/`EntryComparer`/`_priorities` near-identical (comparers differ in direction — significant).
RNG-13. `ValuePicker/Selector.cs:145-193` — "capture Value / recompute / fire onChange" repeated 3× → extract `NotifyIfChanged`.

### Tidying
RNG-18. `Range/Range.cs:393-426` — commented-out `RangeTests`.
RNG-19. Typo `trunctationValue` (→ truncation) at `Range.cs:116,123`.
RNG-20. `ValuePicker/LogicBlender.cs:26,51-52` — leftover commented assert + alternative impl ("This creates garbage.").

---

## Extensions / FlexLayout + NoiseSampler + GLDebug + Property Curve + FSM + Version Control + Timer + MeshBuilder + Texture Transform Utils + misc

### Bugs
MISC-1. ✅ *Fixed (see Done).* "Create" button now assigns `new NoiseSampler()` (was `new SpringHandler(...)`, a copy-paste from the Spring drawer).
MISC-2. ✅ *Fixed (see Done).* `matZOff` getter now uses `_matZOff`; `OnPostRender` draws `linesZOn` with `matZOn` and `linesZOff` with `matZOff` (were swapped).
MISC-3. `Texture Transform Utils/TextureTransformUtil.cs:122-123` — `FlipVertical`'s Graphics-path case leaves the matrix as identity. This likely still flips (because `Graphics.DrawTexture` is inherently Y-flipped and the `Normal` case explicitly un-flips), so it's confusing/fragile rather than broken — verify visually before changing.
MISC-4. ✅ *Fixed (see Done).* `GetGitBranch` now strips the `refs/heads/` prefix, keeping the full branch name incl. slashes (e.g. `feature/foo`).
MISC-5. ✅ *Confirmed a bug, fixed (see Done).* Traced: inserting time 1 into `[0,2,4]` gave `[1,0,2,4]` (wrong index) and the exact-time branch fell through to a duplicate `Insert`. `AddKey` now inserts at the order-preserving index and `return`s after an exact-time replace.

### Unity-native duplication
MISC-6. `Property Curve/PropertyCurve.cs` — reimplements `AnimationCurve`/`Keyframe`/`WrapMode`. *(Not an issue — the generality is the point: `AnimationCurve` is `float`-only, whereas `PropertyCurve<T>` interpolates any `T` (Color, Vector, quaternion, custom types) via its `Lerp`. There's no built-in to defer to, so keep it. Left as-is; no proposal beyond the AddKey fix in MISC-5.)*

### Refactoring / dead code
MISC-7. `NoiseSampler/Editor/NoiseSamplerPropertiesPropertyDrawer.cs:18-124` — `OnGUI`/`Draw` almost identical; three near-identical `DrawNoiseGraph` overloads.
MISC-8. `Texture Transform Utils/TextureTransformUtil.cs` — two parallel pipelines (blit vs `Graphics.DrawTexture`) with heavy copy-paste; `CopyWithSizeAndImageOrientation2` unhelpfully named.
MISC-9. `GLDebug/GLDebug.cs:205-307` — `DrawSquare`/`DrawCube` overload triplets are repetitive boilerplate.

### Tidying
MISC-14. Commented-out lines: `NoiseSamplerPropertyDrawer.cs:32-52`, `NoiseSamplerPropertiesPropertyDrawer.cs:151-336`, `GLDebug.cs:50,62`, `MeshBuilder/AddPlaneParams.cs:45-50`, `FlexLayout/FlexLayout.cs:45`, `Version Control/Editor/VersionBuildPreProcessor.cs:34-44`.
MISC-15. ✅ *Fixed (see Done).* `DrawCircle` now connects consecutive points (and closes the loop) instead of drawing 0.02-unit stubs.
MISC-16. Inconsistent indentation in the nested `GraphGUI` class (`NoiseSamplerPropertiesPropertyDrawer.cs:163-336`).
MISC-17. `NoiseSamplerPropertiesPropertyDrawer.cs:8` — `graphXRange` is `static` but effectively const.

*Verified non-findings:* `FlexLayout` margin accounting is self-consistent (no double-count); `StateMachine.GetStatesInheriting<R>` is loosely typed but not a concrete bug.

---

## Cross-cutting themes (worth a single sweep)

XC-1. ✅ *Fixed (see Done).* `Range` and `CameraProperties` switched from `hash *= …` to `hash = hash*31 + …`; `Polygon` switched from the array reference hash to a content hash consistent with its `SequenceEqual`. `SerializableTransform`, `Point`, `PointRect`, `AdvancedUILine` were already correct (`*31 +` / content-based, fixed under earlier findings).
XC-2. ✅ *Already resolved* under GEO-8 (`Polygon.Scale`) and UI-3 (`AdvancedUILine.Scale`) — both now assign the `Vector2.Scale` result back. Nothing left.
XC-3. **Editor drawer reflection** — `SetPropertyDrawer` uses public-only `GetProperty` binding, so private/protected setters silently never fire; it also reflects uncached.
XC-4. **`enumValueIndex` / mask handling for enums** — `EnumFlagsButtonGroupDrawer` (unmasked flag writes), `EnumButtonGroupDrawer` (unguarded `IndexOf` in the static `Draw`), `EnumFlagDrawer` (`int` truncation for `long` enums).
XC-5. **Unguarded `GetComponentInParent<Canvas>().rootCanvas`** across UI — a shared null-safe helper would fix the whole cluster.
XC-6. **Buggy custom HSV/HSB + easing/curve helpers** that duplicate Unity built-ins (`Color.RGBToHSV`, `AnimationCurve.EaseInOut`, `Collider.ClosestPoint`) — prefer the native APIs.
XC-7. **Struct `operator ==`/`Equals` doing `(object) == null` checks** (can never be null) — `Line`, `Line3D`, `PointRect`, `Point3`. (`Range` does NOT exhibit this — its `Equals` uses `obj is Range` — so it's excluded.)
XC-8. **Widespread commented-out dead code** (entire files: `RangeInt.cs`, `Polygon/Editor/LineEditor.cs`; large blocks in `BoundingSphere.cs`, `Line.cs`, `TouchInputSimulator.cs`, `Point3.cs`, the Text Effects folder, etc.).
XC-9. **Typos baked into public API names**: `CameraX` "frustrum" (~14 methods), "Decendent" folder/class, `PointRect`/`Grid` param docs.

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
- **CMP-19** `Render Texture Creator/RenderTextureCreator.cs` — editor game-view size now via `UnityEditor.Handles.GetMainGameViewSize()` (both it and the old `UnityStats.screenRes` are editor-only and live in the `#if UNITY_EDITOR` branch; builds still use `Screen.width/height`).
- **CMP-41** `BasePolygonRenderer.cs` — `GetMesh` reuse branch now compares `meshFilter.sharedMesh?.name` (was the GameObject/component `name`, so reuse never triggered).
- **MISC-1** `NoiseSampler/Editor/NoiseSamplerPropertyDrawer.cs` — "Create" assigns `new NoiseSampler()` (was `new SpringHandler(...)`).
- **MISC-2** `GLDebug/GLDebug.cs` — `matZOff` getter fixed to use `_matZOff`; `OnPostRender` line lists un-swapped (`matZOn`↔`linesZOn`, `matZOff`↔`linesZOff`).
- **MISC-4** `Version Control/VersionControlX.cs` — `GetGitBranch` strips `refs/heads/` (keeps slashes in branch names) with a last-slash fallback.
- **MISC-5** `Property Curve/PropertyCurve.cs` — `AddKey` inserts at the order-preserving index (`closestIndex+1` when the closest key is earlier) and returns after an exact-time replace (was inserting out of order / duplicating).
- **MISC-15** `GLDebug/GLDebug.cs` — `DrawCircle` connects consecutive points and closes the loop.
- **XC-1** `GetHashCode` — `Range` and `CameraProperties` now `hash = hash*31 + field` (were `hash *= field`, which collapses to 0 on any zero-hash field); `Polygon` now content-hashes its vertices to match `SequenceEqual`. (`SerializableTransform`/`Point`/`PointRect`/`AdvancedUILine` already correct.)
- **XC-2** — already resolved via GEO-8/UI-3 (both `Scale` methods assign the `Vector2.Scale` result back).
- ⚠️ **Not compile-verified in-editor** (community MCP down). Behaviour-preserving except the intended fixes; worth an in-editor glance: **CMP-1/CMP-41** (polygon mesh lifecycle/reuse), **CMP-14** (self-disable in edit mode), **MISC-2/MISC-15** (GLDebug visuals).
