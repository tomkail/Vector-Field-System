using System.Collections.Generic;
using UnityEngine;

public class Flocking : MonoBehaviour {
	[System.Serializable]
	public class FishSpawnInfo {
		public GameObject prefab;
		public int count = 20;
	}

	public List<FishSpawnInfo> fishTypes = new List<FishSpawnInfo>();
	public Bounds bounds = new Bounds(Vector3.zero, new Vector3(10, 10, 0));
	public VectorFieldComponent vectorField;

	[Header("Pre-warming")]
	[SerializeField] private bool preWarmSimulation = true;
	[SerializeField] private int preWarmSteps = 100;
	[SerializeField] private float preWarmDeltaTime = 0.02f;
	[SerializeField] private float spawnRadius = 2f; // Initial spawn radius for pre-warming

	public List<Fish> allFish = new List<Fish>();
	private Vector3 flockCenter;

	void Start() {
		RecreateFlockRuntime();
	}

	public void ClearFlock() {
		allFish.Clear();
		transform.DestroyAllChildrenAutomatic();
	}

	public void RecreateFlockRuntime(bool forcePreWarm = false) {
		ClearFlock();
		InitializeFlock();

		if (preWarmSimulation || forcePreWarm) {
			PreWarmFlock();
		}
	}

	void InitializeFlock() {
		Vector3 center = transform.position;

		foreach (var fishInfo in fishTypes) {
			for (int i = 0; i < fishInfo.count; i++) {
				Vector3 randomPosition;
				if (preWarmSimulation) {
					// Spawn in a tight circle for pre-warming
					float angle = Random.Range(0f, Mathf.PI * 2f);
					float radius = Random.Range(0f, spawnRadius);
					randomPosition = center + new Vector3(
						Mathf.Cos(angle) * radius,
						Mathf.Sin(angle) * radius,
						0
					);
				} else {
					// Regular random spawn in bounds
					randomPosition = new Vector3(
						Random.Range(bounds.min.x, bounds.max.x),
						Random.Range(bounds.min.y, bounds.max.y),
						0
					);
				}

				GameObject fishObject = Instantiate(fishInfo.prefab, randomPosition, Quaternion.identity);
				fishObject.transform.parent = transform;

				Fish fish = fishObject.GetComponent<Fish>();
				allFish.Add(fish);
			}
		}
	}

	void PreWarmFlock() {
		// Store original time scale
		float originalTimeScale = Time.timeScale;
		Time.timeScale = 1f;

		// Run simulation steps
		for (int i = 0; i < preWarmSteps; i++) {
			UpdateFlock(preWarmDeltaTime);
		}

		// Restore time scale
		Time.timeScale = originalTimeScale;
	}

	void Update() {
		UpdateFlock(Time.deltaTime);
	}

	void UpdateFlock(float deltaTime) {
		// Calculate flock center once per frame
		flockCenter = GetFlockCenter();

		foreach (Fish fish in allFish) {
			List<Fish> neighbors = GetNeighbors(fish);

			// Calculate base forces without global multiplier
			Vector2 cohesion = CalculateCohesion(fish, neighbors) * fish.settings.cohesionWeight;
			Vector2 alignment = CalculateAlignment(fish, neighbors) * fish.settings.alignmentWeight;
			Vector2 separation = CalculateSeparation(fish, neighbors) * fish.settings.separationWeight;
			Vector2 globalCohesion = CalculateGlobalCohesion(fish) * fish.settings.globalCohesionWeight;
			Vector2 wander = fish.GetWanderForce() * fish.settings.wanderWeight;

			// Combine all desired directions
			Vector2 desiredDirection = cohesion + alignment + separation + globalCohesion + wander;

			if (vectorField) {
				desiredDirection += (Vector2)vectorField.EvaluateWorldVector(fish.transform.position);
			}

			// Apply global force multiplier to the combined direction BEFORE normalization
			desiredDirection *= fish.settings.globalForceMultiplier;

			// Normalize the desired direction
			if (desiredDirection != Vector2.zero) {
				desiredDirection.Normalize();
			} else {
				// If no direction is desired, continue in current direction
				desiredDirection = (Vector2)fish.velocity.normalized;
			}

			fish.UpdateFish(desiredDirection, deltaTime);
		}
	}

	Vector2 CalculateGlobalCohesion(Fish fish) {
		Vector2 directionToCenter = (Vector2)flockCenter - (Vector2)fish.transform.position;
		float distanceToCenter = directionToCenter.magnitude;

		// Apply custom falloff power for global cohesion
		float normalizedDistance = distanceToCenter / 5f;
		float strengthMultiplier = Mathf.Pow(normalizedDistance, fish.settings.globalCohesionFalloff);

		return directionToCenter.normalized * strengthMultiplier;
	}

	public Vector3 GetFlockCenter() {
		if (allFish.Count == 0) return transform.position;

		Vector3 center = Vector3.zero;
		foreach (Fish fish in allFish) {
			center += fish.transform.position;
		}
		return center / allFish.Count;
	}

	List<Fish> GetNeighbors(Fish fish) {
		List<Fish> neighbors = new List<Fish>();
		foreach (Fish other in allFish) {
			if (other != fish && Vector3.Distance(fish.transform.position, other.transform.position) <= fish.settings.neighborRadius) {
				neighbors.Add(other);
			}
		}
		return neighbors;
	}

	Vector2 CalculateCohesion(Fish fish, List<Fish> neighbors) {
		if (neighbors.Count == 0) return Vector2.zero;

		Vector2 weightedCenter = Vector2.zero;
		float totalWeight = 0f;

		foreach (Fish neighbor in neighbors) {
			float distance = Vector2.Distance(fish.transform.position, neighbor.transform.position);
			float normalizedDistance = 1f - (distance / neighbor.settings.neighborRadius);
			float weight = Mathf.Pow(normalizedDistance, fish.settings.cohesionFalloff);

			weightedCenter += (Vector2)neighbor.transform.position * weight;
			totalWeight += weight;
		}

		if (totalWeight > 0) {
			weightedCenter /= totalWeight;
			return ((Vector2)weightedCenter - (Vector2)fish.transform.position).normalized;
		}

		return Vector2.zero;
	}

	Vector2 CalculateAlignment(Fish fish, List<Fish> neighbors) {
		if (neighbors.Count == 0) return Vector2.zero;

		Vector2 averageVelocity = Vector2.zero;
		float totalWeight = 0f;

		foreach (Fish neighbor in neighbors) {
			float distance = Vector2.Distance(fish.transform.position, neighbor.transform.position);
			float normalizedDistance = 1f - (distance / neighbor.settings.neighborRadius);
			float weight = Mathf.Pow(normalizedDistance, fish.settings.alignmentFalloff);

			averageVelocity += (Vector2)neighbor.velocity * weight;
			totalWeight += weight;
		}

		if (totalWeight > 0) {
			averageVelocity /= totalWeight;
			return averageVelocity.normalized;
		}

		return Vector2.zero;
	}

	Vector2 CalculateSeparation(Fish fish, List<Fish> neighbors) {
		if (neighbors.Count == 0) return Vector2.zero;

		Vector2 separation = Vector2.zero;
		float totalWeight = 0f;

		foreach (Fish neighbor in neighbors) {
			float distance = Vector2.Distance(fish.transform.position, neighbor.transform.position);
			if (distance <= neighbor.settings.separationRadius) {
				float normalizedDistance = 1f - (distance / neighbor.settings.separationRadius);
				float weight = Mathf.Pow(normalizedDistance, fish.settings.separationFalloff);

				Vector2 awayFromNeighbor = (Vector2)(fish.transform.position - neighbor.transform.position);
				separation += awayFromNeighbor.normalized * weight;
				totalWeight += weight;
			}
		}

		if (totalWeight > 0) {
			separation /= totalWeight;
			return separation.normalized;
		}

		return Vector2.zero;
	}
}
