using UnityEngine;
using UnityX.NoiseSampler;

[RequireComponent(typeof(SpriteRenderer))]
public class Fish : MonoBehaviour {
	public FlockingSettings settings;

	[Header("Visual Settings")]
	[Tooltip("Base color of the fish")]
	public Color baseColor = Color.white;

	[HideInInspector]
	public Vector3 velocity;

	private SpriteRenderer spriteRenderer;
	private float currentRotation; // Current rotation in degrees
	private float wanderAngle; // Current wander angle offset
	private NoiseSampler wanderNoiseSampler;
	private float lastRotationTime;
	private float currentSpeed;

	// Color variations
	private Color actualColor;
	private Color trailStartColor;
	private Color trailEndColor;

	// Debug visualization
	private float debugDesiredRotation;
	private float debugActualRotation;
	private bool debugIsRotationClamped;
	private Color debugRotationColor;

	void Awake() {
		wanderNoiseSampler = new NoiseSampler();
		wanderNoiseSampler.properties = settings.wanderNoiseSamplerProperties;
		wanderNoiseSampler.position = Random.insideUnitSphere * 100f;

		// Initialize with random direction and random speed within range
		float randomAngle = Random.Range(0f, 360f);
		currentRotation = randomAngle;
		currentSpeed = Random.Range(settings.minSpeed, settings.maxSpeed);
		velocity = Quaternion.Euler(0, 0, randomAngle) * Vector3.right * currentSpeed;

		wanderAngle = 0f;
		lastRotationTime = Time.time;

		// Initialize colors with slight random variation
		InitializeColors();
	}

	void InitializeColors() {
		spriteRenderer = GetComponent<SpriteRenderer>();

		// Convert base color to HSV
		Color.RGBToHSV(baseColor, out float h, out float s, out float v);

		// Add slight random variations
		h = Mathf.Repeat(h + Random.Range(-0.03f, 0.03f), 1f); // Small hue shift
		s = Mathf.Clamp01(s + Random.Range(-0.05f, 0.05f));    // Small saturation variation
		v = Mathf.Clamp01(v + Random.Range(-0.05f, 0.05f));    // Small value variation

		// Set the actual color with variations
		actualColor = Color.HSVToRGB(h, s, v);
		spriteRenderer.color = actualColor;

		// Create trail start color (darker, less saturated)
		float trailStartH = Mathf.Repeat(h + 0.04f, 1f);       // Slight hue shift
		float trailStartS = Mathf.Clamp01(s * 0.9f);           // Reduce saturation
		float trailStartV = Mathf.Clamp01(v * 0.7f);           // Darker
		trailStartColor = Color.HSVToRGB(trailStartH, trailStartS, trailStartV);

		// Trail end color is the actual color
		trailEndColor = actualColor;
	}

	public Color GetTrailStartColor() {
		return trailStartColor;
	}

	public Color GetTrailEndColor() {
		return trailEndColor;
	}

	public Vector2 GetWanderForce() {
		wanderNoiseSampler.properties = settings.wanderNoiseSamplerProperties;
		// Apply time scale to wander speed

		// Sample Perlin noise for rotation relative to current direction
		float noiseValue = wanderNoiseSampler.Sample().value;

		// Update wander angle smoothly (affected by time scale)
		wanderAngle += noiseValue * Time.deltaTime * settings.timeScale * 360f;

		// Create direction relative to current fish rotation
		return Quaternion.Euler(0, 0, currentRotation + wanderAngle) * Vector2.right;
	}

	public void UpdateFish(Vector2 desiredDirection, float deltaTime) {
		deltaTime *= settings.timeScale;
		wanderNoiseSampler.position += new Vector3(0, 0, deltaTime * settings.wanderSpeed);

		// Get the target rotation from the desired direction
		float targetRotation = Mathf.Atan2(desiredDirection.y, desiredDirection.x) * Mathf.Rad2Deg;

		// Calculate the shortest rotation to the target
		float rotationDiff = Mathf.DeltaAngle(currentRotation, targetRotation);

		// Store the desired rotation for debug visualization
		debugDesiredRotation = targetRotation;

		// Apply force multiplier to rotation speed
		float effectiveTurnSpeed = settings.turnSpeed * settings.globalForceMultiplier;
		float effectiveMaxRotation = settings.maxRotationPerSecond * settings.globalForceMultiplier;

		// Limit maximum rotation per second (affected by time scale)
		float maxRotationThisFrame = effectiveMaxRotation * deltaTime;
		float turnAmount = Mathf.Clamp(rotationDiff, -maxRotationThisFrame, maxRotationThisFrame);

		// Apply rotation based on turn speed (still respect the turn speed limit)
		float finalTurnAmount = Mathf.Clamp(turnAmount, -effectiveTurnSpeed * deltaTime, effectiveTurnSpeed * deltaTime);

		// Store debug info about rotation clamping
		debugIsRotationClamped = Mathf.Abs(finalTurnAmount) < Mathf.Abs(rotationDiff);
		debugActualRotation = currentRotation + finalTurnAmount;

		// Set debug color based on how much clamping occurred
		float clampRatio = Mathf.Abs(finalTurnAmount / rotationDiff);
		debugRotationColor = Color.Lerp(Color.red, Color.green, clampRatio);

		currentRotation += finalTurnAmount;

		// Calculate desired speed based on alignment with desired direction
		float alignmentFactor = Vector2.Dot(desiredDirection, velocity.normalized);
		float targetSpeed = Mathf.Lerp(settings.minSpeed, settings.maxSpeed, (alignmentFactor + 1f) * 0.5f);

		// Smoothly adjust current speed towards target speed (affected by time scale)
		float speedChange = settings.acceleration * settings.globalForceMultiplier * deltaTime;
		currentSpeed = Mathf.MoveTowards(currentSpeed, targetSpeed, speedChange);

		// Update velocity direction while maintaining current speed
		velocity = Quaternion.Euler(0, 0, currentRotation) * Vector3.right * currentSpeed;

		// Update position (deltaTime already includes time scale)
		transform.position += velocity * deltaTime;

		// Update visual rotation
		transform.rotation = Quaternion.Euler(0, 0, currentRotation);

		lastRotationTime = Time.time;
	}

	void OnDrawGizmos() {
		if (!Application.isPlaying) return;

		float gizmoLength = 1f; // Length of the direction indicators
		float arrowSize = 0.2f; // Size of arrow heads

		// Draw current direction
		Gizmos.color = Color.blue;
		Vector3 currentDir = Quaternion.Euler(0, 0, currentRotation) * Vector3.right;
		Gizmos.DrawRay(transform.position, currentDir * gizmoLength);

		// Draw desired direction
		Gizmos.color = debugRotationColor;
		Vector3 desiredDir = Quaternion.Euler(0, 0, debugDesiredRotation) * Vector3.right;
		Gizmos.DrawRay(transform.position, desiredDir * gizmoLength);

		// Draw arrow heads
		DrawArrowHead(transform.position + currentDir * gizmoLength, -currentDir, Color.blue, arrowSize);
		DrawArrowHead(transform.position + desiredDir * gizmoLength, -desiredDir, debugRotationColor, arrowSize);

		// Draw arc between current and desired rotation if being clamped
		if (debugIsRotationClamped) {
			DrawRotationArc(currentRotation, debugDesiredRotation, gizmoLength * 0.8f);
		}
	}

	private void DrawArrowHead(Vector3 pos, Vector3 direction, Color color, float size) {
		Vector3 right = Quaternion.Euler(0, 0, 30) * direction * size;
		Vector3 left = Quaternion.Euler(0, 0, -30) * direction * size;

		Gizmos.color = color;
		Gizmos.DrawRay(pos, right);
		Gizmos.DrawRay(pos, left);
	}

	private void DrawRotationArc(float fromAngle, float toAngle, float radius) {
		float deltaAngle = Mathf.DeltaAngle(fromAngle, toAngle);
		int segments = Mathf.CeilToInt(Mathf.Abs(deltaAngle) / 10f); // One segment per 10 degrees
		segments = Mathf.Max(segments, 2);

		Vector3 lastPoint = transform.position + (Quaternion.Euler(0, 0, fromAngle) * Vector3.right * radius);

		for (int i = 1; i <= segments; i++) {
			float t = (float)i / segments;
			float angle = fromAngle + deltaAngle * t;
			Vector3 nextPoint = transform.position + (Quaternion.Euler(0, 0, angle) * Vector3.right * radius);

			Gizmos.color = new Color(1f, 0.5f, 0f, 0.5f); // Semi-transparent orange
			Gizmos.DrawLine(lastPoint, nextPoint);
			lastPoint = nextPoint;
		}
	}
}
