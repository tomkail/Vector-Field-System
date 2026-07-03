# UnityX Code Review

Read-only review of every `.cs` file under `Assets/UnityX/Scripts/` (514 files, ~67.5k lines), fanned out across the whole tree. **Nothing was modified.** Findings are grouped per area, each into 5 categories: Bugs, Unity-native / .NET duplication, Refactoring / dead code, Misleading / incorrect comments, Tidying.

Paths are relative to `Assets/UnityX/`. Line numbers are approximate — treat as anchors, confirm before acting.


---

## Components / UI (`Scripts/Components/UI/`)

### Bugs
*(All UI bugs UI-1 … UI-28 fixed — see the `## ✅ Done` section.)*

### Unity-native duplication
UI-29. `UIGradient.cs:228-230` — `Mathf.Pow(x,2f)` for squaring; duplicates a magnitude/`Sqrt(dx*dx+dy*dy)`.
UI-30. `Line/AdvancedUILineRenderer.cs:177-198` — `AddQuad` reimplements `VertexHelper.AddUIVertexQuad`.
UI-31. `ExtendedScrollRect/ExtendedScrollRect.cs:85-119,199-213` — reimplements Unity's private `ScrollRect` methods (`InternalCalculateOffset`/`InternalGetContentBounds`); unavoidable since the originals are private. `GetAmountOfExcessMovement` reflects into the base's private `CalculateOffset` rather than the local copy, so the local `CalculateOffset()`/`InternalCalculateOffset` pair is unused in-project (public — may be external API).
UI-32. `UI Imposter/UIImposterRenderer.cs:237-272` & `WorldSpaceUIElement.cs:327-345` — hand-rolled bounds/rect builders. The `Vector3[]` AABB overload could be `Bounds.Encapsulate` (its `else if` min/max chain has a latent bug Encapsulate would fix); the 2D-`Rect` screen-space versions are justified (a 3D `Bounds` would drag in z).
UI-33. `SLayout/SLayoutProperty.cs:40-43` — `Lerp` reimplements `Mathf.LerpUnclamped`.
UI-34. `Draggable/MultitouchDraggable*.cs` — `sqrMagnitude == 1 ? direction : direction.normalized` redundant guard around `Vector2.normalized`.

### Refactoring / dead code
UI-35. `Extended Button/ExtendedSelectable.cs` — near-verbatim copy of `ExtendedButton.cs` (differs only in base class).
UI-36. `Line/AdvancedUILineRenderer.cs` vs `UILineRenderer.cs` — duplicated miter/bevel join block + duplicated `MIN_MITER_JOIN`/`MIN_BEVEL_NICE_JOIN` constants.
UI-37. `Outlines/Outline8.cs:7-27` — a special case fully subsumed by `BoxOutline`.
UI-38. `Line/UILineRenderer.cs:208-261` — `GetDrawingPoints0`/`GetDrawingPoints1` are two impls of the same routine.
UI-39. `Line/AdvancedUILine.cs` — dead helpers: `AddVert` (355), `PointInPolyFromIndex`/`GetIndexInPolyAtPoint`/`GetIndexInPolyLyingOnLineBetween` (323-353).
UI-40. `Line/AdvancedUILineRenderer.cs:175-176` — `vertexBuffer`/`indexBuffer` only referenced by commented code.
UI-41. `RoundRect/RoundRect.cs` — `AddMiddle` (503) never called; `_debugBool` unused; unused `corner1/2` (178); geometry methods take `vertices`/`indices` params but use fields instead.
UI-42. `ExtendedScrollRect/…` — `EnumToFlagValue` duplicated between editor and runtime.
UI-43. `Grid Layout/GridLayoutApplier.cs:48-69` — two branch blocks on same enum; placement math copy-pasted with `GridLayoutItem.cs:32-33`.
UI-44. `Grid Layout/GridLayout.cs:258-264` — `pivot` param threaded through but never used.
UI-45. `Grid Layout/Editor/GridLayoutEditor.cs:227-270` — fragile reflection `GetValueFromObject<T>` (self-admitted incomplete); prefer `boxedValue`.
UI-46. `SLayout/SLayoutAnimation.cs:17-26,108-143` — the parameterized ctor is unused in-project (public API, only referenced by commented-out lines); two near-identical `ThenAnimateInternal` overloads.
UI-47. `SLayout/SLayout.cs:70-159` — many `Animate` overloads duplicate the same block; only the AnimationCurve overload handles edit-mode `CompleteImmediate()` → inconsistent.
UI-48. `Draggable/MultitouchDraggableTouchSimulator.cs:61-97` — `ScaleAround`/`ScaleAroundRelative` duplicated verbatim from `MultitouchDraggable.cs:189-225`; in the simulator `ScaleAround` is the unused one (in `MultitouchDraggable` it's `ScaleAroundRelative`).
UI-49. `Draggable/Draggable.cs` — `dragVelocity` setter guard is a no-op. (`GetPosition` (protected virtual) and `distanceFromTarget` (public) are unused in-project but are public API.)
UI-50. `AbsoluteRectTransformController.cs:34-40` — both `Update()` and `LateUpdate()` call heavy `Refresh()`.
UI-51. `Background Blur UI/BackgroundBlurUI.cs:123,129` — double clamp; `Shader.Find` per access.
UI-52. `AlphaHitTestThresholdSetter.cs:9` — per-frame `GetComponent` in a getter.
UI-53. `Swipe View UI/SwipeView.cs:154` — `OnEnable` doesn't call `base.OnEnable()`. Harmless in practice (its base `UIBehaviour.OnEnable` is an empty stub) but deviates from Unity's convention.
UI-54. `SLayout/SLayout.cs:809-817` — `GetPivotPos` duplicates inline pivot math.

### Tidying
UI-68. `Background Blur UI/BackgroundBlurUI.cs:149` — double semicolon `new Color[size];;`.
UI-69. Unused usings: `UIMonoBehaviour.cs:2`, `MarkLayoutElementForRebuild.cs`, `SLayoutCanvasTimeScalar.cs`.
UI-70. `UIGradient.cs:435` — `System.Math.Abs` amid `Mathf`; nested enum `Type` collides with `System.Type`.
UI-71. `ExtendedCanvasScaler` — typo `scaleMultipler` (in field + serialized-property string).
UI-72. Commented-out blocks / mixed indentation: `ExtendedScrollRect.cs:17,299-355`; `GridLayout.cs:96-113`; `GridLayoutApplier.cs:17,53-57`; `Editor/GridLayoutEditor.cs:211-270`; `Line/Editor/UIAdvancedLineRendererEditor.cs:15,29-85` (+ `#pragma warning disable` with no restore); `Polygon/Editor/UIPolygonEditor.cs:96-97`.
UI-73. `Line/AdvancedUILineRenderer.cs:1-2` — redundant `UnityEngine.UI.Extensions` import.
UI-74. Typos: `Outlines/ModifiedShadow.cs:18` "neededCpacity"; `Polygon/UIPrimitiveBase.cs:66,250` "Resoloution"/"primatives"; `Line/UILineRenderer.cs` "Caluclates"/"inplementation".
UI-75. `RoundRectPolygonUI.cs:51-64` — every `[SerializeField]` commented out → fields non-serialized, stuck at -1.
UI-76. `Swipe View UI/*` — commented `OnDrawGizmos`, unused `static int targetPage`, empty disabled-group, commented button blocks.
UI-77. `WorldSpaceUIElement.cs:293` — `_OnDrawGizmos` (leading underscore → never called) dead.
UI-78. `SLayout/*` — commented ctor calls; `public static SLayoutAnimator _instance`; "the the" (`SLayoutCanvasTimeScalar.cs:20`); "simplied".

---

## Components (non-UI) (`Scripts/Components/`)

### Bugs
CMP-1. `PolygonRenderer/BasePolygonRenderer.cs:91-101` — `if(isPlaying)` nested inside `if(!isPlaying)` → unreachable branch; `DestroyMesh()` never runs in editor.
CMP-2. `PolygonRenderer/BasePolygonRenderer.cs:184-194` — `RecalculateColors` re-checks `colorMode == Shape` inside `else if(colorMode == Shape)` → inner check always true.
CMP-3. `Input/InputPoints/Mouse/MouseInputButton.cs:80` — `ToString()` prints `held` under the "Down" label.
CMP-4. `Input/InputX.cs:17-21` — `acceptInput` setter calls `ResetInput()` in both branches → branch is pointless.
CMP-5. `Region/Region.cs:274` — `ContainsPolygonSpacePoint3D` only rejects `z > height*0.5f`, never `z < -height*0.5f` → asymmetric slab.
CMP-6. `Input/Gestures/Pinch.cs:71-74` — normalizing a zero vector when a finger sits on the pinch center → NaN.
CMP-7. `Input/InputPoints/InputPoint.cs:118` — `UpdateDeltaMovement` both lerps and `+=` the delta → double-counts.
CMP-8. `Render Texture Creator/RenderTextureCreator.cs:88-110` — property mutations after `ReleaseRenderTexture()` but before `Create()` — verify ordering.
CMP-9. `Render Texture Creator/Editor/RenderTextureCreatorEditor.cs:32-35` — casts `objectReferenceValue` to `RenderTexture` + reads `.width/.height` with no null check.
CMP-10. `FPSManager/FPSManager.cs:15` — `1f/averageFPS` with no zero guard → Infinity before first sample.
CMP-11. `FPSManager/FPSManager.cs:95-99` — `RemoveOldDeltaTimes` bounds look off-by-one vs the accumulation loop.
CMP-12. `HideFlags/Editor/SetChildHideFlagsEditor.cs:21-23` — loops `targets` but always operates on the single primary `target` → multi-select edits only apply to one.
CMP-13. `GUIDrawer.cs:17-19` — enumerates the drawer dict in `OnGUI` while callbacks mutate it → "Collection was modified".
CMP-14. `ChangeCheckers/TransformChangeChecker.cs:36` — `enabled = false` set on play path but not edit path → asymmetric self-disable.
CMP-15. `Input/Gestures/Gesture.cs:17` — `CompleteGesture` sets `inputPoints = null` → post-completion subscribers can NRE. `TextMeshPro/TextBackgroundHighlightEffect.cs:16` — `text` is never auto-wired. `MonoInstancer.cs:29` — `_upToDate` is invalidated on play-mode/compile resets but not on scene open / hierarchy changes in edit mode, so the cached list can go stale on scene load.

### Unity-native duplication
CMP-16. `Region/Region.cs:269-271` — `SqrDistance` duplicates `(a-b).sqrMagnitude`.
CMP-17. `Region/Region.cs:431` — `Vector3.Normalize(...)` where `.normalized` is idiomatic.
CMP-18. `Input/InputUtils.cs:8-16` — hand-rolled pointer-over-UI overlaps `EventSystem.IsPointerOverGameObject()`.
CMP-19. `Render Texture Creator/RenderTextureCreator.cs:35-58` — string-parses `UnityStats.screenRes.Split('x')` for the editor game-view size. Fix: use `UnityEditor.Handles.GetMainGameViewSize()`, which returns the size directly (no locale-fragile parsing).

### Refactoring / dead code
CMP-20. `Events/TriggerListener.cs:81-154` — 12 collision/trigger handlers repeat the same block.
CMP-21. `ChangeCheckers/TransformChangeChecker.cs:43-72` — 5 change checks share one structure.
CMP-22. `ChangeCheckers/GameObjectChangeChecker.cs:22-41` — null-check boilerplate duplicated across `Update`/`OnDestroy`.
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
ED-4. `Icon/IconManager.cs` — relies on version-fragile internal reflection into `EditorGUIUtility.SetIconForObject`/`GetIconForObject` (`BindingFlags.NonPublic`).

### Unity-native duplication
ED-5. `Screenshot Exporter/ScreenshotCapturer.cs` — the multi-camera render-to-RT + `ReadPixels` path overlaps `ScreenCapture.CaptureScreenshotAsTexture` (though it adds per-camera selection the built-in lacks).
ED-6. `EditorTime/Editor/EditorTime.cs` — layers over `Time.realtimeSinceStartup` + `EditorApplication.update` to push a `_EditorTime` shader global; overlaps Unity's editor time.
ED-7. `Screenshot Exporter/ScreenshotSaverTextureFormat.cs` — enum+switch maps to `TextureFormat`; the actual PNG/JPG encoding (`EncodeToPNG`/`JPG`, i.e. `ImageConversion`) lives in `ScreenshotExportSettings`, not here.

### Refactoring / dead code
ED-8. `Icon/IconEnums.cs` + `IconManager.cs` — parallel enum/texture arrays indexed into reflection-loaded arrays; a dictionary would be cleaner.
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
PD-3. `SetProperty/Editor/SetPropertyDrawer.cs:38` — `type.GetProperty(attribute.Name)` uses public-only binding → private/protected setters are never found (silent no-op); the setter also fires deferred (via `IsDirty`), so it can act on a stale value.
PD-4. `Regex/Editor/RegexDrawer.cs` — when `attribute.regex` is null the pattern is compiled per-call with no try/catch → an invalid pattern throws on every repaint.
PD-5. `OnChange/Editor/OnChangeDrawer.cs` — the callback fires via `MonoBehaviour.Invoke(name)` before any explicit apply, so it can observe a pre-change value (and requires a no-arg method; won't run in edit mode).
PD-6. `EnumButtonGroup/Editor/EnumButtonGroupDrawer.cs:75` — the static `Draw` uses `Array.IndexOf(trueNames, names[i])` unguarded to index `typedValues[sortedIndex]` → throws on a stale/removed enum name.
PD-7. `EnumFlag/Editor/EnumFlagDrawer.cs:20` — writes to `property.intValue` via `(int)Convert.ChangeType(...)` → truncates for `long`/`ulong`-backed enums.
PD-8. `Info/Editor/InfoDrawer.cs` — help box uses a fixed `helpBoxHeight = 38` rather than measuring the text → long multi-line text clips.
PD-9. `PositionHandle/Editor/PositionHandleDrawer.cs` — the scene-handle write has no `Undo` recording and swallows exceptions in a bare `catch {}`.
PD-10. `HideInEditMode/Editor/HideInEditModeDrawer.cs` — returns `-EditorGUIUtility.standardVerticalSpacing` when hidden → negative height can overlap adjacent rows.

### Unity-native duplication
PD-11. `EnumFlag/Editor/EnumFlagDrawer.cs` — duplicates `EditorGUI.EnumFlagsField` / native `[Flags]` mask.
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
UEX-7. `CameraX.cs:38-40` — `WorldToViewportVector` uses reversed subtraction vs siblings → sign flip.
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
UEX-23. `OnGUIX.cs:97` — `DrawCircle` never closes the loop; `numPoints==1` → divide by zero.
UEX-24. `GizmosX.cs:277,301` — `DrawWireArc`/`DrawWireArcSegment` missing `return` after the degenerate circle draw.

### Unity-native duplication
UEX-25. `Color/HSVColor.cs` & `HSBColor.cs` — reimplement (buggily) `Color.RGBToHSV`/`HSVToRGB`.
UEX-26. `ColorX.cs` — `BlendMode.Normal` == `Color.Lerp`; additive/multiply at 1 == `+`/`*`; `Grayscale` == `Color.grayscale`; `RandomRGB` overlaps `Random.ColorHSV`.
UEX-27. `ColliderX.cs:11` — duplicates `Collider.ClosestPoint`/`ClosestPointOnBounds`.
UEX-28. `HashSetX.cs:5-9` — `AddRange` duplicates `HashSet.UnionWith`.
UEX-29. `LayerMaskX.cs:31-33,65-68` — `Includes` == `(mask & (1<<layer))!=0`; `Inverse` == `~mask`.
UEX-30. `RigidbodyX.cs:31-117` — `Get/SetForward/Up/Right/Back`, `Translate` are thin transform wrappers.
UEX-31. `UIBehaviourX.cs:6-14` — `GetRectTransform` == cast; `GetParentCanvas` == `GetComponentInParent<Canvas>()`.
UEX-32. `RectTransformX.cs:459-467` — `GetSize/Width/Height` trivial wrappers over `rect.*`.
UEX-33. `AnimationCurveX.cs:372-393` — `EaseInOut` is redundant with `AnimationCurve.EaseInOut` (both zero-tangent S-curves).
UEX-34. `Vector4X.cs:19-21` / `QuaternionX.cs:72-74` — duplicate implicit Vector4↔Quaternion conversion.
UEX-35. `SystemInfoX` — `IsMacOS`/`IsWinOS` use fragile string `.Contains`; use `Application.platform`.
UEX-36. `RandomX.cs:37` — `eulerAngle` == `Random.Range(0,360)`; `onUnitCircle` overlaps `Random.insideUnitCircle`.
UEX-37. `MeshRendererX.cs:6-9` — `SharedMaterialsContains` == `Array.IndexOf`.

### Refactoring / dead code
UEX-38. `Color/HSVColor.cs` & `HSBColor.cs` near-identical; both have an unreachable trailing `else`.
UEX-39. `CanvasGroupX.cs`/`CanvasX.cs` — `CanvasGroupsAllowInteraction`/`CanvasGroupsAlpha` duplicated verbatim; `GetRenderCamera` duplicated in `CanvasX`/`RectTransformX`.
UEX-40. `BoundsX.cs:21-76` — three copy-pasted `CreateEncapsulating` scans; `:111-169` — a 5-line face block repeated six times.
UEX-41. `RayX.cs:30-61,68-85` — two large commented-out method bodies (dead).
UEX-42. `ReflectionX.cs` — three near-identical `GetValueFromObject` overloads; `SetValueFromObject` largely dead.
UEX-43. `AnimationCurveX.cs:188-214` — `RemoveKeysBetween`/`RemoveKeysBetweenAndIncluding` byte-identical (one wrong per its name); `curve.keys` re-read in a loop (allocates).
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
UEX-65. Unused usings: `SpriteX.cs:2`, `SystemInfoX.cs:2`.
UEX-66. `DebugX.cs:16` — `debug=true` gates `LogError` too (errors suppressed when false).
UEX-67. `TextureX.cs:106` — `MonoBehaviour.print` instead of `Debug.LogWarning`.

---

## Extensions / Geometry (`Scripts/Extensions/Geometry/`)

### Bugs
GEO-1. `Point/PointRect.cs:265` — `Equals(PointRect)` compares `x==p.x && y==p.y && y==p.width && y==p.height` (uses `y` for width/height). `==` operator is correct → `==`/`Equals` disagree.
GEO-2. `Point/PointRect.cs:307-312` — `operator *` calls `Add`, `operator /` calls `Subtract`.
GEO-3. `Point/PointRect.cs:55-62` — `max` setter adds `value.x` on top of existing extents (asymmetric with getter and with the correct `min` setter).
GEO-4. `Polygon/StarPolygon.cs:89,100,104` — `new Vector2(Sin, Sin)` (should be `Sin, Cos`) → degenerate collinear polygons (3 occurrences).
GEO-5. `Polygon/StarPolygon.cs:71-78` — `Skip==1` path ignores `rotation`.
GEO-6. `Polygon/Polygon.cs:438-448` — `FindPointInDirection` sets `score = bestScore` instead of `bestScore = score` → returns the last vertex.
GEO-7. `Polygon/Polygon.cs:56-62` — `centroid` loop stops one edge early (never wraps) and uses `+= f*3` instead of `f` → doubly-wrong centroid (feeds `poleOfInaccessibility`).
GEO-8. `Polygon/Polygon.cs:204-227` — `Scale(Polygon)` and `Scale(Vector2)` discard `Vector2.Scale`'s return → no-ops (the `float` overload is correct).
GEO-9. `Polygon/Polygon.cs:346-348` — `GetVertexDegreesInternal` measures the angle of `rightDir−leftDir` vs up → not the interior angle the comment claims.
GEO-10. `Polygon/Polygon.cs:673-676` — `RayPolygonIntersection` caps `bestDistance` at `ray.magnitude` → rejects hits past distance 1 for a unit ray.
GEO-11. `Polygon/Polygon.cs:1155` — `CombinePolygons` uses raw `%` on a possibly-negative value where the rest of the file uses the `Mod` helper → can under/overshoot.
GEO-12. `Polygon/Polygon.cs:1539-1552` — `poleOfInaccessibility` caches only when magnitude==0 (never invalidated); `computePoleOfInaccessibility` inits `maxX/maxY` to `MaxValue` (works only via the `i==0` seed).
GEO-13. `Sphere/Sphere.cs:29,83` — `CreateFromBounds`/`CreateFromPoints` are instance methods that ignore `this` (should be static).
GEO-14. `Sphere/Sphere.cs:30` — radius = max extent doesn't enclose box corners (needs `extents.magnitude`).
GEO-15. `Sphere/Sphere.cs:113-114` — `CalculateWelzl` index arithmetic (`points[i+index]`, `points[index-1]`) → IndexOutOfRange for many inputs; looks broken/untested.
GEO-16. `Line/Line3D.cs:141` — `LineIntersectionPoint` solves only the XY projection, returns z=0 despite the 3D name.

### Unity-native duplication
GEO-17. `Point/Point.cs` — largely duplicates `Vector2Int`.
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
GEO-40. `Sphere/Sphere.cs:129` — `Debug.LogError("Should never get here")` (reachable given the bugs).
GEO-41. `Polygon/…PolygonEditorTool.cs:301` — `(i+1)%(vertices.Length-1)` off-by-one in edge-normal debug draw.
GEO-42. `Polygon/Polygon.cs:1433` — shared mutable `static List<int> tris` used by `GetRandomPointInPolygon` (not thread-safe).
GEO-43. `Point/PointRect.cs` — inconsistent namespacing: `Line`/`Polygon`/`Point`/`PointRect` are global while `Triangle`/`Sphere`/`RegularPolygon`/`StarPolygon` are in `UnityX.Geometry`.

---

## Extensions / Grid + UnityEditorX (`Scripts/Extensions/Grid/`, `Scripts/Extensions/UnityEditorX/`)

### Bugs
GRID-1. `Grid 3D/Grid3D.cs:150` — `ClampGridPoint` clamps z from `y` (`z = Clamp(y, minZ, maxZ)`).
GRID-2. `Grid 3D/Grid3D.cs:70,213,227` — forward `GridPointToArrayIndex` is x-major but inverse `ArrayIndexToGridPoint` is z-major → round-trips scatter values on non-cubic grids.
GRID-3. `Grid 3D/Point3.cs:98` — `ToString()` prints `"Z: " + y`.
GRID-4. `Grid 3D/Point3.cs:109-115` — `normalized` and `sqrMagnitude` return `1` (stubs); `magnitude` returns squared magnitude.
GRID-5. `Grid 3D/Point3.cs:189-198` — `operator ==` uses `ReferenceEquals`/null checks on a struct.
GRID-6. `Grid 2D/Map Types/Grid.cs:99` — `ArrayIndexToGridPoint` uses float `FloorToInt(index * reciprocal.x)` → off-by-one for large indices (static version at 321 is correct integer math).
GRID-7. `Grid 2D/Map Types/TypeMap.cs:185-213` — `SetValuesAtGridPosition` whole-number path lacks a `return` → falls through and overwrites with a `default(T)` bilinear splat.
GRID-8. `Grid 2D/Map Types/TypeMap.cs:279-287` — `GetTrimmed` builds an unused `heightMap`, discards `GetValueAtGridPosition` results, returns the untrimmed expanded map.
GRID-9. `Grid 2D/Mesh Generator/HeightMapMeshGenerator.cs:166` — skip-zero test indexes `[(z+1)*w + (z+1)]` (uses `z+1` as the column).
GRID-10. `Grid 2D/Mesh Generator/HeightMapMeshGenerator.cs:546-547,649-650` — edge vertex indexing mixes `sizeMinusOne.y`/`.x` → wrong cells / out of range on non-square maps.
GRID-11. `Grid 2D/Grid/GridRenderer/GridRenderer.cs:22` — `cellSize` third component uses `1f/gridSize.x` (suspicious copy-paste).
GRID-12. `Grid 2D/Map Types/Grid.cs:344`, `Grid3D.cs:255` — `Random.Range(0, 1)` (int overload) always returns 0.
GRID-13. `UnityEditorX/Editor/DeleteEmptyFolders.cs:149` — `.Select(!EndsWith(".meta")).Count()` counts all elements (== `GetFiles().Length`); `.meta` filter lost. Also `GetDirectories(path, string.Empty, …)` — empty pattern returns nothing on some platforms.
GRID-14. `UnityEditorX/HandlesX.cs:49-55` — `BeginMatrix` pushes `GUI.matrix` but assigns `Handles.matrix`; `EndMatrix` pops into `Handles.matrix` → corrupts state.
GRID-15. `UnityEditorX/Editor/UGroup.cs:178-180` — Ungroup derefs `parentObject.transform.parent` (null at scene root) → NRE; also uses the grandfather's sibling index (should be the parent's).
GRID-16. `UnityEditorX/Editor/SelectionX.cs:91,95` — mixes `objects` vs `gameObjects` in `Except` → spurious callbacks; `:128` — `activeObject` setter has no `else` → NRE on null.
GRID-17. `UnityEditorX/Editor/SerializedPropertyX.cs:160` — `Contains` uses reference equality on boxed objects → fails for value types.
GRID-18. `UnityEditorX/…/ScenePathDrawer.cs:57` — uses old `property.stringValue` instead of the newly-picked asset path.
GRID-19. `UnityEditorX/…/SceneDrawer.cs:46-48` — early-return "No Scenes" path leaves `BeginProperty` without `EndProperty`.

### Unity-native duplication
GRID-20. `UnityEditorX/EditorApplicationX.cs:29-31` — `CombinePaths` reimplements `Path.Combine` + normalization.
GRID-21. `UnityEditorX/AssetDatabaseX.cs:37-53` — `LoadAllAssetsAtPath(folder)` via `FindAssets("")` (non-idiomatic; prefer `FindAssets("t:Object", folders)`).
GRID-22. `Grid 2D/Map Types/HeightMap.cs:33-55` — `CalculateTotal/Average/Min/Max` wrap LINQ/`Mathf`; the parameterless `CalculateAverageHeight()` (37) is unused in-project (public — may be external API).
GRID-23. `UnityEditorX/PrimitiveHelper.cs` — standard trick; caches a mesh from a destroyed GO's `sharedMesh` (survives as built-in).

### Refactoring / dead code
GRID-24. `Grid 2D/Mesh Generator/HeightMapMeshGenerator.cs` — ~600 lines of near-identical externals/internals × triangles/quad copy-paste.
GRID-25. `UnityEditorX/Editor/EditorGUILayoutX.cs:266-414` — commented-out property path + dead `DrawPropertyViaReflection`.
GRID-26. `UnityEditorX/Editor/ExtendedScriptableObjectDrawer.cs:135-255` — `_GUILayout<T>`/`DrawScriptableObjectField<T>` near-identical; the latter's `if(isExpanded){}` body is empty (draws nothing).
GRID-27. `Grid 2D/Map Types/Vector2Map.cs:113-184` — two large commented-out operator blocks (one references non-existent fields).
GRID-28. `Grid 3D/Point3.cs:242-482` — ~240 lines of commented-out `Int3`.
GRID-29. `UnityEditorX/Scene Management/.../SceneDrawer.cs:71-107` + `ScenePathDrawer.cs:81-85` — commented-out `findMethod` + dead `SetSceneNumbers`/`GetSceneIndexes`.
GRID-30. `Grid 2D/Grid/SquareGridAgent.cs` & `RadialGridAgent.cs` — duplicated enter/exit diffing.
GRID-31. `Grid 2D/Map Types/Grid.cs:252-260`, `Grid3D.cs:203` — `Filter(Filter(list, IsOnGrid))` double-wrap (outer no-op copy).
GRID-32. `UnityEditorX/HandlesX.cs:66-149` — `DrawWheelHandle` commented-out `Handles.matrix` lines + `Debug.Log` remnants.

### Tidying
GRID-40. Leftover `DebugX.LogList(values)` on every `Resize`: `TypeMap.cs:233`, `TypeMap3D.cs:195`.
GRID-41. `UnityEditorX/Editor/HierarchyX.cs:9-26` — menu "Collapse All" doesn't collapse anything; dead reflection lookup (15).
GRID-42. `UnityEditorX/Editor/TransformEditorUtils.cs:106-117` — paste validator hotkey mismatch (`%&c` vs `%&v`).
GRID-43. Commented-out debug lines: `GridRenderer.cs:301,321-365`.
GRID-44. `UnityEditorX/Editor/ConsoleX.cs:7` — commented `[MenuItem]`; brittle reflection into `LogEntries`.
GRID-45. `Grid 3D/TypeMap3D.cs:18-19` — `Clear()` then re-allocates `values` (redundant double allocation).
GRID-46. `Grid 3D/TypeMap3D.cs:9-10` — `values` is `[NonSerialized]` here but serialized in 2D `TypeMap` (inconsistent → subclasses lose data).
GRID-47. `UnityEditorX/EditorApplicationX.cs:10` — `float.Parse(unityVersion.Substring(0,3))` for Retina detection (culture-dependent, stale `>= 5.4`).

---

## Extensions / Algorithms + Camera + Spline

### Bugs
ACS-1. `Spline System/SplineBezierPoint.cs:42-43,50-51,75-76` — `SetAuto`/`SetAutoDistance`/`CreateAuto` guard only the both-null case then unbox both nullables → NRE at either endpoint. `Spline.CreateFromPoints` throws for any real spline.
ACS-2. `Algorithms/Noise/NoiseSample.cs:67-71` — `operator /(float a, NoiseSample b)` computes `b.value / a` instead of `a / b.value` (wrong value + derivative).
ACS-3. `Algorithms/Noise/NoiseSample.cs:73-77` — `operator /(NoiseSample, NoiseSample)` derivative isn't the quotient rule → all division-derived analytic derivatives wrong.
ACS-4. `Camera/Camera Properties/CameraPropertiesModifier.cs:46` — Axis+Additive branch rotates by `properties.targetPoint` instead of `properties.axis`.
ACS-5. `Algorithms/UpscaleTools.cs:65-68,118-124` — `IsOnVisibilityMap` always returns true (bounds check commented) → IndexOutOfRange at right/bottom edges; the computed `fill` flag (115) is unused.
ACS-6. `Algorithms/UpscaleTools.cs:116` — `pointRect` extends to `4*size+2`, exceeding `colorMapSize` (`4*size`).
ACS-7. `Algorithms/Pathfinding/AStar.cs:297` — compares `GraphEntry` to `GraphElement` (no `Equals` override) → always false; target fast-path never fires.
ACS-8. `Algorithms/Pathfinding/AStar.cs:266` — async assert null-checks `solutionList` instead of `solutionList.solution` → NRE when no path found.
ACS-9. `Algorithms/Noise/SimplexNoiseGenerator.cs:11,45` — `contrast == 1` → `oneMinusContrast == 0` → divide by zero.
ACS-10. `Camera/Camera Properties/CameraModifierZone.cs:32` — `target.position` with no null check.

### Unity-native duplication
ACS-11. `Camera/Camera Properties/CameraProperties.cs:446-457` — `GetPitch/Yaw` hand-roll euler extraction vs `LookRotation(dir).eulerAngles`.
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

### Bugs
TXT-1. `Collections/IEnumerableX.cs:186-193` — `CompareSize` returns true on the first element whenever `targetSize >= 0`, false for empty → always wrong.
TXT-2. `Collections/IEnumerableX.cs:100-166` — `Min`/`Max<T>(selector)` return `-1` for empty (a legitimate value; can't distinguish empty).
TXT-3. `Serializable Components/SerializableTransform.cs:434-443` — `GetHashCode` multiplies → collapses to 0 if any component hash is 0.
TXT-4. `Text/TextMeshProUtils.cs:598-599` — `WorldToScreenRect` param order (`topLeft,bottomLeft,bottomRight,topRight`) mismatches all callers.
TXT-5. `Text/Text Effects/TextFader/TextRevealAnimatorCalculatedParams.cs:30` — `/(numCharacters-1)` → divide by zero for 1-char text.
TXT-6. `Text/Text Effects/TextFader/WorldSpaceTextGradient.cs:6,31` & `GradientArea.cs:19-24` — no null check on `gradientArea`/`gradient` under `[ExecuteInEditMode]`.
TXT-7. `Text/Text Effects/TextDuplicator.cs:12-22` — `duplicated` never assigned (commented at 14) → `CopyNonStyleProperties(..., null)` NRE every frame.
TXT-8. `Text/Text Effects/StackedTextEffectsController.cs:58-60` — non-TMP source leaves `text` null → NRE.
TXT-9. `Text/Text Effects/BaseTextMeshProEffect.cs:28` — `OnDisable` derefs `m_TextComponent` with no null check.
TXT-10. `Text/Text Effects/WordWobble.cs:26-51` & `CharacterWobble.cs:29-35` — index meshes by `vertexIndex`; break with rich-text tags / multiple spaces / changed text.
TXT-11. `Scene Management/Scene Set/RuntimeSceneSet.cs:144-151` — `IsIncludedInBuildSettings()` returns true when scenes are *missing* (inverted).
TXT-12. `Scene Management/…/RuntimeSceneSetLoadTask.cs:107-124` — `UnloadSoft` add-check inside the per-set loop → scene added once per non-containing set.
TXT-13. `Scene Management/…/RuntimeSceneSetLoader.cs:207-209` — cancelled branch passes `lastLoadTask` (cancelled) instead of the found task; `:311-312` — edit-mode broadcast sends to root every iteration, never to children.
TXT-14. `Scene Management/Scene Set/RuntimeSceneSet.cs:224-228` — `LoadInEditor()` `GetSceneAt(length-1)` → `-1` when empty.
TXT-15. `Scene Management/Scene Set/Editor/RuntimeSceneSetEditor.cs:64-65` — `sceneAssets`/`scenePaths` derefed with no null check.

### Unity-native / .NET duplication
TXT-16. `Collections/ListX.cs:8-10` — `ToList` reimplements `Enumerable.ToList`; `:28-39,112-133` — `First`/`Last`/`Contains`/`IndexOf` duplicate LINQ / `List<T>`.
TXT-17. `Collections/IEnumerableX.cs:9-14,16-23,394-416` — `ToHashSet`/`DistinctBy`/`Chunk` duplicate BCL (comment even says "remove when Unity upgrades to .NET 6"); `:206-216` — `Filter`/`Map` alias `Where`/`Select`.
TXT-18. `Text/Text Effects/WordWobble.cs:26-32` — manual `IndexOf(' ')` reimplements `string.Split`.
TXT-19. `Text/Text Effects/TextFader/GradientArea.cs:145-147` — `Clamp1Infinity` == `Mathf.Max(value, 1)`.
TXT-20. `Scene Management/Scene Set/RuntimeSceneSet.cs:145-172` — hand-rolled build-settings collection + manual array-grow vs LINQ/`List`.

### Refactoring / dead code
TXT-21. `Collections/ArrayX.cs:15-21` — `GetShiftedRepeating` exact duplicate of `ListX` (172-178).
TXT-22. `Collections/ShuffleBag.cs:82-105` — `Shuffle` duplicates `ListX.Shuffle`.
TXT-23. `Collections/ProbabilityList.cs:43` — needless `.ToArray()`; `:66-70` — non-generic `GetEnumerator()` yields `null`.
TXT-24. `Text/Text Effects/VertexWobble/CharacterWobble/WordWobble` — identical `Wobble` + scaffold copy-pasted.
TXT-25. `Text/Text Effects/WordHighlightTextEffect.cs:5-33` — whole body commented (dead class).
TXT-26. `Text/Text Effects/CurvedWorldTextEffect.cs:51` — TRS + inverse rebuilt per vertex.
TXT-27. `Text/Text Effects/TextFader/GradientArea.cs:58-165` — `CreateGradient` family + `Radians2Vector2`/`Degrees/RadiansBetween` all dead.
TXT-28. `Scene Management/…/RuntimeSceneSetLoader.cs:24-31` — `_debugLogging` field dead; `:259-285` — two near-identical `BroadcastMessageScene` overloads.

### Tidying
TXT-34. ~54 lines commented-out in `Text/TextMeshProUtils.cs:118-171` + scattered `Debug.Log`s; commented blocks in `SerializableCamera.cs:259-372`, `Audio/AudioClipX.cs:95-145`, `Audio/AudioPeer/*`, `Audio/SaveWav.cs:186`.
TXT-35. Extensive commented-out blocks + leftover template comments across `Text Effects/*` and the Scene Management loader.
TXT-36. Ungated raw `Debug.Log`s in `RuntimeSceneSetLoader.cs:94,102,145,168,174` (rest of file gates on `debugLogging`).
TXT-37. Doc typos: `RuntimeSceneSet.cs` "includesesd"/"includesed"; commented `// [SceneAttribute]` + redundant empty ctor.

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

### Unity-native / .NET duplication
SYS-9. `StringX.cs:5,33-41` — `IsWhiteSpace` == `string.IsNullOrWhiteSpace`; `Contains(string, StringComparison)` == BCL overload.
SYS-10. `EnumX.cs:14-147` — `Length<T>`/`IsValid`/`ToArray`/`GetEnumerable` duplicate `Enum.GetValues`/`Enum.IsDefined`.
SYS-11. `FlagsX.cs:33-56` — `SetFlag`/`UnsetFlag`/`HasFlag` reimplement `Enum.HasFlag` + bitwise ops.
SYS-12. `PathX.cs:9-11` — `GetFullPathWithoutExtension` == `Path.ChangeExtension(path, null)`.
SYS-13. `DirectoryX.cs:12-19` — duplicates `Path.GetRelativePath`.
SYS-14. `BoolX.cs:10-20` — `ToBool`/`ToInt` wrap `Convert.*`.
SYS-15. `SystemX.cs:10` — partially duplicates `EditorUtility.RevealInFinder` (comment notes this).

### Refactoring / dead code
SYS-16. `EnumX.cs:23-76` — `#if !UNITY_WINRT … if(!typeof(T).IsEnum) throw` copy-pasted 5× and dead (the `where T:Enum` constraint already guarantees it).
SYS-17. `EnumX.cs:87-101` — `ToArray<T>` adds nothing over `(T[])GetValues`; `GetEnumerable` boxes.
SYS-18. `FlagsX.cs:14-125` — two parallel families (raw-int vs generic enum) with overlapping duties + inconsistent naming; `:101-107` — `(int)Math.Pow(2,x)` vs `1<<x`.
SYS-19. `StringX.cs:67-114` — `Before`/`BeforeLast`/`AfterFirst`/`After` share structure but mix Ordinal vs culture-sensitive comparison.
SYS-20. `SystemX.cs:25-81` — `OpenInMacFileBrowser`/`OpenInWinFileBrowser` near-identical.

### Tidying
SYS-26. `EnumX.cs:37,49,61,75` — `throw new (...)` (target-typed `new` with no target type) — flagged as possibly non-compiling; likely only inert because inside the `#if !UNITY_WINRT` guard. **Worth confirming against the Unity console.**
SYS-27. `ByteFormatter.cs:8` — commented-out `FromToSize` stub.
SYS-28. `FlagsX.cs:167` — leftover `//yield return value;`.
SYS-29. Mixed tabs/spaces + stray blank lines: `StringX.cs`, `FlagsX.cs:108-110`.
SYS-30. `SystemX.cs:53,77` — `e.HelpLink = ""` "silence warning" hack.
SYS-31. `StringX.cs:119 vs 126` — `UppercaseFirstCharacter` is a `this`-extension but `LowercaseFirstCharacter` is a plain static.

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
STR-12. `Island/OutlineDetector.cs:16-17` — `found = true` set twice (outer is dead).

### Tidying
STR-16. `Island/OwnedIsland.cs:14-101` — ~88-line commented-out `OutlineSolver` block.
STR-17. `Island/OutlineDetector.cs:8,36-37,83,94-107` — multiple commented-out lines; `:100` — stray `;` after a `foreach`.
STR-18. `Shape.cs:41-89` — mixed tabs/spaces; a `TypeMap<bool>` local named "shape" collides with the returned `Shape` type.
STR-19. Unused usings in `Island/Island.cs`, `OwnedIslandDetector.cs`.

---

## Extensions / Spring (`Scripts/Extensions/Spring/`)

### Bugs
SPR-1. `Spring.cs:253` — undamped `SettlingDuration` divides by `-omegaZeta` (zero when `dampingRatio == 0`) → division by zero yielding `+Infinity`; the spring never settles.
SPR-2. `Spring.cs:327-331` — `CalculateTimeOfMaximumDisplacement` can return a spurious near-zero "peak" when initial velocity is 0.

### Unity-native duplication
SPR-3. `Spring.cs:145-150` — `Update(value, target, ref velocity)` intentionally mirrors `Mathf.SmoothDamp` (deliberate spring alternative, not accidental).

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
EAS-10. Unused usings: `QuaternionSmoothDamper.cs:4`, `Vector2/3SmoothDamper.cs:4`, `ColorSmoothDamper.cs:4`.
EAS-11. `Vector3SmoothDamper.cs:10` — ctor param named `target` but passed as `current`.

### Tidying
EAS-15. `FloatSmoothStepDamper.cs:77,83` — commented-out `// this.initial =`; `:96` — `var targetVelocity = 0` infers int.
EAS-16. Attribute-before-doc-comment ordering; mixed tabs/4-space; `[DisableAttribute]` vs `[Disable]` inconsistency.

---

## Extensions / Tween (`Scripts/Extensions/Tween/`)

### Bugs
TWN-1. `Types/Base/TypeTween.cs:242-243` — `Update` samples at the OLD time then advances the timer → value lags one frame (stale at start and just before completion).
TWN-2. `Types/Base/TypeTween.cs:242` — no clamp/overshoot check; with `Mathf.LerpUnclamped` (FloatTween) the value can overshoot if the timer returns >1 before `TweenComplete`.
TWN-3. `Types/QuaternionTween.cs:16-18` — `SetDeltaValue` computes `current * last` (a delta should be `current * Inverse(last)`).
TWN-4. `Types/Base/TypeTween.cs:279-281` — `GetValueAtTime` divides by `tweenTimer.targetTime` with no null/zero check (reachable via the zero-time path).
TWN-5. `Types/Base/TypeTween.cs:206-224` — zero-time branch fires `OnStart` then `OnComplete` in the same call.

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
RNG-3. `Range/Range.cs:173-174` — `RemoveRange` doesn't clamp `rangeToRemove` to `[min,max]` (fragile).
RNG-4. `Range/Range.cs:280-287` — `GetHashCode` multiplies → any zero endpoint (very common) → hash 0; also loses ordering.
RNG-5. `ValuePicker/Blender.cs:9,85` & `LogicBlender.cs:82` & `Selector.cs:167` — `previousValue.Equals(current)` NREs for reference `T` when previous is null.
RNG-6. `ValuePicker/LogicBlender.cs:45` — `Remove` uses `==` while `Set`/`TryGet` use `.Equals` → inconsistent equality.
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
MISC-1. `NoiseSampler/Editor/NoiseSamplerPropertyDrawer.cs:12` — "Create" button assigns `new SpringHandler(...)` to a `NoiseSampler` field (wrong type).
MISC-2. `GLDebug/GLDebug.cs:55-66` — `matZOff` getter is a copy of `matZOn` (tests/assigns `_matZOn`, loads the ZOff shader into `_matZOn`); `:108-116` — `linesZOff` drawn with `matZOn` and vice-versa (swapped).
MISC-3. `Texture Transform Utils/TextureTransformUtil.cs:122-123` — `FlipVertical` case has an empty body → the Graphics path performs no flip.
MISC-4. `Version Control/VersionControlX.cs:53-54` — `GetGitBranch` returns `Substring(LastIndexOf("/")+1)` → drops everything before the last `/` for slash-containing branch names.
MISC-5. `Property Curve/PropertyCurve.cs` — `AddKey` inserts at `ClosestIndexToTime(time)` (the floor index) without the needed `+1` when `keys[closestIndex].time < newKey.time` → keys can end up out of order (e.g. inserting time 1 into `[0,2,4]` yields `[1,0,2,4]`).

### Unity-native duplication
MISC-6. `Property Curve/PropertyCurve.cs` — reimplements `AnimationCurve`/`Keyframe`/`WrapMode` (justified only by being generic over `T`).

### Refactoring / dead code
MISC-7. `NoiseSampler/Editor/NoiseSamplerPropertiesPropertyDrawer.cs:18-124` — `OnGUI`/`Draw` almost identical; three near-identical `DrawNoiseGraph` overloads.
MISC-8. `Texture Transform Utils/TextureTransformUtil.cs` — two parallel pipelines (blit vs `Graphics.DrawTexture`) with heavy copy-paste; `CopyWithSizeAndImageOrientation2` unhelpfully named.
MISC-9. `GLDebug/GLDebug.cs:205-307` — `DrawSquare`/`DrawCube` overload triplets are repetitive boilerplate.

### Tidying
MISC-14. Commented-out lines: `NoiseSamplerPropertyDrawer.cs:32-52`, `NoiseSamplerPropertiesPropertyDrawer.cs:151-336`, `GLDebug.cs:50,62`, `MeshBuilder/AddPlaneParams.cs:45-50`, `FlexLayout/FlexLayout.cs:45`, `Version Control/Editor/VersionBuildPreProcessor.cs:34-44`.
MISC-15. `GLDebug/GLDebug.cs:311-319` — `DrawCircle` draws each point as a 0.02-unit stub rather than connecting points (renders a dotted ring, not a circle).
MISC-16. Inconsistent indentation in the nested `GraphGUI` class (`NoiseSamplerPropertiesPropertyDrawer.cs:163-336`).
MISC-17. `NoiseSamplerPropertiesPropertyDrawer.cs:8` — `graphXRange` is `static` but effectively const.

*Verified non-findings:* `FlexLayout` margin accounting is self-consistent (no double-count); `StateMachine.GetStatesInheriting<R>` is loosely typed but not a concrete bug.

---

## Cross-cutting themes (worth a single sweep)

XC-1. **`GetHashCode` by multiplication / reference hash** — `Range`, `SerializableTransform`, `Polygon`, `AdvancedUILine`, `CameraProperties`, `Point`/`PointRect`. Fix to `hash = hash*prime + field.GetHashCode()` and make `Equals`/`GetHashCode` consistent.
XC-2. **`Vector2.Scale` / `Vector3.Scale` return value discarded** — `Polygon.Scale`, `AdvancedUILine.Scale` (both silent no-ops).
XC-3. **Editor drawer reflection** — `SetPropertyDrawer` uses public-only `GetProperty` binding, so private/protected setters silently never fire; it also reflects uncached.
XC-4. **`enumValueIndex` / mask handling for enums** — `EnumFlagsButtonGroupDrawer` (unmasked flag writes), `EnumButtonGroupDrawer` (unguarded `IndexOf` in the static `Draw`), `EnumFlagDrawer` (`int` truncation for `long` enums).
XC-5. **Unguarded `GetComponentInParent<Canvas>().rootCanvas`** across UI — a shared null-safe helper would fix the whole cluster.
XC-6. **Buggy custom HSV/HSB + easing/curve helpers** that duplicate Unity built-ins (`Color.RGBToHSV`, `AnimationCurve.EaseInOut`, `Collider.ClosestPoint`) — prefer the native APIs.
XC-7. **Struct `operator ==`/`Equals` doing `(object) == null` checks** (can never be null) — `Line`, `Line3D`, `PointRect`, `Point3`, `Range`.
XC-8. **Widespread commented-out dead code** (entire files: `RangeInt.cs`, `Polygon/Editor/LineEditor.cs`; large blocks in `BoundingSphere.cs`, `Line.cs`, `TouchInputSimulator.cs`, `Point3.cs`, the Text Effects folder, etc.).
XC-9. **Typos baked into public API names**: `CameraX` "frustrum" (~14 methods), "Decendent" folder/class, `PointRect`/`Grid` param docs.

---

## ✅ Done (branch `unityx-updates`)

Completed findings, moved out of the sections above. IDs are the original finding IDs (stable). Notes call out anything noteworthy discovered during implementation.

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

### Island detector — iterative rewrite
- **STR-1 / STR-2** — hang fixed. Both detectors now walk a fixed collection and *pop* seeds (`foreach` / `Queue.Dequeue`) instead of peeking `[0]`; an island is created only for a valid seed, so no empty `OwnedIsland`s.
- **STR-3** — the dead "re-add already-connected point" branch is gone (replaced by the shared `FloodFill`). Residual `static`-collection reentrancy is tracked by STR-11.
- **STR-7** — recursion replaced by an explicit `Stack<Coord>` → no stack-overflow risk on large islands.
- **STR-9** — removed the redundant `this.GetAdjacentPoints = GetAdjacentPoints` in the owned ctor (base already sets it).
- **STR-10** — the `*WithSameOwner` copy-paste is gone; the owned detector reuses base `FloodFill` via a `canJoin` predicate + an `onValidSkip` callback that re-seeds differently-owned neighbours (preserving the discover-adjacent-owner-regions behaviour).
- **STR-20** — `List` work-set (O(n²) at grid scale) replaced by `HashSet` membership + a local `Stack`/`Queue` → O(n).
- ⚠️ Behavioural rewrite; **not compile-verified** (the community "MCP for Unity" server is down). Public API (ctors + `FindIslands()` signatures) is unchanged.
