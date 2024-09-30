public class DrawableVectorFieldComponent : VectorFieldComponent {
    protected override void RenderInternal() {
        if(vectorField == null || vectorField.values.Length != gridRenderer.gridSize.x * gridRenderer.gridSize.y)
            vectorField = new Vector2Map(gridRenderer.gridSize);
    }

    [EasyButtons.Button]
    public void Clear() {
        vectorField.Clear();
        SetDirty();
    }
}