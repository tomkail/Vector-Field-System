using System;
using UnityEngine;

// Code-callable polygon vector field generator (CPU). For each grid cell it points toward (or away from) the nearest
// polygon edge, restricted to the chosen side(s) and shaped by a distance falloff. MonoBehaviour-free: give it the
// target map, a grid-cell -> world mapping, the polygon, and the relevant transforms.
public static class PolygonVectorFieldGenerator {
	// Which sides of the shape get a vector. Enable both for the whole grid.
	[Flags]
	public enum Sides {
		None = 0,
		Inside = 1 << 0,
		Outside = 1 << 1,
	}

	public enum BoundaryFlip {
		None,
		FlipInside,
		FlipOutside,
	}

	// Fills `target` with the polygon field. `gridPointToWorld` maps a grid cell to a world point;
	// `polygonWorldToLocal`/`polygonLocalToWorld` move points/vectors between world and the polygon's local space;
	// `fieldWorldToLocal` brings the result back into the destination field's local space.
	public static void Generate(
		Vector2Map target,
		Func<Point, Vector3> gridPointToWorld,
		Polygon polygon,
		Matrix4x4 polygonWorldToLocal,
		Matrix4x4 polygonLocalToWorld,
		Matrix4x4 fieldWorldToLocal,
		Sides sides, BoundaryFlip boundaryFlip, float innerFalloff, float outerFalloff, float angle, float magnitude) {
		if (target == null || polygon == null) return;

		bool wantInside = (sides & Sides.Inside) != 0;
		bool wantOutside = (sides & Sides.Outside) != 0;
		// Precompute the rotation (around the plane normal) applied to every vector.
		float rad = angle * Mathf.Deg2Rad;
		float cos = Mathf.Cos(rad), sin = Mathf.Sin(rad);

		foreach (var cell in target) {
			var worldPoint = gridPointToWorld(cell.point);
			var polygonPoint = (Vector2)polygonWorldToLocal.MultiplyPoint3x4(worldPoint);

			// Restrict to the chosen side(s) of the shape; cells on an inactive side stay zeroed.
			bool inside = polygon.ContainsPoint(polygonPoint);
			if (inside ? !wantInside : !wantOutside) {
				target[cell.index] = Vector2.zero;
				continue;
			}

			var closestPoint = polygon.FindClosestPointOnPolygon(polygonPoint);
			var toEdge = closestPoint - polygonPoint; // points toward the nearest edge
			float distance = toEdge.magnitude;
			// Outward (away from the shape) is continuous across the boundary: inside points toward its nearest edge,
			// outside points away from it, so both sides flow the same way by default.
			Vector2 outward = inside ? toEdge : -toEdge;
			Vector2 direction = distance > 1e-5f ? outward / distance : Vector2.zero;

			// Reverse one side to converge on / diverge from the outline.
			if ((inside && boundaryFlip == BoundaryFlip.FlipInside) || (!inside && boundaryFlip == BoundaryFlip.FlipOutside))
				direction = -direction;
			// Rotate around the plane normal (2D rotation in polygon space).
			if (angle != 0f) direction = new Vector2(direction.x * cos - direction.y * sin, direction.x * sin + direction.y * cos);

			// Full strength at the edge, fading to zero `falloff` units away (0 = constant strength). Inside and
			// outside regions use their own falloff distance.
			float falloff = inside ? innerFalloff : outerFalloff;
			float strength = falloff > 0f ? Mathf.Clamp01(1f - distance / falloff) : 1f;
			var vector = direction * (strength * magnitude);

			var worldVector = polygonLocalToWorld.MultiplyVector((Vector3)vector);
			target[cell.index] = (Vector2)fieldWorldToLocal.MultiplyVector(worldVector);
		}
	}
}
