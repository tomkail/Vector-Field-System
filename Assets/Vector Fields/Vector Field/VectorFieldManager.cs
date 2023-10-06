using UnityEngine;
using System.Collections;
using UnityEngine.Experimental.Rendering;

[ExecuteInEditMode]
public class VectorFieldManager : MonoSingleton<VectorFieldManager> {
	public GridRenderer gridRenderer;

	public Vector2Map vectorField;

	public float viscosity;
	public float damping;

	public Texture2D texture;

	public float magnitudeScaleFactor = 20;
	public Vector3 planeNormal => transform.forward;
	
//	public float speed;
//	public float amount;

	public delegate void OnUpdateDelegate (float deltaTime);
	public event OnUpdateDelegate OnUpdate;
	public event OnUpdateDelegate OnRender;
	
	
	#if UNITY_EDITOR
	[UnityEditor.Callbacks.DidReloadScripts]
	private static void OnReloadScripts () {
		if(Instance != null)
			Instance.Awake();
	}
	#endif
	
	protected override void Awake () {
		base.Awake();
		// vectorField = new Vector2Map(gridRenderer.gridSize, Vector2.zero);
		texture = TextureX.Create(vectorField.size, Color.black);
		texture.Apply();
	}

	void OnEnable () {
		Update();
	}

	void CreateTexture () {
		var colors = VectorFieldUtils.VectorsToColors(vectorField.values, 1f/VectorFieldScriptableObject.GetMaxAbsComponent(vectorField.values));
		texture.SetPixels(colors);
		texture.Apply();
	}

	public void Update () {
		//vectorField.Clear();

		//if(OnUpdate != null) OnUpdate(Time.deltaTime);

		Spread();
		
		CreateTexture();
		if(OnRender != null) OnRender(Time.fixedDeltaTime);
	}

	void Spread() {
		// Spread
//		Based on its speed, a certain amount of a cell will have moved position. If slow, only a small part moves a single cell. If fast, the entire part moves the entire cell.
		/// We meed to double buffer this so not to transfer accumlatively ovr a single frame.
		//		AddValues(vectorField, position, vector * Time.fixedDeltaTime);

//		Vector2Map clonedMap = new Vector2Map(vectorField.size);
//		foreach(var cell in vectorField) {
//			AddValues(clonedMap, (Vector2)cell.point + (Vector2.Scale(cell.value, gridToWorldScaleFactor) * speed), cell.value * amount);
//		}
//		vectorField.values = clonedMap.values;
//		vectorField.SetValueAtGridPoint(0,0, vectorField.GetValueAtGridPoint(0,0) + vector);
//		var current = vectorField.GetValueAtGridPoint(0,0);
//		current = current.normalized * MathX.Clamp0Infinity((current.magnitude - damping * Time.fixedDeltaTime));
//		vectorField.SetValueAtGridPoint(0,0, current);
//		// Damp
//		for (int i = 0; i < vectorField.values.Length; i++) {
//			var cell = vectorField.GetCellInfo(i);
		//			float newMagnitude = Mathf.Lerp (cell.value.magnitude, 0, damping * Time.fixedDeltaTime);
//			newMagnitude = MathX.Clamp0Infinity (newMagnitude);
//			vectorField [cell.index] = cell.value.normalized * newMagnitude;
//		}
	}

	public void AddValues (Vector2Map vectorField, Vector2 gridPosition, Vector2 value) {
		gridPosition = vectorField.ClampGridPosition(gridPosition);
		if(gridPosition.x.IsWhole() && gridPosition.y.IsWhole()) {
			Point point = new Point((int)gridPosition.x, (int)gridPosition.y);
			vectorField.SetValueAtGridPoint(point, vectorField.GetValueAtGridPoint(point) + value);
//			return GetValueAtGridPoint((int)gridPosition.x, (int)gridPosition.y);
		}

		int left = Mathf.FloorToInt(gridPosition.x);
		int right = left+1;
		int bottom = Mathf.FloorToInt(gridPosition.y);
		int top = bottom+1;

		float rightStrength = gridPosition.x - Mathf.FloorToInt(gridPosition.x);
		float leftStrength = 1-rightStrength;
		float topStrength = gridPosition.y - Mathf.FloorToInt(gridPosition.y);
		float bottomStrength = 1-topStrength;

		if(vectorField.IsOnGrid(right, top)) {
			vectorField.SetValueAtGridPoint(right, top, vectorField.GetValueAtGridPoint(right, top) + Vector2.Lerp(Vector2.zero, value, rightStrength * topStrength));
		}
		if(vectorField.IsOnGrid(left, top)) {
			vectorField.SetValueAtGridPoint(left, top, vectorField.GetValueAtGridPoint(left, top) + Vector2.Lerp(Vector2.zero, value, leftStrength * topStrength));
		}
		if(vectorField.IsOnGrid(left, bottom)) {
			vectorField.SetValueAtGridPoint(left, bottom, vectorField.GetValueAtGridPoint(left, bottom) + Vector2.Lerp(Vector2.zero, value, leftStrength * bottomStrength));
		}
		if(vectorField.IsOnGrid(right, bottom)) {
			vectorField.SetValueAtGridPoint(right, bottom, vectorField.GetValueAtGridPoint(right, bottom) + Vector2.Lerp(Vector2.zero, value, rightStrength * bottomStrength));
		}
	}

	public Vector3 EvaluateWorldVector(Vector3 position) {
		return transform.TransformVector(EvaluateVector(position));
	}

	public Vector2 EvaluateVector (Vector3 position) {
		var gridPosition = gridRenderer.cellCenter.WorldToGridPosition(position);
		return vectorField.GetValueAtGridPosition(gridPosition) * magnitudeScaleFactor;
	}

	public Quaternion EvaluateRotation(Vector3 position) {
		return Quaternion.LookRotation(EvaluateWorldVector(position), planeNormal);
	}
}