using UnityEngine.UIElements;
using UnityEditor;
using UnityEditor.UIElements;

[CustomEditor(typeof(SimulatedVectorFieldComponent)), CanEditMultipleObjects]
public class SimulatedVectorFieldComponentEditor : VectorFieldComponentEditor {
	protected override void BuildBody(VisualElement root) {
		var sim = VectorFieldInspectorUI.MakeSection("Simulation", ViewKey("sim"));
		sim.Add(VectorFieldInspectorUI.Field(serializedObject.FindProperty("simulationFps"), "Solver Rate (FPS)",
			"Fixed solver rate. The sim steps in increments of 1/this regardless of frame rate."));
		sim.Add(VectorFieldInspectorUI.Field(serializedObject.FindProperty("maxSubstepsPerFrame"), "Max Substeps / Frame",
			"Cap on solver steps per frame, so a hitch can't spiral into a death-loop of catch-up steps."));
		sim.Add(VectorFieldInspectorUI.Field(serializedObject.FindProperty("timeScale"), "Time Scale",
			"Simulated seconds per real second — how fast the fluid evolves. Raise it (e.g. 10–30) to get visible flow at a high, smooth step rate."));
		sim.Add(VectorFieldInspectorUI.Field(serializedObject.FindProperty("pressureIterations"), "Pressure Iterations",
			"Jacobi iterations for the pressure solve. More = more accurately incompressible, but costlier. 20–40 is typical."));
		sim.Add(VectorFieldInspectorUI.Field(serializedObject.FindProperty("viscosityDamp"), "Viscosity Damp",
			"Per-step velocity damping. 1 = inviscid (energy persists), lower fakes viscosity / drag."));
		sim.Add(VectorFieldInspectorUI.Field(serializedObject.FindProperty("simulateInEditMode"), "Simulate In Edit Mode",
			"Run the sim in edit mode too. Off by default — sims are usually only meaningful while playing."));
		root.Add(sim);

		var advection = VectorFieldInspectorUI.MakeSection("Advection", ViewKey("advection"));
		advection.Add(VectorFieldInspectorUI.Field(serializedObject.FindProperty("advectionMode"), "Mode",
			"MacCormack cancels most of the numerical diffusion that makes plain semi-Lagrangian flow decay to mush (~2× advection cost)."));
		root.Add(advection);

		var vorticity = VectorFieldInspectorUI.MakeSection("Vorticity Confinement", ViewKey("vorticity"));
		vorticity.Add(VectorFieldInspectorUI.Field(serializedObject.FindProperty("vorticityStrength"), "Strength",
			"Re-injects the small-scale swirl that diffusion eats, keeping the flow lively. 0 disables it; 0.1–0.5 is a useful range."));
		root.Add(vorticity);

		var forcing = VectorFieldInspectorUI.MakeSection("Forcing", ViewKey("forcing"));
		var forceFieldProp = serializedObject.FindProperty("forceField");
		forcing.Add(VectorFieldInspectorUI.Field(forceFieldProp, "Force Field",
			"Another vector field whose output is injected as a continuous force each step (e.g. Noise for gusty wind, Stamp for a fan)."));

		var mappingProp = serializedObject.FindProperty("forceMapping");
		var mapping = VectorFieldInspectorUI.Field(mappingProp, "Mapping",
			"How the force field maps onto the sim. DirectTexel: 1:1 cell copy. WorldSpace: transform-aware. Stretched: fill the sim, ignoring transform.");
		var strength = VectorFieldInspectorUI.Field(serializedObject.FindProperty("forceStrength"), "Strength",
			"Multiplier on the injected force.");
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
		boundaries.Add(VectorFieldInspectorUI.Field(serializedObject.FindProperty("boundaryMode"), "Edge Mode",
			"What happens at the domain edges. Wrap = periodic/tiling; Wall = solid box; Open = outflow/absorbing."));
		boundaries.Add(VectorFieldInspectorUI.Field(serializedObject.FindProperty("obstacles"), "Obstacles",
			"Optional mask the fluid flows around (>0.5 = solid). Independent of the edge mode above."));
		root.Add(boundaries);

		var output = VectorFieldInspectorUI.MakeSection("Output", ViewKey("output"));
		output.Add(VectorFieldInspectorUI.Field(serializedObject.FindProperty("outputScale"), "Output Scale",
			"Scales raw solver velocity into the encoded [-1,1] field range before it enters the pipeline."));
		root.Add(output);
	}
}
