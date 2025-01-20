using UnityEngine;
using UnityEditor;

[InitializeOnLoad]
public static class VectorFieldComponentDrawer
{
    static VectorFieldComponentDrawer()
    {
        SceneView.duringSceneGui += OnSceneGUI;
        //EditorApplication.update += OnUpdate;
        UnityEditor.AssemblyReloadEvents.beforeAssemblyReload += OnAssemblyReload;
    }

    static void OnAssemblyReload() {
        // DISPOSE HERE
    }

    private static void OnSceneGUI(SceneView sceneView) {
        foreach (var obj in Selection.objects) {
            GameObject go = obj as GameObject;
            if (go != null) {
                var component = go.GetComponent<VectorFieldComponent>();
                if (component == null || !component.isActiveAndEnabled) return;
                if (GizmoUtility.TryGetGizmoInfo(component.GetType(), out GizmoInfo info)) 
                    if (!info.gizmoEnabled) continue;

                var renderer = new VectorFieldDebugRenderer();
                renderer.Draw(component, 1, 1, sceneView.camera);
            }
        }
    }
}