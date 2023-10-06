using UnityEngine;
using UnityEditor;
using System.Collections;

[CustomEditor(typeof(VectorFieldTurbulence)), CanEditMultipleObjects]
public class VectorFieldTurbulenceEditor : BaseEditor<VectorFieldTurbulence> {

	private static FilterMode filterMode = FilterMode.Point;
	private static int resolution = 128;
	private static bool fitThisFrame = true;
    public override bool HasPreviewGUI() {return true;}

    public override void OnPreviewGUI(Rect r, GUIStyle background) {
        if (Event.current.type == EventType.Repaint) {
			if(fitThisFrame) {
				resolution = Mathf.RoundToInt(Mathf.Min(r.size.x, r.size.y));
				fitThisFrame = false;
			}
			Vector2Map vectorMap = new Vector2Map(new Point(resolution, resolution));
			VectorFieldTurbulence turbulence = (VectorFieldTurbulence)target;
			foreach(TypeMapCellInfo<Vector2> sample in vectorMap) {
				vectorMap[sample.index] = turbulence.Sample(sample.point);
			}
//			Debug.Log(Vector2X.LargestMagnitude(vectorMap.values));
			Color[] colors = VectorFieldUtils.VectorsToColors(vectorMap.values, 1f/vectorMap.values.Max(x => x.magnitude));
			Texture2D texture = TextureX.Create(vectorMap.size, colors, filterMode);
        	texture.Apply();
			GUI.DrawTexture(r, texture, ScaleMode.ScaleToFit, false);
        }
    }

    public override void OnPreviewSettings() {
		filterMode = (FilterMode)EditorGUILayout.EnumPopup(filterMode, EditorStyles.toolbarDropDown, GUILayout.Width(60));
		resolution = EditorGUILayout.IntSlider(resolution, 1, 256, GUILayout.Width(160));
		if (GUILayout.Button("Fit", EditorStyles.toolbarButton)) {
			fitThisFrame = true;
		}
    }
}