using System;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

[System.Serializable]
public abstract class VectorFieldTextureCreator : IDisposable {
    protected Vector2Int gridSize;

    public Vector2Int GridSize {
        get => gridSize;
        set {
            gridSize = value;
            EnsureHasValidRenderTexture();
        }
    }
    public float magnitude = 1;
    public float Magnitude {
        get => magnitude;
        set {
            magnitude = value;
        }
    }
    
    public Texture2D cookieTexture;
    
    [SerializeReference]
    protected RenderTexture renderTexture;
    public RenderTexture RenderTexture => renderTexture;
    
    
    public bool keepCPUUpdated = false;
    AsyncGPUReadbackRequest? readbackRequest;
    public Vector2Map vectorField;
    
    protected bool disposed = false; 
    
    public VectorFieldTextureCreator(Vector2Int gridSize) {
        this.gridSize = gridSize;
        EnsureHasValidRenderTexture();
    }
    
    public virtual void DisposeAndDestroyRenderTexture() {
        DestroyRenderTexture();
        Dispose();
    }
    public virtual void Dispose() {
        vectorField = null;
        disposed = true;
    }

    protected void ThrowIfDisposed() {
        if (disposed) throw new ObjectDisposedException(GetType().Name);
    }
    
    public void EnsureHasValidRenderTexture() {
        VectorFieldRenderTextureUtils.EnsureValid(ref renderTexture, gridSize.x, gridSize.y);
    }
    
    public void ReleaseRenderTexture () {
        if(renderTexture == null) return;
        if(RenderTexture.active == renderTexture) RenderTexture.active = null;
        renderTexture.Release();
    }

    public void DestroyRenderTexture() {
        if(renderTexture == null) return;
        if(RenderTexture.active == renderTexture) RenderTexture.active = null;
        if(Application.isPlaying) Object.Destroy(renderTexture);
        else Object.DestroyImmediate(renderTexture);
        renderTexture = null;
    }
    
    

    public void Render() {
        RenderInternal();
        if(keepCPUUpdated && renderTexture != null) _ = ReadIntoCPUAsync();
        // OnRender?.Invoke();
    }
    protected abstract void RenderInternal();
    
    
    // Reads the vector field texture into the VectorField object immediately.
    public void ReadIntoCPUImmediate() {
        if (renderTexture == null) {
            Debug.LogError("RenderTexture is not assigned.");
            return;
        }
        
        try {
            if (readbackRequest == null || ((AsyncGPUReadbackRequest) readbackRequest).done) 
                readbackRequest = AsyncGPUReadback.Request(renderTexture, 0, ReadFromGPUCallback);
            ((AsyncGPUReadbackRequest) readbackRequest).WaitForCompletion();
        } catch (Exception e) {
            Debug.LogError(e);
        } finally {
            readbackRequest = null;
        }
    }

    // Reads the vector field texture into the VectorField object. Will only run if not already running.
    public async Task ReadIntoCPUAsync() {
        if (renderTexture == null) {
            Debug.LogError("RenderTexture is not assigned.");
            return;
        }

        try {
            if (readbackRequest == null || ((AsyncGPUReadbackRequest) readbackRequest).done) {
                readbackRequest = await AsyncGPUReadback.RequestAsync(renderTexture, 0);
                ReadFromGPUCallback((AsyncGPUReadbackRequest)readbackRequest);
            }

            // if (vectorField == null) {
            //     ((AsyncGPUReadbackRequest) readbackRequest).WaitForCompletion();
            // }
        } catch (OperationCanceledException) {
            // Expected when a domain/script reload interrupts the in-flight GPU readback. Safe to ignore.
        } catch (Exception e) {
            Debug.LogError(e);
        } finally {
            readbackRequest = null;
        }

    }
    
    void ReadFromGPUCallback(AsyncGPUReadbackRequest request) {
        if (request.hasError) {
            Debug.LogError("AsyncGPUReadback encountered an error.");
            return;
        }
        var rawData = request.GetData<Color>();
        Vector2[] vectors = VectorFieldUtils.ColorsToVectors(rawData, 1);
        vectorField = new Vector2Map(new Point(request.width, request.height), vectors);
    }
}