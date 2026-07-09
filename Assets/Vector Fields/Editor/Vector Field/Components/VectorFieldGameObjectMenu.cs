using UnityEditor;
using UnityEngine;

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

	[MenuItem(Root + "Stamp Vector Field", false, Priority)]
	static void CreateStamp(MenuCommand cmd) => Create<StampVectorFieldComponent>("Stamp Vector Field", cmd);

	[MenuItem(Root + "Wave Vector Field", false, Priority)]
	static void CreateWave(MenuCommand cmd) => Create<WaveVectorFieldComponent>("Wave Vector Field", cmd);

	static void Create<T>(string name, MenuCommand cmd) where T : VectorFieldComponent {
		// ObjectFactory places the object in the active stage/scene and registers the creation undo.
		var go = ObjectFactory.CreateGameObject(name, typeof(T));
		GameObjectUtility.SetParentAndAlign(go, cmd.context as GameObject);
		Undo.SetCurrentGroupName("Create " + name);
		Selection.activeGameObject = go;
		EditorGUIUtility.PingObject(go);
	}
}
