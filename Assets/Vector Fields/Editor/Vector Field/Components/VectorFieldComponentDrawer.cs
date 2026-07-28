using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEditor;

namespace VectorFields {
    [InitializeOnLoad]
    public static class VectorFieldComponentDrawer
    {
        // One renderer per component, reused across frames so GPU buffers aren't reallocated (and leaked) every repaint.
        static readonly Dictionary<VectorFieldComponent, VectorFieldDebugRenderer> renderers = new();
        static readonly HashSet<VectorFieldComponent> drawnThisFrame = new();
        static readonly HashSet<VectorFieldComponent> toDraw = new();
        static readonly List<VectorFieldComponent> stale = new();

        static VectorFieldComponentDrawer()
        {
            // The instanced arrow draw is a one-frame persistent draw, so it must be (re)issued right before the camera
            // that shows it renders. Under a Scriptable Render Pipeline (URP) that moment is
            // RenderPipelineManager.beginCameraRendering. IMGUI's SceneView.duringSceneGui runs AFTER the SRP has already
            // rendered the camera, so issuing the draw there is a frame late and flickers on zoom/pan. Built-in RP has no
            // per-camera callback and composites IMGUI differently, so it keeps using duringSceneGui. Exactly one path is
            // active, chosen by the current pipeline (see the guards in each handler).
            SceneView.duringSceneGui += OnSceneGui;
            RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
            UnityEditor.AssemblyReloadEvents.beforeAssemblyReload += OnAssemblyReload;
            Selection.selectionChanged += SceneView.RepaintAll;
        }

        static void OnAssemblyReload() {
            foreach (var renderer in renderers.Values) renderer.Dispose();
            renderers.Clear();
        }

        // Built-in render pipeline only: draw during the scene GUI's Repaint.
        static void OnSceneGui(SceneView sceneView) {
            if (GraphicsSettings.currentRenderPipeline != null) return;   // an SRP is active — OnBeginCameraRendering handles it
            if (Event.current.type != EventType.Repaint) return;          // one draw per render, not per IMGUI event
            DrawSelected(sceneView.camera);
        }

        // Scriptable Render Pipeline (URP): fires per camera immediately before it renders — the correct time to register
        // the instanced draw so it lands in that exact render (no one-frame lag, no flicker).
        static void OnBeginCameraRendering(ScriptableRenderContext context, Camera camera) {
            if (GraphicsSettings.currentRenderPipeline == null) return;   // built-in RP — OnSceneGui handles it
            if (camera.cameraType != CameraType.SceneView) return;        // scene-view arrows only
            DrawSelected(camera);
        }

        // Issue the arrow draw for every selected, gizmo-enabled vector field into the given camera, and release renderers
        // for components no longer selected.
        static void DrawSelected(Camera camera) {
            drawnThisFrame.Clear();

            // Collect the fields to draw first (a set, so a group shared by several selected children is only drawn
            // once). Normally each selected field is drawn. When "Show parent group" is on and a selected field has a
            // drawable ancestor group, that group's combined output is drawn *instead* of the field — the two overlap
            // too much to read together, and the group already shows the field's contribution in context.
            toDraw.Clear();
            bool showParentGroup = VectorFieldDebugSettings.ShowParentGroup;
            foreach (var obj in Selection.objects) {
                GameObject go = obj as GameObject;
                if (go == null) continue;

                var component = go.GetComponent<VectorFieldComponent>();
                if (!IsDrawable(component)) continue;

                var group = showParentGroup ? GetParentGroup(component) : null;
                toDraw.Add(group != null && IsDrawable(group) ? group : component);
            }

            foreach (var component in toDraw) {
                if (!renderers.TryGetValue(component, out var renderer)) {
                    renderer = new VectorFieldDebugRenderer();
                    renderers[component] = renderer;
                }
                renderer.Draw(component, camera,
                    VectorFieldProjectSettings.instance.appearance,
                    VectorFieldDebugSettings.VariableResolution ? VectorFieldArrowResolutionMode.Adaptive : VectorFieldArrowResolutionMode.Native,
                    VectorFieldDebugSettings.TargetSpacingPixels,
                    VectorFieldDebugSettings.MaxArrows,
                    0); // fixedResolution unused for Native/Adaptive; the Scene-view overlay has no Fixed mode
                drawnThisFrame.Add(component);
            }

            // Release renderers for components that are no longer selected (or were destroyed).
            if (renderers.Count > drawnThisFrame.Count) {
                stale.Clear();
                foreach (var kvp in renderers)
                    if (!drawnThisFrame.Contains(kvp.Key)) stale.Add(kvp.Key);
                foreach (var component in stale) {
                    renderers[component].Dispose();
                    renderers.Remove(component);
                }
            }
        }

        // A field is drawable when it's present, active, and its gizmo hasn't been switched off in the Gizmos menu.
        static bool IsDrawable(VectorFieldComponent component) {
            if (component == null || !component.isActiveAndEnabled) return false;
            if (GizmoUtility.TryGetGizmoInfo(component.GetType(), out GizmoInfo info) && !info.gizmoEnabled) return false;
            return true;
        }

        // The nearest ancestor group a field lives under (excluding the field itself), or null if it isn't inside one.
        // Searches from the parent so a selected group returns its own container, not itself.
        public static GroupVectorFieldComponent GetParentGroup(VectorFieldComponent component) {
            if (component == null || component.transform.parent == null) return null;
            return component.transform.parent.GetComponentInParent<GroupVectorFieldComponent>();
        }

        // True when any selected field sits inside a group — i.e. "Show parent group" has something to act on.
        public static bool SelectionHasParentGroup() {
            foreach (var obj in Selection.gameObjects) {
                var component = obj.GetComponent<VectorFieldComponent>();
                if (component != null && GetParentGroup(component) != null) return true;
            }
            return false;
        }
    }
}
