using UnityEngine;

// Dev harness for previewing a cookie/falloff in isolation. Tick `update` to regenerate.
[ExecuteAlways]
public class CircularBrushFalloffTester : MonoBehaviour {
    public VectorFieldCookieSource cookie = new VectorFieldCookieSource();
    public Vector2Int size = new Vector2Int(64, 64);
    public bool update;

    [PreviewTexture] public Texture result;

    void Update() {
        if (update) result = cookie.Resolve(size);
    }

    void OnDisable() {
        cookie?.Dispose();
        result = null;
    }
}
