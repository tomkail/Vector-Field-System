using UnityEngine;
using UnityX.NoiseSampler;

[CreateAssetMenu(fileName = "FlockingSettings", menuName = "Flocking/Flocking Settings")]
public class FlockingSettings : ScriptableObject {
	[Tooltip("How strongly fish are attracted to the center of their local group")]
	public float cohesionWeight = 1f;

	[Tooltip("How strongly fish match their neighbors' direction")]
	public float alignmentWeight = 1f;

	[Tooltip("How strongly fish avoid getting too close to each other")]
	public float separationWeight = 1f;

	[Tooltip("How strongly fish are attracted to the center of the entire flock")]
	public float globalCohesionWeight = 0.5f;

	[Header("Global Modifiers")]
	[Tooltip("Multiplier for all forces (cohesion, alignment, separation). Higher values make behavior more aggressive")]
	[Range(0.1f, 5f)]
	public float globalForceMultiplier = 1f;

	[Tooltip("Multiplier for simulation speed. Affects movement speed, turning, and behavior response")]
	[Range(0.1f, 5f)]
	public float timeScale = 1f;

	[Header("Movement")]
	[Tooltip("Minimum speed of the fish in units per second")]
	public float minSpeed = 1.5f;

	[Tooltip("Maximum speed of the fish in units per second")]
	public float maxSpeed = 3f;

	[Tooltip("How quickly the fish can accelerate/decelerate in units per second")]
	public float acceleration = 4f;

	[Tooltip("How quickly the fish can turn in degrees per second")]
	public float turnSpeed = 360f;

	[Tooltip("Maximum angle the fish can rotate in degrees per second")]
	public float maxRotationPerSecond = 180f;

	[Header("Wandering")]
	[Tooltip("How strongly the random wandering movement affects the fish")]
	public float wanderWeight = 0.8f;

	[Tooltip("How quickly the wandering direction changes (higher = more frequent changes)")]
	public float wanderSpeed = 1f;  // How fast the wander direction changes
	public NoiseSamplerProperties wanderNoiseSamplerProperties;

	[Header("Speed Limits")]
	[Tooltip("Maximum speed the fish can move")]
	public float maxSpeedLimit = 5f;

	[Tooltip("Minimum speed the fish must maintain")]
	public float minSpeedLimit = 2f;

	[Header("Neighbor Detection")]
	[Tooltip("How far away a fish can see other fish to flock with")]
	public float neighborRadius = 2.5f;

	[Tooltip("How close is too close - fish within this radius will be avoided")]
	public float separationRadius = 1f;

	// Fish type specific settings
	[Header("Fish Type")]
	[Tooltip("The type of fish this settings applies to (affects scoring and behavior)")]
	public FishType fishType;

	[Tooltip("Points awarded when collecting this type of fish")]
	public int pointValue;

	[Tooltip("Color tint applied to the fish sprite")]
	public Color fishColor = Color.white;

	[Header("Falloff Powers")]
	[Tooltip("Power for cohesion falloff (1 = linear, 2 = quadratic, 3 = cubic)")]
	[Range(1, 4)]
	public float cohesionFalloff = 2f;

	[Tooltip("Power for alignment falloff (1 = linear, 2 = quadratic, 3 = cubic)")]
	[Range(1, 4)]
	public float alignmentFalloff = 2f;

	[Tooltip("Power for separation falloff (1 = linear, 2 = quadratic, 3 = cubic)")]
	[Range(1, 4)]
	public float separationFalloff = 3f;

	[Tooltip("Power for global cohesion falloff (1 = linear, 2 = quadratic, 3 = cubic)")]
	[Range(1, 4)]
	public float globalCohesionFalloff = 2f;
}

public enum FishType {
	Blue,   // Regular points
	Red,    // Dangerous
	Gold    // High value
}
