using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

[InitializeOnLoad]
public static class VectorFieldComponentDrawer
{
    // One renderer per component, reused across frames so GPU buffers aren't reallocated (and leaked) every repaint.
    static readonly Dictionary<VectorFieldComponent, VectorFieldDebugRenderer> renderers = new();
    static readonly HashSet<VectorFieldComponent> drawnThisFrame = new();
    static readonly List<VectorFieldComponent> stale = new();

    static VectorFieldComponentDrawer()
    {
        SceneView.duringSceneGui += OnSceneGUI;
        UnityEditor.AssemblyReloadEvents.beforeAssemblyReload += OnAssemblyReload;
        Selection.selectionChanged += SceneView.RepaintAll;
    }

    static void OnAssemblyReload() {
        foreach (var renderer in renderers.Values) renderer.Dispose();
        renderers.Clear();
    }

    private static void OnSceneGUI(SceneView sceneView) {
        // duringSceneGui fires for every event (Layout, mouse, Repaint...). Issuing the instanced draw on more than
        // one of them stacks transparent draws in the same render, doubling the opacity (the flicker on zoom/pan).
        // Draw only on Repaint so each scene view renders the arrows exactly once.
        if (Event.current.type != EventType.Repaint) return;

        drawnThisFrame.Clear();
        foreach (var obj in Selection.objects) {
            GameObject go = obj as GameObject;
            if (go == null) continue;

            var component = go.GetComponent<VectorFieldComponent>();
            if (component == null || !component.isActiveAndEnabled) continue;
            if (GizmoUtility.TryGetGizmoInfo(component.GetType(), out GizmoInfo info) && !info.gizmoEnabled) continue;

            if (!renderers.TryGetValue(component, out var renderer)) {
                renderer = new VectorFieldDebugRenderer();
                renderers[component] = renderer;
            }
            renderer.Draw(component, 1, sceneView.camera,
                VectorFieldDebugSettings.VariableResolution,
                VectorFieldDebugSettings.TargetSpacingPixels,
                VectorFieldDebugSettings.MaxArrows);
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
}
