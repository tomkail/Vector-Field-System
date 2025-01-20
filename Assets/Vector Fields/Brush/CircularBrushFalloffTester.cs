using UnityEngine;

[ExecuteAlways]
public class CircularBrushFalloffTester : MonoBehaviour {
    public VectorFieldCookieTextureCreatorSettings settings;
    public VectorFieldCookieTextureCreator circularBrushFalloff;
    public bool update;

    void OnEnable() {
        circularBrushFalloff = new VectorFieldCookieTextureCreator();
    }

    void Update() {
        if (update) {
            circularBrushFalloff.Render(settings);
        }
    }
}