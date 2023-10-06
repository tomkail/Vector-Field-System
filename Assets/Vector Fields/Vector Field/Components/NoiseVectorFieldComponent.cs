using System.Linq;
using UnityEngine;

public class NoiseVectorFieldComponent : VectorFieldComponent {
    public enum Space {
        Local,
        World
    }
    public Space space = Space.Local;
    public NoiseSampler noiseSampler;
    public float vortexAngle = 90;

    public ComputeShader computeShader;

    // Must match what's in the compute shader
    const int threadsPerGroupX = 16;
    const int threadsPerGroupY = 16;

    
    protected override void RenderInternal() {
        vectorField = new Vector2Map(gridRenderer.gridSize);

        // Initialize ComputeBuffer
        var computeBuffer = new ComputeBuffer(vectorField.values.Length, sizeof(float) * 2);
        computeShader.SetBuffer(0, "Result", computeBuffer);

        // Calculate the number of thread groups
        int threadGroupsX = Mathf.CeilToInt((float)vectorField.size.x / threadsPerGroupX);
        int threadGroupsY = Mathf.CeilToInt((float)vectorField.size.y / threadsPerGroupY);

        
        computeShader.SetInt("NumThreadGroupsX", threadGroupsX);
        computeShader.SetInt("width", vectorField.size.x);
        computeShader.SetInt("height", vectorField.size.y);
        
        computeShader.SetFloat("magnitude", magnitude);
        Matrix4x4 gridToWorldMatrix = Matrix4x4.identity;
        if (space == Space.Local) gridToWorldMatrix = Matrix4x4.Translate(new Vector3(1000f,0,0)) * gridRenderer.cellCenter.gridToLocalMatrix * Matrix4x4.Translate(noiseSampler.position);
        else if (space == Space.World) gridToWorldMatrix = gridRenderer.cellCenter.gridToWorldMatrix * Matrix4x4.Translate(noiseSampler.position);
        computeShader.SetMatrix("gridToWorldMatrix", gridToWorldMatrix);
        
        computeShader.SetFloat("frequency", noiseSampler.properties.frequency);
        computeShader.SetFloat("persistence", noiseSampler.properties.persistence);
        computeShader.SetFloat("lacunarity", noiseSampler.properties.lacunarity);
        computeShader.SetInt("numOctaves", noiseSampler.properties.octaves);
        
        computeShader.SetFloat("vortexAngle", vortexAngle);

        computeShader.Dispatch(0, threadGroupsX, threadGroupsY, 1);
        computeBuffer.GetData(vectorField.values);
        computeBuffer.Release();
    }
    
    /*
    protected override void RenderInternal() {
        vectorField = new Vector2Map(gridRenderer.gridSize, Vector2.zero);
        var points = vectorField.Points();
        foreach (var point in points) {
            vectorField.SetValueAtGridPoint(point, Evaluate(point));
        }
    }

    Vector2 Evaluate(Point point) {
        var position = gridRenderer.cellCenter.GridToWorldPoint(point);
        var localPosition = gridRenderer.cellCenter.GridToLocalPoint(point);
        var normalizedPosition = gridRenderer.cellCenter.GridToNormalizedPosition(point);
        var cookieValue = 1f;
        if (cookie.cookieTexture != null) {
            cookie.cookieMap = new HeightMap(new Point(cookie.cookieTexture.width, cookie.cookieTexture.height), cookie.cookieTexture.GetPixels().Select(x => x.r).ToArray());
            cookieValue = cookie.cookieMap.GetValueAtNormalizedPosition(normalizedPosition);
        }

        var rawVector = noiseSampler.SampleAtPosition(noiseSampler.position + (space == Space.Local ? localPosition : position)).derivative;
        return rawVector * (magnitude * (1f / noiseSampler.properties.frequency) * cookieValue);
    }
    */
}