using System.Collections.Generic;
using UnityEngine;

namespace Windfall {
    /// <summary>
    /// Builds a random level at runtime as a <c>GroupVectorFieldComponent</c> (GAME_DESIGN.md §4/§6): a Noise
    /// base flow with a handful of randomly-placed Stamp features (vortices / attractors / gusts) blended on top.
    /// A level is "effectively a vector field group", so this just assembles the group's child fields from a seed.
    ///
    /// The Vector Field components live in the global namespace and follow a strict runtime-creation order
    /// (deactivate → AddComponent → set grid resolution → configure → activate), which <see cref="MakeField"/>
    /// encapsulates. Spline lanes are optional (needs com.unity.splines; compiled under WINDFALL_SPLINES) and are
    /// aimed roughly at the target so they never carry players entirely away from it.
    /// </summary>
    public class WindfallLevelGenerator : MonoBehaviour {
        [Header("Field extent / resolution")]
        [Tooltip("The generated group covers a worldSize × worldSize square centred on the origin (matches the play area).")]
        public float worldSize = 26f;
        [Tooltip("Blend/output resolution of the group field.")]
        public int groupResolution = 96;
        [Tooltip("Resolution of each child field (noise / stamps).")]
        public int childResolution = 64;

        [Header("Toward-target pull (base flow — keeps levels winnable)")]
        [Tooltip("Strength of the constant attractor pulling players toward the target; kept above the noise so the target is always reachable.")]
        public float targetPullStrength = 1.6f;

        [Header("Noise variation")]
        [Tooltip("Noise frequency (cycles across the field): higher = more, tighter meanders on top of the pull.")]
        public Vector2 frequencyRange = new Vector2(1f, 2f);
        public Vector2Int octavesRange = new Vector2Int(1, 3);
        [Tooltip("Vortex angle range: ~0 = flow along the gradient, 90 = swirling/curl.")]
        public Vector2 vortexAngleRange = new Vector2(55f, 120f);

        [Header("Stamp accents (subtle local swirls / gusts)")]
        public Vector2Int stampCountRange = new Vector2Int(0, 2);
        [Tooltip("Stamp diameter as a fraction of the level size, so features scale with the level.")]
        public Vector2 stampSizeFracRange = new Vector2(0.14f, 0.3f);
        [Tooltip("Kept low so accents don't overpower the toward-target pull.")]
        public Vector2 stampStrengthRange = new Vector2(0.3f, 0.6f);
        [Tooltip("Stamps are placed within this fraction of the play radius so they land on the board.")]
        public float stampPlacementFrac = 0.7f;

        [Header("Spline roads (needs com.unity.splines)")]
        [Range(0f, 1f), Tooltip("Chance a level gets a curved spline 'road' aimed at the target.")]
        public float splineChance = 0.7f;
        [Tooltip("Road half-width in field-local units (scaled by the level size).")]
        public Vector2 splineWidthRange = new Vector2(0.08f, 0.16f);

        readonly List<GameObject> _spawned = new List<GameObject>();

        /// <summary>
        /// Build a fresh random level from a seed. If <paramref name="target"/> is given (normally the scene's
        /// level Group that the visualiser + game already reference), its child fields are replaced in place so
        /// everything downstream keeps working; otherwise a standalone group is created at the origin.
        /// </summary>
        public GroupVectorFieldComponent Generate(int seed, GroupVectorFieldComponent target, Vector2 targetWorldPos, float worldSizeOverride) {
            var rng = new System.Random(seed);
            float ws = worldSizeOverride > 0f ? worldSizeOverride : worldSize;
            Vector3 center = Vector3.zero;
            float playRadius = ws * 0.5f;

            GroupVectorFieldComponent group;
            if (target != null) {
                // Populate the existing group in place — keep the object the visualiser + game.field point at.
                group = target;
                ClearChildFields(group);
                group.transform.position = center;
                group.transform.localScale = new Vector3(ws, ws, 1f);
            } else {
                var groupGO = new GameObject("GeneratedLevel");
                groupGO.SetActive(false);
                _spawned.Add(groupGO);
                group = groupGO.AddComponent<GroupVectorFieldComponent>();
                group.grid.Size = new Vector2Int(groupResolution, groupResolution);
                groupGO.transform.position = center;
                groupGO.transform.localScale = new Vector3(ws, ws, 1f);
            }

            // --- base: a constant pull toward the target so every level is winnable ---
            var pull = MakeField<StampVectorFieldComponent>("PullToTarget", group.transform, targetWorldPos, ws * 2.5f);
            pull.magnitude = targetPullStrength;
            pull.brushSettingsParams.forceType = VectorFieldBrushSettings.ForceEmitterType.Spot;
            pull.brushSettingsParams.vortexAngle = 180f;                  // 180 = pull inward toward the target (0 pushes away, verified)
            pull.cookie.mode = VectorFieldCookieSource.Mode.None;         // uniform pull across the whole level
            Activate(pull);

            // --- noise: meandering variation layered on top of the pull ---
            var noise = MakeField<NoiseVectorFieldComponent>("Noise", group.transform, center, ws);
            noise.space = NoiseVectorFieldComponent.Space.Local;
            noise.noiseSampler.position = new Vector3((float)rng.NextDouble() * 1000f, (float)rng.NextDouble() * 1000f, 0f);
            noise.noiseSampler.properties.frequency = Range(frequencyRange, rng);
            noise.noiseSampler.properties.octaves = rng.Next(octavesRange.x, octavesRange.y + 1);
            noise.noiseSampler.properties.lacunarity = 2f;
            noise.noiseSampler.properties.persistence = 0.5f;
            noise.vortexAngle = Range(vortexAngleRange, rng);
            noise.normalizeMagnitude = true;   // keep the base flow ~unit regardless of frequency
            Activate(noise);

            // --- random stamp features ---
            int stamps = rng.Next(stampCountRange.x, stampCountRange.y + 1);
            for (int i = 0; i < stamps; i++) {
                float size = Range(stampSizeFracRange, rng) * ws;
                Vector2 pos = (Vector2)center + RandomInDisc(rng, playRadius * stampPlacementFrac);
                var stamp = MakeField<StampVectorFieldComponent>("Stamp" + i, group.transform, pos, size);
                stamp.magnitude = Range(stampStrengthRange, rng);
                stamp.cookie.mode = VectorFieldCookieSource.Mode.Falloff;   // soft radial edge
                stamp.cookie.falloffSoftness = 0.65f;

                if (rng.NextDouble() < 0.65) {
                    // Spot: attractor (vortexAngle→0), vortex (→90) or repeller (→180)
                    stamp.brushSettingsParams.forceType = VectorFieldBrushSettings.ForceEmitterType.Spot;
                    stamp.brushSettingsParams.vortexAngle = Range(new Vector2(0f, 180f), rng);
                } else {
                    // Directional gust
                    stamp.brushSettingsParams.forceType = VectorFieldBrushSettings.ForceEmitterType.Directional;
                    stamp.brushSettingsParams.directionalAngle = Range(new Vector2(0f, 360f), rng);
                }
                Activate(stamp);
            }

#if WINDFALL_SPLINES
            // --- optional spline lane (slipstream), aimed roughly at the target ---
            if (rng.NextDouble() < splineChance) AddSplineLane(group.transform, center, ws, targetWorldPos, rng);
#endif

            if (target == null) group.gameObject.SetActive(true);   // standalone: children were parented while inactive
            group.SetDirty();
            group.Render();            // force RefreshLayers + an initial blend so consumers see it immediately
            return group;
        }

#if WINDFALL_SPLINES
        // A curved open flow-lane traced by a spline (GAME_DESIGN §9a slipstream). Knots live in the field's
        // local unit square (±0.5); a random Z rotation aims the lane so it isn't always left-to-right.
        void AddSplineLane(Transform parent, Vector3 center, float ws, Vector2 targetWorldPos, System.Random rng) {
            var go = new GameObject("SplineLane");
            go.SetActive(false);
            var container = go.AddComponent<UnityEngine.Splines.SplineContainer>();
            var field = go.AddComponent<SplineVectorFieldComponent>();
            field.grid.Size = new Vector2Int(childResolution, childResolution);

            // Knots trace a gentle curve along +x local (so the lane's net flow is +x before rotation).
            var spline = container.Spline;
            const int knots = 4;
            for (int i = 0; i < knots; i++) {
                float x = Mathf.Lerp(-0.42f, 0.42f, i / (float)(knots - 1));
                float y = Range(new Vector2(-0.35f, 0.35f), rng);
                spline.Add(new UnityEngine.Splines.BezierKnot(new Unity.Mathematics.float3(x, y, 0f)),
                           UnityEngine.Splines.TangentMode.AutoSmooth);
            }
            field.splineContainer = container;
            field.width = Range(splineWidthRange, rng);
            field.directionMode = SplineVectorFieldGenerator.DirectionMode.Flow;
            field.magnitude = Range(stampStrengthRange, rng);

            // Rotate so the lane's +x flow points roughly at the target (± spread), never entirely away from it.
            Vector2 toTarget = targetWorldPos - (Vector2)center;
            float baseAngle = toTarget.sqrMagnitude > 0.0001f
                ? Mathf.Atan2(toTarget.y, toTarget.x) * Mathf.Rad2Deg
                : Range(new Vector2(0f, 360f), rng);
            float rotZ = baseAngle + Range(new Vector2(-45f, 45f), rng);

            go.transform.position = new Vector3(center.x, center.y, 0f);
            go.transform.rotation = Quaternion.Euler(0f, 0f, rotZ);
            go.transform.localScale = new Vector3(ws, ws, 1f);
            go.transform.SetParent(parent, worldPositionStays: true);
            go.SetActive(true);
        }
#endif

        /// <summary>Destroy the child fields of a group (its previous level), leaving the group itself.</summary>
        static void ClearChildFields(GroupVectorFieldComponent group) {
            var kids = group.GetComponentsInChildren<VectorFieldComponent>(true);
            foreach (var k in kids) if (k != group) Destroy(k.gameObject);
        }

        /// <summary>Destroy every standalone GameObject this generator spawned (teardown only).</summary>
        public void Dispose() {
            foreach (var go in _spawned) if (go != null) Destroy(go);
            _spawned.Clear();
        }

        // Create a child field GameObject following the required order: deactivate → AddComponent → set grid,
        // then place it in world space and parent it (keeping the world transform so the parent's scale doesn't
        // distort it). The caller configures it and calls Activate().
        T MakeField<T>(string name, Transform parent, Vector2 worldPos, float worldSize) where T : VectorFieldComponent {
            var go = new GameObject(name);
            go.SetActive(false);
            var comp = go.AddComponent<T>();
            comp.grid.Size = new Vector2Int(childResolution, childResolution);
            go.transform.position = new Vector3(worldPos.x, worldPos.y, 0f);
            go.transform.localScale = new Vector3(worldSize, worldSize, 1f);
            go.transform.SetParent(parent, worldPositionStays: true);
            return comp;
        }

        static void Activate(VectorFieldComponent comp) => comp.gameObject.SetActive(true);

        static float Range(Vector2 r, System.Random rng) => r.x + (r.y - r.x) * (float)rng.NextDouble();

        static Vector2 RandomInDisc(System.Random rng, float radius) {
            double ang = rng.NextDouble() * System.Math.PI * 2.0;
            double rad = radius * System.Math.Sqrt(rng.NextDouble());
            return new Vector2((float)(System.Math.Cos(ang) * rad), (float)(System.Math.Sin(ang) * rad));
        }
    }
}
