using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class FlockingPlayer : MonoBehaviour {
	public float speed = 5f;
	private Vector2 movementInput;
	private Vector2 lastNonZeroInputVector;

	public InputActionAsset actionAsset;

	private InputAction moveAction;
	private InputAction actionAction;

	private void Awake() {
		// Get actions
		moveAction = actionAsset.FindAction("Move");
		actionAction = actionAsset.FindAction("Dodge");

		// Set up callbacks
		moveAction.performed += ctx => movementInput = ctx.ReadValue<Vector2>();
	}

	private void OnEnable() {
		// Enable the input actions
		moveAction.Enable();
		actionAction.Enable();
	}

	private void OnDisable() {
		// Disable the input actions
		moveAction.Disable();
		actionAction.Disable();
	}

	private void Update() {
		var moveInputVector = moveAction.ReadValue<Vector2>();
		if (moveInputVector != Vector2.zero)
			lastNonZeroInputVector = moveInputVector;
		var movementDirection = ScreenToCameraRelativeMovementDirection(moveInputVector);
		var movement = movementDirection * (speed * Time.deltaTime);
		transform.Translate(movement, Space.World);
	}

	Vector3 ScreenToCameraRelativeMovementDirection(Vector2 screenDirection) {
		Camera camera = Camera.main;
		Vector3 planeNormal = Vector3.up;
		Vector3 cameraForwardOnPlane = Vector3.ProjectOnPlane(camera.transform.forward, planeNormal).normalized;
		float angle = Vector3.SignedAngle(Vector3.forward, cameraForwardOnPlane, planeNormal);
		Vector3 movement = new Vector3(screenDirection.x, 0f, screenDirection.y);
		return Quaternion.Euler(0f, angle, 0f) * movement;
	}
}