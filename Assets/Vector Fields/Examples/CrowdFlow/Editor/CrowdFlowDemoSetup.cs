using UnityEditor;
using UnityEngine;
using CrowdFlow;
using VectorFields;

namespace CrowdFlow.EditorTools {
    /// <summary>
    /// One-click builder for the Crowd Flow demo. Terrain scenes can't sensibly be hand-authored, so this menu item
    /// assembles the whole thing into the active scene: a procedurally sculpted terrain (two hills + a lake basin), an
    /// obstacle layer, the <see cref="CrowdFlowManager"/> / <see cref="CrowdDirector"/> / <see cref="WorldEditor"/>,
    /// three colour-coded <see cref="Attraction"/>s, best-effort ground flow-visualisation fields, a water plane, a
    /// camera and a light. Press Play, then sculpt (keys 1–4) and watch the crowd re-route.
    /// </summary>
    public static class CrowdFlowDemoSetup {
        const int GridRes = 128;
        const float TerrainWorld = 200f;   // XZ size (square)
        const float TerrainHeight = 30f;
        const float WaterWorldY = 6f;

        [MenuItem("Vector Fields/Examples/Create Crowd Flow Demo")]
        public static void Build() {
            int obstacleLayer = EnsureLayer("CrowdObstacle");

            var terrain = BuildTerrain(out Terrain terrainComp);
            BuildWaterPlane();
            BuildLight();
            BuildCamera();

            // Manager root (also holds the director + editor + attractions as children).
            var root = new GameObject("CrowdFlow");
            var mgr = root.AddComponent<CrowdFlowManager>();
            mgr.terrain = terrainComp;
            mgr.resolution = GridRes;
            mgr.waterLevel = WaterWorldY;
            mgr.maxWalkableSlopeDeg = 42f;
            mgr.slopeCostScale = 0f;   // slope costs through speed, not a separate penalty
            // Slope ↔ speed: one model drives both the route cost (= travel time) and the agents' pace.
            mgr.uphillSlow = 3f;
            mgr.downhillSpeedup = 0.6f;
            mgr.minSpeedMul = 0.15f;
            mgr.maxSpeedMul = 1.6f;
            mgr.blockedMask = 1 << obstacleLayer;

            var dir = root.AddComponent<CrowdDirector>();
            dir.agentCount = 250;
            dir.moveSpeed = 6f;
            dir.agentSize = 3f;   // big enough to read clearly from the overview camera
            // Flocking: separation-dominant so visitors keep personal space and spread out; light alignment for
            // natural streams; cohesion off (the flow field already groups them toward attractions).
            dir.separationRadius = 1.8f;
            dir.separationWeight = 5f;
            dir.neighborRadius = 3.5f;
            dir.alignmentWeight = 0.5f;
            dir.cohesionWeight = 0f;

            var editor = root.AddComponent<WorldEditor>();
            editor.manager = mgr;
            editor.obstacleLayer = obstacleLayer;

            // Flow-map arrow visualisation, draped over the terrain (toggle with the HUD / V key).
            var arrows = root.AddComponent<FlowArrowVisualizer>();
            arrows.manager = mgr;
            arrows.terrain = terrainComp;
            // The built-in arrow glyph lives in an Editor folder, so assign it explicitly (Resources.Load fails at runtime).
            arrows.arrowTexture = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Vector Fields/Editor/Debug Renderer/Arrows/Debug Arrow 5.png");

            // Attractions + their visualisation fields.
            var quad = BuiltinQuad();
            Vector3[] spots = {
                new Vector3(-70f, 0f, -70f),
                new Vector3( 72f, 0f, -55f),
                new Vector3(  0f, 0f,  74f),
            };
            Color[] cols = { new Color(1f, 0.4f, 0.35f), new Color(0.4f, 0.85f, 0.45f), new Color(0.45f, 0.65f, 1f) };

            for (int i = 0; i < spots.Length; i++) {
                Vector3 p = spots[i];
                p.y = terrainComp.SampleHeight(p) + terrainComp.transform.position.y;
                var aGo = new GameObject("Attraction_" + (i + 1));
                aGo.transform.SetParent(root.transform, true);
                aGo.transform.position = p;
                var att = aGo.AddComponent<Attraction>();
                att.color = cols[i];
                att.arriveRadius = 3f;

                var marker = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                marker.name = "Marker";
                marker.transform.SetParent(aGo.transform, false);
                marker.transform.localScale = new Vector3(3f, 2f, 3f);
                marker.transform.localPosition = Vector3.up * 2f;
                Object.DestroyImmediate(marker.GetComponent<Collider>());
                TintPrimitive(marker, cols[i]);

                BuildBeacon(aGo.transform, cols[i]);

                var vf = BuildVisualField(root.transform, i, quad, cols[i], onlyThisRenders: i == 0);
                mgr.destinations.Add(new CrowdFlowManager.Destination { attraction = att, visualField = vf });
            }

            BuildWind(terrainComp);

            Selection.activeGameObject = root;
            EditorUtility.SetDirty(root);
            Debug.Log("Crowd Flow demo built. Press Play. Brushes 1-4 (LMB paint / RMB inverse), [ ] brush size, " +
                      "G reset, V flow map. Camera: WASD pan, scroll zoom, Q/E rotate, R/F tilt.");
        }

        static GameObject BuildTerrain(out Terrain terrainComp) {
            var td = new TerrainData { heightmapResolution = 129 };
            td.size = new Vector3(TerrainWorld, TerrainHeight, TerrainWorld);
            int res = td.heightmapResolution;
            var h = new float[res, res];
            for (int z = 0; z < res; z++) {
                float nz = z / (res - 1f);
                for (int x = 0; x < res; x++) {
                    float nx = x / (res - 1f);
                    float hill1 = Gauss(nx, nz, 0.32f, 0.34f, 0.12f) * 0.55f;
                    float hill2 = Gauss(nx, nz, 0.70f, 0.62f, 0.15f) * 0.75f;
                    float lake = -Gauss(nx, nz, 0.5f, 0.82f, 0.12f) * 0.55f;   // basin that fills with water
                    float land = 0.32f + hill1 + hill2 + lake;
                    // Island falloff: pull the terrain down to (below) the waterline toward the map edges so the level
                    // reads as an island ringed by sea, with a beach where the land crosses the shoreline.
                    float edge = Mathf.Min(Mathf.Min(nx, 1f - nx), Mathf.Min(nz, 1f - nz));   // 0 at border -> 0.5 centre
                    float e = Mathf.Clamp01(edge / 0.20f);                                     // 0 at border, 1 by 20% in
                    float island = e * e * (3f - 2f * e);                                     // smoothstep ramp (0 -> 1)
                    h[z, x] = Mathf.Clamp01(land * island);
                }
            }
            td.SetHeights(0, 0, h);

            // Persist the TerrainData so the built scene survives a save/reload.
            const string dir = "Assets/Vector Fields/Examples/CrowdFlow";
            const string assetPath = dir + "/CrowdTerrain.asset";
            if (AssetDatabase.LoadAssetAtPath<TerrainData>(assetPath) != null) AssetDatabase.DeleteAsset(assetPath);
            AssetDatabase.CreateAsset(td, assetPath);
            AssetDatabase.SaveAssets();

            var go = Terrain.CreateTerrainGameObject(td);
            go.name = "CrowdTerrain";
            go.transform.position = new Vector3(-TerrainWorld * 0.5f, 0f, -TerrainWorld * 0.5f);   // centre on origin
            terrainComp = go.GetComponent<Terrain>();

            // Stylised "Mario 3D World" terrain: grass on non-steep ground (two grass tiles swapped by height),
            // striped wall on steep faces, sand near water. drawInstanced off so the terrain renders as a plain mesh
            // our custom (non-terrain-instancing) shader supports.
            terrainComp.materialTemplate = BuildGrassMaterial();
            terrainComp.drawInstanced = false;
            return go;
        }

        // Create (or refresh) the terrain material asset: wire the grass/wall/sand tiles, the grass height swap,
        // and the wall slope threshold.
        static Material BuildGrassMaterial() {
            const string dir = "Assets/Vector Fields/Examples/CrowdFlow/";
            var sh = Shader.Find("CrowdFlow/MarioGrass");
            if (sh == null) { Debug.LogWarning("CrowdFlow: MarioGrass shader not found; terrain will use its default material."); return null; }
            var mat = AssetDatabase.LoadAssetAtPath<Material>(dir + "GrassTerrain.mat");
            if (mat == null) { mat = new Material(sh); AssetDatabase.CreateAsset(mat, dir + "GrassTerrain.mat"); }
            else mat.shader = sh;

            SetTexIfPresent(mat, "_GrassTex",  dir + "grass.psd");     // low grass
            SetTexIfPresent(mat, "_GrassTex2", dir + "grass 2.psd");   // high grass
            SetTexIfPresent(mat, "_SandTex",   dir + "sand.psd");
            SetTexIfPresent(mat, "_WallTex",   dir + "wall.psd");

            mat.SetColor("_GrassTexTint", Color.white);
            mat.SetFloat("_GrassTexScale", 14f);
            mat.SetFloat("_GrassHeight", 14f);        // world Y where low grass -> high grass
            mat.SetFloat("_GrassHeightBlend", 6f);
            mat.SetFloat("_SandTexScale", 10f);
            mat.SetFloat("_WallTexScale", 12f);
            mat.SetFloat("_WallSlopeAngle", 42f);     // slope > X = wall
            mat.SetFloat("_SlopeBlend", 0.07f);
            mat.SetColor("_FresnelColor", new Color(0.78f, 1f, 0.55f));
            mat.SetFloat("_FresnelPower", 3f);
            mat.SetFloat("_FresnelStrength", 0.6f);
            mat.SetFloat("_AmbientBoost", 1.2f);
            mat.SetFloat("_ShadeFloor", 0.55f);
            mat.SetFloat("_WaterLevel", WaterWorldY);
            EditorUtility.SetDirty(mat);
            AssetDatabase.SaveAssets();
            return mat;
        }

        static void SetTexIfPresent(Material mat, string prop, string path) {
            var t = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (t != null) mat.SetTexture(prop, t);
        }

        static float Gauss(float x, float y, float cx, float cy, float sigma) {
            float dx = x - cx, dy = y - cy;
            return Mathf.Exp(-(dx * dx + dy * dy) / (2f * sigma * sigma));
        }

        // Open sea around the island: a large plane at the waterline using the stylised water shader.
        // Wind: a scrolling noise vector field over the island + a particle system advected by it (via the Vector
        // Field particle force-field consumer), rendered as stretched streaks so the wind currents are visible.
        static void BuildWind(Terrain terrain) {
            var td = terrain.terrainData;
            Vector3 centre = terrain.transform.position + new Vector3(td.size.x * 0.5f, 0f, td.size.z * 0.5f);
            float windY = WaterWorldY + 14f;

            // Scrolling noise field, laid flat (rot X -90) and sampled in world space so it reads as moving wind.
            var fieldGo = new GameObject("WindField");
            fieldGo.transform.SetPositionAndRotation(new Vector3(centre.x, windY, centre.z), Quaternion.Euler(-90f, 0f, 0f));
            fieldGo.transform.localScale = new Vector3(td.size.x * 1.5f, td.size.z * 1.5f, 30f);
            var noise = fieldGo.AddComponent<NoiseVectorFieldComponent>();
            noise.grid.Size = new Vector2Int(40, 40);
            noise.space = NoiseVectorFieldComponent.Space.World;
            noise.magnitude = 8f;
            fieldGo.AddComponent<ScrollNoiseField>();

            // Force field driven by the noise field (adds ParticleSystemForceField + constraints; constrained to the field).
            var ffGo = new GameObject("WindForceField");
            var psvf = ffGo.AddComponent<ParticleSystemVectorField>();
            psvf.vectorFieldComponent = noise;
            var ff = ffGo.GetComponent<ParticleSystemForceField>();

            // Particle system emitting over the island, pushed by the wind force field, drawn as velocity-stretched streaks.
            var psGo = new GameObject("WindParticles");
            psGo.transform.position = new Vector3(centre.x, windY, centre.z);
            var ps = psGo.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = 7f;
            main.startSpeed = 0f;
            main.startSize = 1.4f;
            main.startColor = new Color(1f, 1f, 1f, 0.35f);
            main.maxParticles = 1500;
            var emission = ps.emission; emission.rateOverTime = 200f;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(td.size.x, 18f, td.size.z);
            var ext = ps.externalForces;
            ext.enabled = true;
            ext.multiplier = 1f;
            ext.influenceFilter = ParticleSystemGameObjectFilter.List;
            ext.AddInfluence(ff);
            var rend = psGo.GetComponent<ParticleSystemRenderer>();
            rend.renderMode = ParticleSystemRenderMode.Stretch;
            rend.velocityScale = 0.12f;
            rend.lengthScale = 2.5f;
            rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var psh = Shader.Find("Universal Render Pipeline/Particles/Unlit") ?? Shader.Find("Sprites/Default");
            var pmat = new Material(psh);
            if (pmat.HasProperty("_BaseColor")) pmat.SetColor("_BaseColor", new Color(1f, 1f, 1f, 0.4f));
            rend.material = pmat;
        }

        // A "pillar of light" beacon at an attraction: a tall additive beam cylinder + a point light so it glows and
        // is easy to spot from anywhere on the island.
        static void BuildBeacon(Transform parent, Color c) {
            var beam = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            beam.name = "Beacon";
            Object.DestroyImmediate(beam.GetComponent<Collider>());
            beam.transform.SetParent(parent, false);
            beam.transform.localScale = new Vector3(4f, 40f, 4f);        // cylinder is 2 tall -> 80 world units
            beam.transform.localPosition = Vector3.up * 40f;            // base at the attraction, rising 80 up
            var sh = Shader.Find("CrowdFlow/LightPillar");
            var mat = new Material(sh != null ? sh : Shader.Find("Sprites/Default"));
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", c * 3f);   // HDR for bloom
            beam.GetComponent<MeshRenderer>().sharedMaterial = mat;
            beam.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            var lightGo = new GameObject("BeaconLight");
            lightGo.transform.SetParent(parent, false);
            lightGo.transform.localPosition = Vector3.up * 6f;
            var l = lightGo.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = c;
            l.intensity = 6f;
            l.range = 45f;
        }

        static void BuildWaterPlane() {
            var water = GameObject.CreatePrimitive(PrimitiveType.Quad);
            water.name = "Sea";
            Object.DestroyImmediate(water.GetComponent<Collider>());
            water.transform.position = new Vector3(0f, WaterWorldY, 0f);
            water.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            water.transform.localScale = new Vector3(2000f, 2000f, 1f);   // extends well past the island to the horizon
            water.GetComponent<MeshRenderer>().sharedMaterial = BuildWaterMaterial();
        }

        // Material from the Minions Art stylised-water shadergraph dropped into this folder; falls back to a blue Lit
        // material if the graph isn't present. Tuning is left to the material asset (iterate in the inspector).
        static Material BuildWaterMaterial() {
            const string dir = "Assets/Vector Fields/Examples/CrowdFlow/";
            const string matPath = dir + "StylizedWater.mat";
            var sh = AssetDatabase.LoadAssetAtPath<Shader>(dir + "StylizedWater2026.shadergraph");
            var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (sh == null) {
                Debug.LogWarning("CrowdFlow: StylizedWater2026.shadergraph not found; using a plain blue water material.");
                sh = Shader.Find("Universal Render Pipeline/Lit");
                if (mat == null) { mat = new Material(sh); AssetDatabase.CreateAsset(mat, matPath); } else mat.shader = sh;
                var blue = new Color(0.2f, 0.45f, 0.8f, 0.7f);
                if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", blue);
                SetTransparent(mat);
            } else {
                if (mat == null) { mat = new Material(sh); AssetDatabase.CreateAsset(mat, matPath); } else mat.shader = sh;
            }
            EditorUtility.SetDirty(mat);
            AssetDatabase.SaveAssets();
            return mat;
        }

        static void BuildLight() {
            if (Object.FindObjectOfType<Light>() != null) return;
            var go = new GameObject("Sun");
            var l = go.AddComponent<Light>();
            l.type = LightType.Directional;
            l.intensity = 1.1f;
            go.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
        }

        static void BuildCamera() {
            Camera cam = Camera.main;
            if (cam == null) {
                var go = new GameObject("Main Camera");
                go.tag = "MainCamera";
                cam = go.AddComponent<Camera>();
            }
            cam.transform.position = new Vector3(0f, 110f, -150f);
            cam.transform.LookAt(new Vector3(0f, 6f, 0f));
            cam.fieldOfView = 45f;
            cam.farClipPlane = 3000f;
            if (cam.GetComponent<DemoCameraController>() == null) cam.gameObject.AddComponent<DemoCameraController>();
        }

        // A Drawable field laid out by the manager at runtime; here we just create it with a quad + material so the
        // existing VectorFieldTextureRenderer draws the flow on the ground. Encoded RG shows as colour — swap the
        // material for an IBFV / Flow-Map setup for a prettier look.
        static DrawableVectorFieldComponent BuildVisualField(Transform parent, int i, Mesh quad, Color tint, bool onlyThisRenders) {
            // The field (data) sits on one GameObject, laid flat over the terrain; a separate child quad carries the
            // VectorFieldTextureRenderer and aligns itself to the field rect (matchFieldBounds). Keeping them apart
            // avoids the renderer's bounds-matching feeding back on the field's own transform.
            var fieldGo = new GameObject("FlowField_" + (i + 1));
            fieldGo.transform.SetParent(parent, true);
            fieldGo.transform.SetPositionAndRotation(new Vector3(0f, 12f, 0f), Quaternion.Euler(-90f, 0f, 0f));
            fieldGo.transform.localScale = new Vector3(TerrainWorld, TerrainWorld, 1f);
            var vf = fieldGo.AddComponent<DrawableVectorFieldComponent>();
            vf.grid.Size = new Vector2Int(GridRes, GridRes);

            var vizGo = new GameObject("FlowViz_" + (i + 1));
            vizGo.transform.SetParent(fieldGo.transform, false);
            var mf = vizGo.AddComponent<MeshFilter>();
            mf.sharedMesh = quad;
            var mr = vizGo.AddComponent<MeshRenderer>();
            var sh = Shader.Find("Unlit/Texture") ?? Shader.Find("Sprites/Default");
            mr.sharedMaterial = new Material(sh);
            mr.enabled = onlyThisRenders;

            var tr = vizGo.AddComponent<VectorFieldTextureRenderer>();
            tr.vectorFieldComponent = vf;
            return vf;
        }

        static Mesh BuiltinQuad() {
            var temp = GameObject.CreatePrimitive(PrimitiveType.Quad);
            var mesh = temp.GetComponent<MeshFilter>().sharedMesh;
            Object.DestroyImmediate(temp);
            return mesh;
        }

        static void TintPrimitive(GameObject go, Color c) {
            var mr = go.GetComponent<MeshRenderer>();
            var sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var mat = new Material(sh);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", c);
            if (mat.HasProperty("_Color")) mat.color = c;
            mr.sharedMaterial = mat;
        }

        static void SetTransparent(Material mat) {
            if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 1f);
            mat.SetOverrideTag("RenderType", "Transparent");
            if (mat.HasProperty("_SrcBlend")) mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            if (mat.HasProperty("_DstBlend")) mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            if (mat.HasProperty("_ZWrite")) mat.SetFloat("_ZWrite", 0f);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        }

        // Ensure a named layer exists (editing the TagManager asset) and return its index; falls back to Default (0).
        static int EnsureLayer(string name) {
            var tagManager = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            var layers = tagManager.FindProperty("layers");
            for (int i = 8; i < layers.arraySize; i++) {
                var sp = layers.GetArrayElementAtIndex(i);
                if (sp.stringValue == name) return i;
            }
            for (int i = 8; i < layers.arraySize; i++) {
                var sp = layers.GetArrayElementAtIndex(i);
                if (string.IsNullOrEmpty(sp.stringValue)) {
                    sp.stringValue = name;
                    tagManager.ApplyModifiedProperties();
                    return i;
                }
            }
            Debug.LogWarning("CrowdFlow: no free layer slot for 'CrowdObstacle'; obstacles will use Default (may block terrain).");
            return 0;
        }
    }
}
