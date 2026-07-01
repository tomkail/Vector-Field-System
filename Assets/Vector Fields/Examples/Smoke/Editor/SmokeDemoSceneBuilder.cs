using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Builds the coloured-smoke demo scene from scratch (menu: Tools > Vector Field > Build Smoke Demo Scene).
//
// A reproducible, in-repo builder: run it and it creates + wires the whole setup and saves the scene. Lives inside the
// deletable Examples/Smoke folder, so removing the demo removes this too.
//
// Wiring it produces:
//   Wind Source  — NoiseVectorFieldComponent, a static swirly force field
//   Fluid Sim    — SimulatedVectorFieldComponent, forced by the wind (the air currents)
//   Smoke        — SmokeSimulationComponent (velocitySource = Fluid Sim) + SmokeMousePainter
//   Demo Camera  — framed on the field plane (XY, normal +Z)
//
// All three fields sit at the origin on the same plane, scaled to the same world extent so the smoke rides the fluid
// one-to-one. Enter Play and drag the mouse to paint smoke; the fluid then blows it around.
public static class SmokeDemoSceneBuilder {
    const string ScenePath = "Assets/Vector Fields/Examples/Smoke/SmokeDemo.unity";

    // World size (units) of each field's plane. The fields span [-FieldWorldSize/2, +FieldWorldSize/2] on X and Y.
    const float FieldWorldSize = 50f;
    const int GridResolution = 64;

    [MenuItem("Tools/Vector Field/Build Smoke Demo Scene")]
    public static void Build() {
        if (!EditorUtility.DisplayDialog("Build Smoke Demo Scene",
                $"This creates a new scene and saves it to:\n{ScenePath}\n\nUnsaved changes in the current scene will be lost. Continue?",
                "Build", "Cancel"))
            return;
        BuildScene(save: true);
    }

    // The actual construction, with no blocking dialog — callable headlessly (e.g. over the editor MCP) as well as from
    // the menu item above.
    public static Scene BuildScene(bool save) {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        // --- Camera: look at the XY plane face-on (the fields' normal is +Z at identity rotation) ------------------
        var camGo = new GameObject("Demo Camera");
        var cam = camGo.AddComponent<Camera>();
        camGo.tag = "MainCamera";
        camGo.transform.SetPositionAndRotation(new Vector3(0f, 0f, 30f), Quaternion.LookRotation(Vector3.back, Vector3.up));
        cam.orthographic = true;
        cam.orthographicSize = FieldWorldSize * 0.55f;     // frame the field with a little margin
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = Color.black;
        cam.nearClipPlane = 0.1f;
        cam.farClipPlane = 200f;

        // Each field is built INACTIVE, then configured, then activated. Adding a VectorFieldComponent / smoke component
        // fires its OnEnable immediately, which allocates GPU textures sized to gridSize — so the grid must be set up
        // before the object goes live, or the first allocation happens at 0×0 and throws.

        // --- Wind source: a noise field that continuously forces the fluid -----------------------------------------
        var windGo = new GameObject("Wind Source");
        windGo.SetActive(false);
        var noise = windGo.AddComponent<NoiseVectorFieldComponent>();
        ConfigureFieldTransform(windGo);
        TrySetNoise(noise);
        windGo.SetActive(true);

        // --- Fluid sim: the air currents, driven by the wind -------------------------------------------------------
        var fluidGo = new GameObject("Fluid Sim");
        fluidGo.SetActive(false);
        var fluid = fluidGo.AddComponent<SimulatedVectorFieldComponent>();
        ConfigureFieldTransform(fluidGo);
        fluid.forceField = noise;
        fluid.forceStrength = 1f;
        fluid.timeScale = 20f;              // visible flow speed at a stable 60fps step rate
        fluid.viscosityDamp = 1f;
        fluid.vorticityStrength = 0.3f;     // keep it lively
        fluidGo.SetActive(true);

        // --- Smoke: rides the fluid velocity, painted with the mouse -----------------------------------------------
        var smokeGo = new GameObject("Smoke");
        smokeGo.SetActive(false);
        var smoke = smokeGo.AddComponent<SmokeSimulationComponent>();
        ConfigureFieldTransform(smokeGo);
        smoke.velocitySource = fluid;
        smoke.velocityScale = 8f;
        smoke.dissipationPerSecond = 0.7f;
        smoke.tint = Color.white;
        var painter = smokeGo.AddComponent<SmokeMousePainter>();
        painter.cam = cam;
        smokeGo.SetActive(true);

        Selection.activeGameObject = smokeGo;
        EditorSceneManager.MarkSceneDirty(scene);
        if (save) {
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorGUIUtility.PingObject(AssetDatabase.LoadMainAssetAtPath(ScenePath));
        }
        Debug.Log($"[SmokeDemo] Built {ScenePath}. Enter Play and drag in the view to paint smoke; the fluid blows it around.");
        return scene;
    }

    // Every field (noise / fluid / smoke) sits at the origin, unrotated, scaled so its grid fills the same world area.
    // The components configure their own GridRenderer (Manhattan mode, scaleWithGridSize = false, gridSize); we only set
    // the transform scale, which they don't touch — that's what maps the normalized grid to FieldWorldSize world units.
    static void ConfigureFieldTransform(GameObject go) {
        go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        go.transform.localScale = Vector3.one * FieldWorldSize;
        var gr = go.GetComponent<GridRenderer>();
        if (gr != null) {
            if (gr.modeModule is not GridRendererManhattanModeModule)
                gr.modeModule = ScriptableObject.CreateInstance<GridRendererManhattanModeModule>();
            gr.scaleWithGridSize = false;
            gr.gridSize = new Point(GridResolution, GridResolution);
        }
    }

    // The noise force field only pushes the fluid if it has a non-zero frequency. Set sensible values via the
    // serialized object (works whether NoiseSampler is a class or struct, and marks the change dirty), guarded so a
    // future rename of these fields just no-ops instead of throwing.
    static void TrySetNoise(NoiseVectorFieldComponent noise) {
        var so = new SerializedObject(noise);
        SetIfPresent(so, "noiseSampler.properties.frequency", 0.12f);
        SetIfPresent(so, "noiseSampler.properties.persistence", 0.5f);
        SetIfPresent(so, "noiseSampler.properties.lacunarity", 2f);
        SetIfPresent(so, "noiseSampler.properties.octaves", 3f);
        SetIfPresent(so, "vortexAngle", 90f);   // curl-ish swirl so the fluid gets rotational forcing
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    static void SetIfPresent(SerializedObject so, string path, float value) {
        var p = so.FindProperty(path);
        if (p != null && p.propertyType == SerializedPropertyType.Float) p.floatValue = value;
    }
}
