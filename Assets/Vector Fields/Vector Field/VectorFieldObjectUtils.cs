using UnityEngine;

namespace VectorFields {
	// Small self-contained object helpers, replacing the UnityX ObjectX utilities the plugin used so it depends only on
	// UnityEngine.
	public static class VectorFieldObjectUtils {
		// Destroy an object using the correct call for the current play-mode state: Destroy in play mode,
		// DestroyImmediate in the editor. Null-safe.
		public static void DestroyAutomatic(Object o) {
			if (o == null) return;
	#if UNITY_EDITOR
			if (Application.isPlaying) Object.Destroy(o);
			else Object.DestroyImmediate(o);
	#else
			Object.Destroy(o);
	#endif
		}
	}
}
