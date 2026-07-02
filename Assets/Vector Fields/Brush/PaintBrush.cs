// A reusable, configured brush over any field value type T: shape + op + size + pressure + tip/direction mode. The
// generic counterpart of VectorFieldBrush (which stays as the established Vector2 public API); new value types (e.g.
// Color/smoke) use this. Cheap to copy (holds two references plus scalars); build once and reuse every frame.
public struct PaintBrush<T> {
    public BrushShape shape;              // the spatial profile is value-agnostic, so it's shared as-is
    public IBrushOp<T> op;
    public float size;                    // brush radius in WORLD units
    public float pressure;                // op strength / magnitude reference
    public TipMode tipMode;               // how a continuous stroke renders its head; ignored by one-shots
    public BrushDirectionMode directionMode;   // how a map/directional brush orients (ignored by radial shapes)

    public PaintBrush(BrushShape shape, IBrushOp<T> op, float size = 1.5f, float pressure = 1f,
                      TipMode tipMode = TipMode.Smoothed,
                      BrushDirectionMode directionMode = BrushDirectionMode.FollowStroke) {
        this.shape = shape;
        this.op = op;
        this.size = size;
        this.pressure = pressure;
        this.tipMode = tipMode;
        this.directionMode = directionMode;
    }

    public bool IsValid => shape != null && op != null;
}
