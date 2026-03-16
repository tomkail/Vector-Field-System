using UnityEngine;

[ExecuteAlways]
public class CameraController : MonoBehaviour {
	Camera camera => GetComponent<Camera>();

	public Flocking flocking;
	public CameraProperties cameraProperties;

	[SerializeField] float positionSmoothTime = 0.2f;
	private Vector3 positionVelocity;

	void Update() {
		if (flocking != null) {
			Vector3 targetPosition = flocking.GetFlockCenter();
			cameraProperties.targetPoint = Vector3.SmoothDamp(
				cameraProperties.targetPoint,
				targetPosition,
				ref positionVelocity,
				positionSmoothTime
			);
			cameraProperties.ApplyTo(camera);
		}
	}
}
