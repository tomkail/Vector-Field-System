using UnityEngine;

namespace VectorFields {
    // How the debug arrows are tinted.
    public enum VectorFieldDebugColorMode {
        // Hue from the vector's direction, opacity from its magnitude (the classic flow-field look).
        Direction,
        // A low->high colour gradient driven by the vector's magnitude.
        Magnitude,
        // A single flat colour, regardless of direction or magnitude.
        Fixed,
        // Invert whatever's behind each arrow instead of tinting — guarantees the arrows stand out against any
        // background (the one null is exact mid-grey, which inverts to itself). Ignores the colour fields.
        InvertBackground,
    }

    // Appearance settings for the vector field debug arrows. This is plain runtime data so the renderer can consume it
    // directly; the editor-side project settings (see VectorFieldDebugProjectSettings) hold and serialize an instance of
    // it. Defaults give the standard look (Direction colouring, full opacity, unit magnitude scale).
    [System.Serializable]
    public class VectorFieldDebugAppearance {
        [Tooltip("Glyph drawn for each arrow. Leave empty to use the built-in arrow texture.")]
        public Texture2D arrowTexture;

        [Tooltip("How arrows are tinted: by direction (hue), by magnitude (gradient), or a single fixed colour.")]
        public VectorFieldDebugColorMode colorMode = VectorFieldDebugColorMode.Direction;

        [Tooltip("Colour used in Fixed mode.")]
        public Color fixedColor = Color.white;

        [Tooltip("Magnitude mode: colour at zero magnitude.")]
        public Color lowColor = new Color(0.15f, 0.3f, 0.9f, 1f);

        [Tooltip("Magnitude mode: colour at (and above) the max magnitude.")]
        public Color highColor = new Color(1f, 0.35f, 0.1f, 1f);

        [Tooltip("Vector magnitude that maps to full intensity (the high colour / full direction opacity).")]
        public float maxMagnitude = 1f;

        [Range(0f, 1f), Tooltip("Overall opacity multiplier for the arrows.")]
        public float opacity = 1f;
    }
}
