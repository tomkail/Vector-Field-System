using UnityEngine;

// Abstract base for components that lay a quad over a vector field's world rect and keep it aligned as the field (or
// our own parent) moves. Owns ONLY the alignment concern — the matchFieldBounds/depthOffset controls and the
// LateUpdate that pins the quad via VectorFieldRendererUtils.MatchFieldRect. Subclasses decide what the quad actually
// displays: VectorFieldTextureRenderer binds the field's live texture; VectorFieldFlowIBFV runs a feedback loop and
// shows its own accumulation buffer. That's why display isn't in here — only the shared "quad follows field" plumbing.
public abstract class VectorFieldQuad : MonoBehaviour {
	// When on (the default) the quad is pinned over the field's world rect every tick — position, rotation, and size
	// all driven by the field. Turn it off to place and size the quad yourself (the script then never touches the
	// transform); note the mesh is a unit quad, so size it to cover the field or the texture won't line up.
	[SerializeField] protected bool matchFieldBounds = true;

	// Shifts the quad along the field's plane normal (forward = positive) for draw-order control — push it in front of
	// / behind other geometry. Ignored when matchFieldBounds is off.
	[SerializeField] protected float depthOffset;

	// The field this quad follows. Subclasses expose their own serialized reference — the names and lifecycles differ
	// (the texture renderer subscribes to OnRendered; IBFV polls in LateUpdate) — so the base reads it through this.
	protected abstract VectorFieldComponent Field { get; }

	protected MeshRenderer meshRenderer => GetComponent<MeshRenderer>();

	// Re-align every tick (not just on the field's OnRendered) so the quad tracks moves of our own parent — which
	// don't re-render the field. [ExecuteAlways] on the concrete subclass runs this in edit mode too, on every repaint.
	protected virtual void LateUpdate() {
		MatchFieldBounds();
	}

	// Lay the quad over the field's world rect (a unit-quad mesh centred at the origin maps exactly onto it). See
	// VectorFieldRendererUtils.MatchFieldRect for the position/orientation/scale solve.
	protected void MatchFieldBounds() {
		if (!matchFieldBounds) return;
		VectorFieldRendererUtils.MatchFieldRect(transform, Field, depthOffset);
	}
}
