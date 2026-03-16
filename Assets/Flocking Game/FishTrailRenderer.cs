using UnityEngine;
using System.Collections.Generic;

[RequireComponent(typeof(LineRenderer))]
public class FishTrailRenderer : MonoBehaviour {
	[Header("Trail Settings")]
	[Tooltip("Total length of the trail in world units")]
	public float trailLength = 2f;

	[Tooltip("Distance between trail points")]
	public float segmentLength = 0.1f;

	[Tooltip("How far ahead of the fish the trail should start")]
	public float startOffset = 0.2f;

	[Tooltip("Maximum time between trail points, regardless of distance moved")]
	public float maxTimeBetweenPoints = 0.05f;

	private LineRenderer lineRenderer;
	private Queue<Vector3> trailPoints;
	private Vector3 lastRecordedPosition;
	private float distanceSinceLastPoint;
	private float timeSinceLastPoint;
	private Fish fish;

	void Awake() {
		lineRenderer = GetComponent<LineRenderer>();
		fish = GetComponentInParent<Fish>();
		SetupLineRenderer();

		trailPoints = new Queue<Vector3>();
		lastRecordedPosition = GetTrailStartPosition();
		timeSinceLastPoint = 0;

		// Initialize with first point
		trailPoints.Enqueue(GetTrailStartPosition());
	}

	Vector3 GetTrailStartPosition() {
		// Use the object's forward direction (based on its rotation) to offset the start position
		return transform.position + (transform.right * startOffset);
	}

	void LateUpdate() {
		Vector3 currentTrailStart = GetTrailStartPosition();

		// Calculate distance moved since last recorded point
		float distanceMoved = Vector3.Distance(currentTrailStart, lastRecordedPosition);
		distanceSinceLastPoint += distanceMoved;

		// Accumulate time since last point
		timeSinceLastPoint += Time.deltaTime;

		// Scale the segment length based on timeScale to maintain visual consistency
		float adjustedSegmentLength = segmentLength * fish.settings.timeScale;

		// Add point if we've moved enough distance OR enough time has passed
		bool shouldAddPoint = distanceSinceLastPoint >= adjustedSegmentLength ||
							timeSinceLastPoint >= maxTimeBetweenPoints;

		if (shouldAddPoint) {
			// Add new point
			trailPoints.Enqueue(currentTrailStart);

			// Calculate total trail length and remove old points if too long
			float totalLength = 0f;
			List<Vector3> points = new List<Vector3>(trailPoints);

			// Start from the newest point and work backwards
			for (int i = points.Count - 1; i > 0; i--) {
				totalLength += Vector3.Distance(points[i], points[i - 1]);
				if (totalLength > trailLength) {
					// Keep only the points from index i onwards (the newest points)
					int pointsToKeep = points.Count - i;
					while (trailPoints.Count > pointsToKeep) {
						trailPoints.Dequeue(); // Remove oldest points
					}
					break;
				}
			}

			distanceSinceLastPoint = 0;
			timeSinceLastPoint = 0;
			UpdateLineRenderer();
		}

		lastRecordedPosition = currentTrailStart;
	}

	void SetupLineRenderer() {
		lineRenderer.useWorldSpace = true;

		// Create gradient from fish colors
		Gradient gradient = new Gradient();
		gradient.SetKeys(
			new GradientColorKey[] {
				new GradientColorKey(fish.GetTrailStartColor(), 0f),
				new GradientColorKey(fish.GetTrailEndColor(), 1f)
			},
			new GradientAlphaKey[] {
				new GradientAlphaKey(1f, 0f),
				new GradientAlphaKey(1f, 1f)
			}
		);
		lineRenderer.colorGradient = gradient;

		// Optional: Set material properties
		lineRenderer.numCapVertices = 4; // Rounded ends
		lineRenderer.numCornerVertices = 4; // Rounded corners
		lineRenderer.alignment = LineAlignment.View; // Face camera
	}

	void UpdateLineRenderer() {
		Vector3[] positions = trailPoints.ToArray();
		lineRenderer.positionCount = positions.Length;
		lineRenderer.SetPositions(positions);
	}

	void OnEnable() {
		// Clear and reinitialize trail when enabled
		if (trailPoints != null) {
			trailPoints.Clear();
			trailPoints.Enqueue(GetTrailStartPosition());
			timeSinceLastPoint = 0;
			UpdateLineRenderer();
			SetupLineRenderer(); // Refresh gradient when enabled
		}
	}
}
