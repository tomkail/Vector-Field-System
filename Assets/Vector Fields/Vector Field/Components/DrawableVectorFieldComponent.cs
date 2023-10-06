public class DrawableVectorFieldComponent : VectorFieldComponent {
    protected override void RenderInternal() { }

    [EasyButtons.Button]
    public void Clear() {
        vectorField.Clear();
        SetDirty();
    }
}