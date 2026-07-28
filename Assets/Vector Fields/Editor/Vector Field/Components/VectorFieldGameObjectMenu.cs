using UnityEditor;
using UnityEngine;

namespace VectorFields {
	// GameObject > Vector Fields > ... create-menu entries. These mirror the [AddComponentMenu] names on the
	// concrete VectorFieldComponent subclasses, but create a new GameObject with the component already attached
	// (parented to the right-clicked object / current stage, undoable, and selected) — the standard Unity pattern.
	static class VectorFieldGameObjectMenu {
		const string Root = "GameObject/Vector Fields/";
		// GameObject-menu priority; groups these entries together near the top of the create menu.
		const int Priority = 10;

		[MenuItem(Root + "Drawable Vector Field", false, Priority)]
		static void CreateDrawable(MenuCommand cmd) => Create<DrawableVectorFieldComponent>("Drawable Vector Field", cmd);

		[MenuItem(Root + "Group Vector Field", false, Priority)]
		static void CreateGroup(MenuCommand cmd) => Create<GroupVectorFieldComponent>("Group Vector Field", cmd);

		[MenuItem(Root + "Mesh Vector Field", false, Priority)]
		static void CreateMesh(MenuCommand cmd) => Create<MeshVectorField>("Mesh Vector Field", cmd);

		[MenuItem(Root + "Noise Vector Field", false, Priority)]
		static void CreateNoise(MenuCommand cmd) => Create<NoiseVectorFieldComponent>("Noise Vector Field", cmd);

		[MenuItem(Root + "Simulated Vector Field", false, Priority)]
		static void CreateSimulated(MenuCommand cmd) => Create<SimulatedVectorFieldComponent>("Simulated Vector Field", cmd);

	#if VECTOR_FIELDS_SPLINES
		// Only exists while the optional com.unity.splines package is installed (same define the component compiles
		// under). Also adds the SplineContainer the field traces — assigned into the inspector reference (the component's
		// GetComponent fallback would find it anyway, but an explicit reference is visible) and seeded so the created
		// field shows itself off out of the box: the field spans the unit square (±0.5) in its local plane, so a rounded
		// three-knot closed loop comfortably inside it demonstrates curving flow, and a small width leaves the falloff
		// fade clearly visible around the loop.
		[MenuItem(Root + "Spline Vector Field", false, Priority)]
		static void CreateSpline(MenuCommand cmd) {
			var go = Create("Spline Vector Field", cmd, typeof(UnityEngine.Splines.SplineContainer), typeof(SplineVectorFieldComponent));
			var container = go.GetComponent<UnityEngine.Splines.SplineContainer>();
			var field = go.GetComponent<SplineVectorFieldComponent>();
			field.splineContainer = container;
			field.width = 0.1f;

			var spline = container.Spline;
			const float radius = 0.3f;
			for (int i = 0; i < 3; i++) {
				float angle = (90f + i * 120f) * Mathf.Deg2Rad;
				spline.Add(new UnityEngine.Splines.BezierKnot(new Unity.Mathematics.float3(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius, 0f)),
					UnityEngine.Splines.TangentMode.AutoSmooth);
			}
			spline.Closed = true;
		}
	#endif

		[MenuItem(Root + "Stamp Vector Field", false, Priority)]
		static void CreateStamp(MenuCommand cmd) => Create<StampVectorFieldComponent>("Stamp Vector Field", cmd);

		[MenuItem(Root + "Wave Vector Field", false, Priority)]
		static void CreateWave(MenuCommand cmd) => Create<WaveVectorFieldComponent>("Wave Vector Field", cmd);

		static void Create<T>(string name, MenuCommand cmd) where T : VectorFieldComponent => Create(name, cmd, typeof(T));

		static GameObject Create(string name, MenuCommand cmd, params System.Type[] componentTypes) {
			// ObjectFactory places the object in the active stage/scene and registers the creation undo.
			var go = ObjectFactory.CreateGameObject(name, componentTypes);
			GameObjectUtility.SetParentAndAlign(go, cmd.context as GameObject);
			Undo.SetCurrentGroupName("Create " + name);
			Selection.activeGameObject = go;
			EditorGUIUtility.PingObject(go);
			return go;
		}
	}
}
