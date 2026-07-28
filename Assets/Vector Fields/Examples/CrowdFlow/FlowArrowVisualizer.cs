using System.Collections.Generic;
using UnityEngine;
using VectorFields;

namespace CrowdFlow {
    /// <summary>
    /// Visualises each destination's flow field as arrows, using the Vector Field <see cref="VectorFieldArrowRenderer"/>,
    /// draped over the terrain heightmap. It builds a height texture from the terrain (rebuilt when the terrain is
    /// sculpted) and pushes it to the arrow shader through global properties (<c>_VFHeightMap</c> / <c>_VFHeightDrape</c> /
    /// <c>_VFHeightRect</c> / <c>_VFHeightParams</c>), so the arrows follow the ground instead of the flat field plane.
    /// One arrow renderer is created per destination flow field; <see cref="SetVisible"/> / <see cref="Cycle"/> pick which
    /// "map" is shown (or none). The flat coloured texture-quad visualisation is hidden so the arrows are the readout.
    /// </summary>
    public class FlowArrowVisualizer : MonoBehaviour {
        [Tooltip("Owns the destinations + their flow fields (defaults to a manager on this GameObject).")]
        public CrowdFlowManager manager;
        [Tooltip("Terrain the arrows drape over (defaults to the manager's terrain).")]
        public Terrain terrain;
        [Tooltip("Lift of the arrows above the terrain surface, world units.")]
        public float heightOffset = 0.6f;
        [Tooltip("Number of arrows along the field's long axis (fixed density).")]
        public int arrowCount = 48;
        [Tooltip("Arrow glyph texture (the built-in one lives in an Editor folder, so it must be assigned here to " +
                 "show at runtime — otherwise arrows render as solid quads).")]
        public Texture2D arrowTexture;

        static readonly int HeightMap = Shader.PropertyToID("_VFHeightMap");
        static readonly int Drape     = Shader.PropertyToID("_VFHeightDrape");
        static readonly int RectProp  = Shader.PropertyToID("_VFHeightRect");
        static readonly int ParamsProp= Shader.PropertyToID("_VFHeightParams");

        readonly List<VectorFieldArrowRenderer> _renderers = new List<VectorFieldArrowRenderer>();
        Texture2D _heightTex;
        bool _heightDirty;
        int _visible = -1;   // -1 = off; otherwise the destination index whose arrows are shown

        /// <summary>Number of destination maps available.</summary>
        public int Count => _renderers.Count;
        /// <summary>Currently shown map (-1 = none).</summary>
        public int Visible => _visible;

        void Start() {
            if (manager == null) manager = GetComponent<CrowdFlowManager>();
            if (terrain == null && manager != null) terrain = manager.terrain;
            if (manager == null || terrain == null) { enabled = false; return; }

            BuildHeightTexture();

            for (int i = 0; i < manager.destinations.Count; i++) {
                var vf = manager.destinations[i].visualField;
                if (vf == null) { _renderers.Add(null); continue; }
                // Hide the flat coloured texture-quad viz; the arrows are the visualisation now.
                foreach (var mr in vf.GetComponentsInChildren<MeshRenderer>()) mr.enabled = false;
                var ar = vf.gameObject.AddComponent<VectorFieldArrowRenderer>();
                ar.vectorFieldComponent = vf;
                // A fixed, dense grid of direction-coloured arrows reads as a flow map (Adaptive makes a few giant
                // arrows at this camera distance). Tint each map by its attraction colour so the maps stay distinct.
                ar.ResolutionMode = VectorFieldArrowResolutionMode.Fixed;
                ar.FixedResolution = arrowCount;
                var att = manager.GetAttraction(i);
                ar.Appearance = new VectorFieldDebugAppearance {
                    colorMode = VectorFieldDebugColorMode.Fixed,
                    fixedColor = att != null ? att.color : Color.white,
                    opacity = 1f,
                    maxMagnitude = 1f,
                    arrowTexture = arrowTexture,
                };
                ar.enabled = false;      // shown on demand via SetVisible
                _renderers.Add(ar);
            }

            TerrainCallbacks.heightmapChanged += OnHeightmapChanged;
            SetVisible(-1);
        }

        void OnDestroy()  => TerrainCallbacks.heightmapChanged -= OnHeightmapChanged;
        void OnEnable()   { if (_heightTex != null) Shader.SetGlobalFloat(Drape, 1f); }
        void OnDisable()  => Shader.SetGlobalFloat(Drape, 0f);   // don't drape other arrow renderers when we're inactive

        void OnHeightmapChanged(Terrain t, RectInt region, bool synched) { if (t == terrain) _heightDirty = true; }

        void LateUpdate() { if (_heightDirty) { _heightDirty = false; RefreshHeightTexture(); } }

        // ---------------------------------------------------------------- visibility

        /// <summary>Show destination <paramref name="index"/>'s arrows (-1 = hide all).</summary>
        public void SetVisible(int index) {
            _visible = Mathf.Clamp(index, -1, _renderers.Count - 1);
            for (int i = 0; i < _renderers.Count; i++)
                if (_renderers[i] != null) _renderers[i].enabled = (i == _visible);
        }

        /// <summary>Advance Off -> map 0 -> map 1 -> ... -> last -> Off.</summary>
        public void Cycle() => SetVisible(_visible + 1 > _renderers.Count - 1 ? -1 : _visible + 1);

        // ---------------------------------------------------------------- height texture / drape globals

        void BuildHeightTexture() {
            int res = terrain.terrainData.heightmapResolution;
            _heightTex = new Texture2D(res, res, TextureFormat.RGBAFloat, false, true) {
                name = "CrowdFlow_HeightTex",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            RefreshHeightTexture();
        }

        void RefreshHeightTexture() {
            var td = terrain.terrainData;
            int res = td.heightmapResolution;
            if (_heightTex == null || _heightTex.width != res) { BuildHeightTexture(); return; }
            float[,] h = td.GetHeights(0, 0, res, res);   // [z, x], normalized 0..1
            var cols = new Color[res * res];
            for (int z = 0; z < res; z++)
                for (int x = 0; x < res; x++)
                    cols[z * res + x] = new Color(h[z, x], 0f, 0f, 0f);
            _heightTex.SetPixels(cols);
            _heightTex.Apply(false);
            PushDrapeGlobals();
        }

        void PushDrapeGlobals() {
            var td = terrain.terrainData;
            Vector3 p = terrain.transform.position;
            Shader.SetGlobalTexture(HeightMap, _heightTex);
            Shader.SetGlobalVector(RectProp, new Vector4(p.x, p.z, td.size.x, td.size.z));
            Shader.SetGlobalVector(ParamsProp, new Vector4(p.y, td.size.y, heightOffset, 0f));
            if (isActiveAndEnabled) Shader.SetGlobalFloat(Drape, 1f);
        }
    }
}
