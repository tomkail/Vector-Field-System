using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Windfall {
    /// <summary>
    /// Runtime bloom for the "instrument glow" look (GAME_DESIGN.md §8): enables post-processing on the main
    /// camera and installs a global <see cref="Volume"/> with a <see cref="Bloom"/> override. Built entirely in
    /// code (no scene Volume or profile asset to author), created by <see cref="WindfallGame"/> on start, so the
    /// plasma trails and cyan flow lines actually glow. Tunables apply live in play mode.
    /// </summary>
    [DisallowMultipleComponent]
    public class WindfallPostFx : MonoBehaviour {
        [Tooltip("Bloom strength.")] public float intensity = 1f;
        [Tooltip("Brightness above which pixels bloom (lower = more glow).")] public float threshold = 0.8f;
        [Range(0f, 1f), Tooltip("How wide the glow spreads.")] public float scatter = 0.7f;

        Volume _volume;
        VolumeProfile _profile;
        Bloom _bloom;

        void OnEnable() {
            var cam = Camera.main;
            if (cam != null) {
                var data = cam.GetComponent<UniversalAdditionalCameraData>();
                if (data == null) data = cam.gameObject.AddComponent<UniversalAdditionalCameraData>();
                data.renderPostProcessing = true;
            }

            _profile = ScriptableObject.CreateInstance<VolumeProfile>();
            _bloom = _profile.Add<Bloom>(true);
            _bloom.active = true;
            _bloom.intensity.Override(intensity);
            _bloom.threshold.Override(threshold);
            _bloom.scatter.Override(scatter);

            _volume = gameObject.AddComponent<Volume>();
            _volume.isGlobal = true;
            _volume.priority = 100f;
            _volume.profile = _profile;
        }

        void Update() {
            if (_bloom == null) return;
            _bloom.intensity.value = intensity;
            _bloom.threshold.value = threshold;
            _bloom.scatter.value = scatter;
        }

        void OnDisable() {
            if (_volume != null) Destroy(_volume);
            if (_profile != null) Destroy(_profile);
        }
    }
}
