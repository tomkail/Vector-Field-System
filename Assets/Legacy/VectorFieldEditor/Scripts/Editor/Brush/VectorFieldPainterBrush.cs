using UnityEngine;
using System.Collections;
using UnityX.Geometry;

[System.Serializable]
public class VectorFieldPainterBrush {

	public enum BrushMode {
		Brush,
		Pencil,
		Custom
	}

	private int _size = 20;
	public int size {
		get => _size;
		set {
			if(_size == value) return;
			_size = Mathf.Clamp(value, minSize, maxSize);
			SetBrushPixels();
		}
	}
	public int minSize = 1;
	public int maxSize = 20;

	public AnimationCurve targetSizeCurve = AnimationCurve.Linear(0,0,200,10);
	public bool sizeDistanceTweening;
	public float sizeDistanceTweenTime = 0;

	public float sizeReciprocal => 1f/_size;

	public float radius => size * 0.5f;


	public BrushMode _mode;
	public BrushMode mode {
		get => _mode;
		set {
			if(_mode == value) return;
			_mode = value;
			RefreshIntensityMap();
		}
	}
	
	public Texture2D texture;
	Color[] pixels;
	private float[] brushPixels;

	private float _brushHardness = 0.8f;
	public float brushHardness {
		get => _brushHardness;
		set {
			if(_brushHardness == value) return;
			_brushHardness = Mathf.Clamp(value, minHardness, maxHardness);
			RefreshIntensityMap();
		}
	}
	public float minHardness = 0;
	public float maxHardness = 0.99f;

	public AnimationCurve brushShape => AnimationCurve.Linear(1f-brushHardness, 1, 0, 0);

	public HeightMap intensityMap {get;private set;}

	public VectorFieldPainterBrush () {}

	public VectorFieldPainterBrush (int size) {
		this.size = size;
	}
	
	public virtual void Init () {
//		if(vectorField != null) {
//			size = MathX.Clamp1Infinity((((Vector2)vectorField.size).Largest() * 0.1f)).RoundToInt();
//		}
		SetBrushTexture(null);
		mode = BrushMode.Brush;
		RefreshIntensityMap();
	}
	
	private void SetBrushTexture (Texture2D tex) {
		texture = tex;
		if(texture == null) {
			pixels = null;
			brushPixels = null;
		} else {
			SetBrushPixels();
		}
	}
	
	
	private void SetBrushPixels () {
		if(size == 0) brushPixels = new float[]{0};
		else {
			RenderTexture rt = RenderTexture.GetTemporary(size, size, 0);
			Graphics.Blit (texture, rt);
			pixels = GetPixels(rt);
			brushPixels = ColorX.ColorArrayToAlphaFloatArray(pixels);
			RenderTexture.ReleaseTemporary(rt);
		}
		RefreshIntensityMap();
	}
	
	static public Color[] GetPixels (RenderTexture rt) {
		Texture2D texture = new Texture2D(rt.width, rt.height);
		
		// Store active render texture
		RenderTexture lastActiveRT = RenderTexture.active;
		
		// Set the supplied RenderTexture as the active one
		RenderTexture.active = rt;
		
		// Create a new Texture2D and read the RenderTexture image into it
		texture.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0);
		Color[] pixels = texture.GetPixels();
		
		// Restore previously active render texture
		RenderTexture.active = lastActiveRT;
		return pixels;
	}

	protected virtual void RefreshIntensityMap () {
		Vector2 center = Vector2.one * (size-1) * 0.5f;
		if(mode == BrushMode.Brush) {
			intensityMap = new HeightMap(new Point(size,size));
			if(size <= 2) {
				intensityMap.values.Fill(1);
				return;
			}
			Vector2 centeredVector;
			float tmpRadius;
			float brushPressure;
			foreach(TypeMapCellInfo<float> cellInfo in intensityMap) {
				centeredVector = (Vector2)cellInfo.point - center;
				tmpRadius = centeredVector.magnitude;
				if(tmpRadius < radius) {
					brushPressure = Mathf.Clamp01((((size+1) * 0.5f)-tmpRadius) / radius);
					brushPressure = brushShape.Evaluate(brushPressure);
					intensityMap[cellInfo.index] = brushPressure;
				}
			}
		} else if(mode == BrushMode.Pencil) {
			intensityMap = new HeightMap(new Point(size,size), 1);
		} else if(mode == BrushMode.Custom && brushPixels != null) {
			intensityMap = new HeightMap(new Point(size,size), brushPixels);
		} else {
			Debug.LogError("Mode "+mode+" not recognized");
		}
	}

//	public virtual float GetBrushIntensity (Vector2 targetPosition) {
//		float brushPressure = 0;
//		
//		if(mode == BrushMode.Custom && brushPixels != null) {
//			Point targetPoint = new Point(Mathf.FloorToInt(targetPosition.x + size * 0.5f), Mathf.FloorToInt(targetPosition.y + size * 0.5f));
//			Point relativeTargetPoint = targetPoint;
//			int brushIndex = Grid.GridPointToArrayIndex(relativeTargetPoint, size);
//			if(brushPixels.ContainsIndex(brushIndex)) {
//				brushPressure = brushPixels[brushIndex];
//			}
//		} else if(mode == BrushMode.Pencil) {
//			brushPressure = 1;
//		} else {
//			float tmpRadius = targetPosition.magnitude;
//			if(tmpRadius < radius) {
//				brushPressure = Mathf.Clamp01((radius-tmpRadius) / radius);
//				brushPressure = brushShape.Evaluate(brushPressure);
//			}
//		}
//		
//		return brushPressure;
//	}
}