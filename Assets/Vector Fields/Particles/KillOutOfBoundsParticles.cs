using UnityEngine;

[ExecuteAlways]
public class KillOutOfBoundsParticles : MonoBehaviour
{
	[Header("Boundary Settings")]
	[SerializeField] private bool useShapeModuleBounds = true;
	[SerializeField] private Vector3 boundaryMin = new Vector3(-10f, -10f, -10f);
	[SerializeField] private Vector3 boundaryMax = new Vector3(10f, 10f, 10f);
	[SerializeField] private float extraBoundaryDistance = 2f; // Extra distance for trails

	[Header("Debug")]
	[SerializeField] private bool showBoundary = true;

	private ParticleSystem ps;
	private ParticleSystem.Particle[] particles;
	private Vector3 shapeBoundsMin;
	private Vector3 shapeBoundsMax;

	void Update()
	{
		ps = GetComponent<ParticleSystem>();
		if (particles == null || particles.Length != ps.main.maxParticles)
			particles = new ParticleSystem.Particle[ps.main.maxParticles];

		UpdateBounds();

		int particleCount = ps.GetParticles(particles);

		for (int i = 0; i < particleCount; i++)
		{
			Vector3 position = particles[i].position;
			Vector3 currentMin = useShapeModuleBounds ? shapeBoundsMin : boundaryMin;
			Vector3 currentMax = useShapeModuleBounds ? shapeBoundsMax : boundaryMax;

			if (position.x < currentMin.x - extraBoundaryDistance ||
				position.x > currentMax.x + extraBoundaryDistance ||
				position.y < currentMin.y - extraBoundaryDistance ||
				position.y > currentMax.y + extraBoundaryDistance ||
				position.z < currentMin.z - extraBoundaryDistance ||
				position.z > currentMax.z + extraBoundaryDistance)
			{
				particles[i].remainingLifetime = 0;
			}
		}

		ps.SetParticles(particles, particleCount);
	}

	private void UpdateBounds()
	{
		if (!useShapeModuleBounds) return;

		var shape = ps.shape;
		var position = transform.position;
		var scale = transform.lossyScale;

		switch (shape.shapeType)
		{
			case ParticleSystemShapeType.Box:
				Vector3 boxScale = shape.scale * 0.5f;
				shapeBoundsMin = position - new Vector3(
					boxScale.x * scale.x,
					boxScale.y * scale.y,
					boxScale.z * scale.z
				);
				shapeBoundsMax = position + new Vector3(
					boxScale.x * scale.x,
					boxScale.y * scale.y,
					boxScale.z * scale.z
				);
				break;

			case ParticleSystemShapeType.Sphere:
				float radius = shape.radius * Mathf.Max(scale.x, Mathf.Max(scale.y, scale.z));
				shapeBoundsMin = position - Vector3.one * radius;
				shapeBoundsMax = position + Vector3.one * radius;
				break;

			// Add more shape types as needed
			default:
				// Fallback to custom bounds if shape type isn't supported
				useShapeModuleBounds = false;
				Debug.LogWarning($"Shape type {shape.shapeType} not supported for bounds calculation. Using custom bounds instead.");
				break;
		}
	}

	void OnDrawGizmos()
	{
		if (!showBoundary) return;

		Vector3 currentMin = useShapeModuleBounds ? shapeBoundsMin : boundaryMin;
		Vector3 currentMax = useShapeModuleBounds ? shapeBoundsMax : boundaryMax;
		Vector3 size = currentMax - currentMin;
		Vector3 center = currentMin + size * 0.5f;

		// Draw the kill boundary
		Gizmos.color = new Color(1f, 0f, 0f, 0.2f);
		Gizmos.DrawWireCube(center, size);

		// Draw the extended boundary for trails
		Gizmos.color = new Color(1f, 0.5f, 0f, 0.1f);
		Vector3 extendedSize = size + Vector3.one * (extraBoundaryDistance * 2f);
		Gizmos.DrawWireCube(center, extendedSize);
	}
}
