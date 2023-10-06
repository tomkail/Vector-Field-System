using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using UnityX.Geometry;
using System.Linq;

public class VectorFieldTurbulence : MonoBehaviour {
	public VectorFieldManager vectorFieldManager;

	[Range(0,2)]
	public float strength = 1;

	public NoiseSampler detailSampler;
	public NoiseSampler amplitudeSampler;

	[Header("The floor of the amplitude, as (max amplitude - this)")]
	public float amplitudeFloorAsHeightFromCeiling = 0.2f;

	public float frequency = 1;
	public float lacunarity = 2f;
//	public float persistence = 0.5f;
	[Header("Scales the output based on the dot product with the real vector field")]
	public AnimationCurve flowCurlDotProductMultiplier;

	public float epsilon = 100;

	[ButtonAttribute("ExportVectorFieldTextureWithTurbulence")]
	public bool buttonProxy;
	public void ExportVectorFieldTextureWithTurbulence () {
		/*
		#if UNITY_EDITOR
		string path = UnityEditor.EditorUtility.SaveFilePanelInProject("Save", "texture.png", "png", "Save vector field with turbulence texture");
		if (path.Length > 0) {
			Vector2Map vectorField = vectorFieldManager.vectorFieldScriptableObject.CreateMapWithTurbulence();
			Texture2D texture = vectorFieldManager.vectorFieldScriptableObject.CreateTexture(vectorField);


			ScreenshotExportSettings exportSettings = null;
			Debug.Log(System.IO.Path.GetDirectoryName(path)+" "+ System.IO.Path.GetFileName(path));
			exportSettings = new ScreenshotExportSettings(texture, System.IO.Path.GetDirectoryName(path), System.IO.Path.GetFileName(path), ScreenshotExportFormat.PNG);
			ScreenshotExporter.Export(exportSettings);

			UnityEditor.TextureImporter textureImporter = (UnityEditor.TextureImporter)UnityEditor.AssetImporter.GetAtPath(exportSettings.fullFilePath);
			UnityEditor.TextureImporterSettings importerSettings = new UnityEditor.TextureImporterSettings();
			importerSettings.readable = true;
			importerSettings.mipmapEnabled = false;
			importerSettings.filterMode = FilterMode.Point;
			importerSettings.textureFormat = UnityEditor.TextureImporterFormat.AutomaticTruecolor;
			importerSettings.maxTextureSize = Mathf.Max(texture.width, texture.height);
			textureImporter.SetTextureSettings(importerSettings);
			UnityEditor.AssetDatabase.ImportAsset(exportSettings.fullFilePath, UnityEditor.ImportAssetOptions.ForceUpdate);

			Texture2D textureAsset = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>(exportSettings.fullFilePath);
			texture = textureAsset;
			UnityEditor.EditorUtility.SetDirty(this);
			UnityEditor.AssetDatabase.SaveAssets();
		}
		#endif
		*/
	}

	public Vector2 GetValueAtIndex (Vector2Map vectorField, int index) {
		return GetValueAtGridPoint(vectorField, vectorField.ArrayIndexToGridPoint(index));
	}

	public Vector2 GetValueAtGridPoint (Vector2Map vectorField, Point gridPoint) {
//		Vector3 worldPosition = vectorFieldManager.GridPositionToWorldPosition(gridPoint);
		Vector2 vectorFieldVector = vectorField.GetValueAtGridPoint(gridPoint);
		return GetValInternal(gridPoint, vectorFieldVector);
	}

	public Vector2 GetValueAtGridPosition (Vector2Map vectorField, Vector2 gridPosition) {
//		Vector3 worldPosition = vectorFieldManager.GridPositionToWorldPosition(gridPosition);
		Vector2 vectorFieldVector = vectorField.GetValueAtGridPosition(gridPosition);
		return GetValInternal(gridPosition, vectorFieldVector);
	}

	public Vector2 GetValueAtWorldPosition (Vector3 worldPosition) {
		Vector2 gridPosition = vectorFieldManager.gridRenderer.cellCenter.WorldToGridPosition(worldPosition);
		Vector2 vectorFieldVector = vectorFieldManager.vectorField.GetValueAtGridPosition(gridPosition);
		return GetValInternal(gridPosition, vectorFieldVector);
	}


	private Vector2 GetValInternal (Vector2 gridPosition, Vector2 vectorFieldVector) {
		if(strength == 0)
			return Vector2.zero;
		
		Vector2 curlNoiseVector = GetProcessedRawValue(gridPosition);

		float dot = Vector2.Dot(vectorFieldVector.normalized, curlNoiseVector.normalized);
		return curlNoiseVector * flowCurlDotProductMultiplier.Evaluate(dot) * strength * vectorFieldVector.magnitude;
	}

	// Gets the value of the turbulence field using the amplitude clamps 
	public Vector2 GetProcessedRawValue (Vector2 gridPosition) {
		Vector2 curlNoiseVector = Sample(gridPosition);

		float maxAmplitude = amplitudeSampler.SampleAtPosition(gridPosition).value;
		float minAmplitude = Mathf.Max(0, maxAmplitude - amplitudeFloorAsHeightFromCeiling);
		float currentAmplitude = curlNoiseVector.magnitude;
		float targetAmplitude = Mathf.Clamp(currentAmplitude, minAmplitude, maxAmplitude);
		if(currentAmplitude != targetAmplitude) {
			curlNoiseVector = curlNoiseVector.normalized * targetAmplitude;
		}
		return curlNoiseVector;
	}

	// Gets the raw value (no amplitude applied) of the turbulence field
	public Vector2 Sample (Vector2 position) {
		float detail = detailSampler.SampleAtPosition(position).value;


//		NoiseMethod method = Noise.methods[(int)NoiseMethodType.Perlin][1];
		float n1 = PerlinOctaveLerp(new Vector3(position.x, position.y + epsilon), detail);
		float n2 = PerlinOctaveLerp(new Vector3(position.x, position.y - epsilon), detail);
		float a = (n1 - n2) / (2 * epsilon);

		n1 = PerlinOctaveLerp(new Vector3(position.x + epsilon, position.y), detail);
		n2 = PerlinOctaveLerp(new Vector3(position.x - epsilon, position.y), detail);
		float b	= (n1 - n2) / (2 * epsilon);

		// Multiply by a magic fudge factor to make it work properly. I don't really know why we need this.
		return new Vector2(a, -b) * 6.792f;

//		NoiseMethod method = Noise.methods[(int)NoiseMethodType.Perlin][2];
//
//		Vector3 point = new Vector3(position.z, position.y, position.x);
//		NoiseSample sampleX = Noise.Sum(method, point, frequency, octaves, lacunarity, persistence);
//
//		point = new Vector3(position.x + 100f, position.z, position.y);
//		NoiseSample sampleY = Noise.Sum(method, point, frequency, octaves, lacunarity, persistence);
//
//		sampleX *= amplitude;
//		sampleY *= amplitude;
//
//		Vector2 curl;
//		curl.x = sampleZ.derivative.x - sampleY.derivative.y;
//		curl.y = sampleX.derivative.x - sampleZ.derivative.y;
//		return curl;
	}

	// Gets a perlin noise value derived from a lerp of the two values originating from adjacent octaves of noise.
	public float PerlinOctaveLerp (Vector3 position, float octave) {
		NoiseMethod method = Noise.methods[(int)NoiseMethodType.Perlin][1];
		int octaveA = Mathf.FloorToInt(octave);
		float lerp = octave - octaveA;
//		float octaveFrequency = Mathf.Pow(frequency, lacunarity);
		float octaveFrequency = frequency;
		for (int o = 0; o < octaveA; o++) {
			octaveFrequency *= lacunarity;
		}
		float n1 = method(position, octaveFrequency).value;
		octaveFrequency *= lacunarity;
		float n2 = method(position, octaveFrequency).value;
		return Mathf.Lerp(n1, n2, lerp);
	}
}