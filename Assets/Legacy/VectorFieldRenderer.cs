using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[ExecuteAlways]
public class VectorFieldRenderer : MonoBehaviour {
	VectorFieldManager _vectorFieldManager;
	public VectorFieldManager vectorFieldManager {
		get {
			if(_vectorFieldManager == null) _vectorFieldManager = VectorFieldManager.Instance;
			return _vectorFieldManager;
		} set {
			if(_vectorFieldManager == value) return;
			_vectorFieldManager = value;
			Update();
		}
	}

	[SerializeField]
	Material materialPrefab;
	Material material;

	private MeshRenderer meshRenderer => GetComponent<MeshRenderer>();

	void Awake () {
		vectorFieldManager = VectorFieldManager.Instance;
		material = new Material(materialPrefab);
		material.SetTexture("_MainTex", vectorFieldManager.texture);
		meshRenderer.material = material;
	}

	void Update () {
		transform.localScale = new Vector3(vectorFieldManager.gridRenderer.gridSize.x, vectorFieldManager.gridRenderer.gridSize.y, 1);
	}
}