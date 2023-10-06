using UnityEngine;
using System.Collections;
using UnityX.Geometry;

public class VectorFieldScriptableObject : TypeMapScriptableObject<Vector2Map> {
	public override Vector2Map CreateMap () {
		if(texture == null) {
			Save(new Vector2Map(size));
		}
		Color[] colors = texture.GetPixels();
		Vector2[] vectors = VectorFieldUtils.ColorsToVectors(colors, maxComponent);
		return new Vector2Map(new Point(texture.width, texture.height), vectors);
	}

	public override void SetMaxComponent (Vector2Map vectorField) {
		maxComponent = GetMaxAbsComponent(vectorField.values);
	}

	public override Color[] GetMapColors (Vector2Map vectorField) {
		return VectorFieldUtils.VectorsToColors(vectorField.values, 1f/maxComponent);
	}
	
	

	/// <summary>
	/// Returns the largest absolute component in the list.
	/// </summary>
	/// <returns>The abs component index.</returns>
	/// <param name="values">Values.</param>
	public static float GetMaxAbsComponent (Vector2[] vectors) {
		if (vectors.Length == 0) return 0;
		static float MaxAbs(Vector2 v ){
			return Mathf.Max(Mathf.Abs(v.x), Mathf.Abs(v.y));
		}
		static int MaxAbsComponentIndex (Vector2[] values) {
			if(values.Length == 0) {
				Debug.LogError("Values is empty!");
				return -1;
			}
			

			int index = 0;
			float maxComponent = 0;
			float _tmpAbsComponent = 0;
			for(int i = 0; i < values.Length; i++){
				_tmpAbsComponent = MaxAbs(values[i]);
				if(_tmpAbsComponent > maxComponent) {
					maxComponent = _tmpAbsComponent;
					index = i;
				}
			}
			return index;
		}
		int index = MaxAbsComponentIndex(vectors);
		return MaxAbs(vectors[index]);
	}

	public static float LargestMagnitude(Vector2[] vectorFieldValues) {
		return vectorFieldValues.Max(x => x.magnitude);
	}
}