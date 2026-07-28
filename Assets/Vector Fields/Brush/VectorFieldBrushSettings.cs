namespace VectorFields {
    [System.Serializable]
    public class VectorFieldBrushSettings {
        public enum ForceEmitterType {
            Directional,
            Spot
        }
        public ForceEmitterType forceType = ForceEmitterType.Directional;

        public float directionalAngle;
        public float vortexAngle;

        // Allocation-free content hash for change detection, replacing per-tick JSON serialization.
        public int GetContentHash() => System.HashCode.Combine((int)forceType, directionalAngle, vortexAngle);
    }
}
