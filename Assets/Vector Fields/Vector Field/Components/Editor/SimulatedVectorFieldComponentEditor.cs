using UnityEngine.UIElements;
using UnityEditor;
using UnityEditor.UIElements;

[CustomEditor(typeof(SimulatedVectorFieldComponent)), CanEditMultipleObjects]
public class SimulatedVectorFieldComponentEditor : VectorFieldComponentEditor {
	protected override void BuildBody(VisualElement root) {
		var sim = VectorFieldInspectorUI.MakeSection("Simulation", ViewKey("sim"));
		sim.Add(new PropertyField(serializedObject.FindProperty("simulationFps"), "Solver Rate (FPS)"));
		sim.Add(new PropertyField(serializedObject.FindProperty("maxSubstepsPerFrame"), "Max Substeps / Frame"));
		sim.Add(new PropertyField(serializedObject.FindProperty("timeScale"), "Time Scale"));
		sim.Add(new PropertyField(serializedObject.FindProperty("pressureIterations"), "Pressure Iterations"));
		sim.Add(new PropertyField(serializedObject.FindProperty("viscosityDamp"), "Viscosity Damp"));
		sim.Add(new PropertyField(serializedObject.FindProperty("simulateInEditMode"), "Simulate In Edit Mode"));
		root.Add(sim);

		var advection = VectorFieldInspectorUI.MakeSection("Advection", ViewKey("advection"));
		advection.Add(new PropertyField(serializedObject.FindProperty("advectionMode"), "Mode"));
		root.Add(advection);

		var vorticity = VectorFieldInspectorUI.MakeSection("Vorticity Confinement", ViewKey("vorticity"));
		vorticity.Add(new PropertyField(serializedObject.FindProperty("vorticityStrength"), "Strength"));
		root.Add(vorticity);

		var forcing = VectorFieldInspectorUI.MakeSection("Forcing", ViewKey("forcing"));
		var forceFieldProp = serializedObject.FindProperty("forceField");
		forcing.Add(new PropertyField(forceFieldProp, "Force Field"));

		var mappingProp = serializedObject.FindProperty("forceMapping");
		var mapping = new PropertyField(mappingProp, "Mapping");
		var strength = new PropertyField(serializedObject.FindProperty("forceStrength"), "Strength");
		var worldHelp = VectorFieldInspectorUI.Help("WorldSpace samples the force field by world position, so moving / rotating / resizing it drives the fluid.");
		forcing.Add(mapping);
		forcing.Add(worldHelp);
		forcing.Add(strength);

		// Mapping and strength only matter once a force field is assigned; the world-space note only when that mode is picked.
		VectorFieldInspectorUI.ShowIf(mapping, forceFieldProp, () => forceFieldProp.objectReferenceValue != null);
		VectorFieldInspectorUI.ShowIf(strength, forceFieldProp, () => forceFieldProp.objectReferenceValue != null);
		VectorFieldInspectorUI.ShowIf(worldHelp, serializedObject, () =>
			forceFieldProp.objectReferenceValue != null &&
			mappingProp.enumValueIndex == (int)SimulatedVectorFieldComponent.ForceMapping.WorldSpace);
		root.Add(forcing);

		var boundaries = VectorFieldInspectorUI.MakeSection("Boundaries", ViewKey("boundaries"));
		boundaries.Add(new PropertyField(serializedObject.FindProperty("boundaryMode"), "Edge Mode"));
		boundaries.Add(new PropertyField(serializedObject.FindProperty("obstacles"), "Obstacles"));
		root.Add(boundaries);

		var output = VectorFieldInspectorUI.MakeSection("Output", ViewKey("output"));
		output.Add(new PropertyField(serializedObject.FindProperty("outputScale"), "Output Scale"));
		root.Add(output);
	}
}
