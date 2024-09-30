using InControl;
using UnityEngine;

[System.Serializable]
public struct DragParams {
    public float dragCoefficient;
    public float quadraticDragCoefficient;
}

[ExecuteAlways]
public class Player : MonoSingleton<Player> {
    public Rigidbody2D rigidbody2D => GetComponent<Rigidbody2D>();
    public float speed => rigidbody2D.velocity.magnitude;
    public VectorFieldComponent vectorField;
    
    // public Camera camera;
    public ThumbstickUI thumbstickUI;
    
    [Space]
    public ShipSettings settings;
    
    [Space]
    Vector2 inputVector;
    float inputDegrees;
    
    float angleChangeVelocity;
    void Update() {
        var deltaTime = Time.deltaTime;
        
        if (Application.isPlaying) {
            if (thumbstickUI.held) {
                inputVector = thumbstickUI.movementVector;
            } else if(InputManager.Enabled) {
                inputVector = InputManager.Devices.Count > 0 ? InputManager.ActiveDevice.LeftStick.Value : KeyboardInput.GetCombinedDirectionFromArrowKeys();
            } else {
                inputVector = Vector2.zero;
            }
        }
        
        
        if (Application.isPlaying) {
            // rigidbody2D.rotation = Mathf.MoveTowardsAngle(rigidbody2D.rotation, Vector2.SignedAngle(Vector2.up, inputVector), settings.angleChangeSpeed * deltaTime);
            
            // var dragParams = settings.fullBrakeDragParams;
            
            
            var baseThrust = settings.thrustAcceleration * settings.thrustAccelerationOverSpeed.Evaluate(speed);

            var velocity = rigidbody2D.velocity;
            var velocityAngle = Vector2.SignedAngle(Vector2.up, velocity);



            var normalizedInputVector = inputVector.normalized;
            var inputAngle = Vector2.SignedAngle(Vector2.up, normalizedInputVector);
            Vector2 normalizedVelocity = velocity.normalized;

            var newInputAngle = Mathf.MoveTowardsAngle(velocityAngle, inputAngle, settings.angleChangeSpeed);
            Debug.Log($"Velocity angle {velocityAngle} input angle {inputAngle} new input angle {newInputAngle}");
            var rotatedThumbstickVector = MathX.DegreesToVector2(newInputAngle) * inputVector.magnitude;

            // Add rotated thumbstick vector to velocity
            velocity += rotatedThumbstickVector * (baseThrust * Time.deltaTime);
            
            
            // velocity += baseThrust * inputVector * Time.deltaTime;
            velocity += (Vector2)vectorField.EvaluateWorldVector(transform.position) * Time.deltaTime;

            var movementDirection = rigidbody2D.velocity.normalized;            
            var dragForce = (-movementDirection * (rigidbody2D.velocity.magnitude * settings.dragParams.dragCoefficient)) + -movementDirection * (rigidbody2D.velocity.magnitude * rigidbody2D.velocity.magnitude * settings.dragParams.quadraticDragCoefficient * Time.deltaTime);
            velocity += dragForce * Time.deltaTime;
            
            rigidbody2D.velocity = velocity;
            
            rigidbody2D.position += rigidbody2D.velocity * Time.deltaTime;

            rigidbody2D.rotation = Mathf.MoveTowardsAngle(rigidbody2D.rotation, Vector2.SignedAngle(Vector2.up, velocity), settings.angleChangeSpeed * deltaTime);
        }

        // transform.position += rigidbody2D.velocity;
        if (inputVector.magnitude > 0) {
            // transform.rotation = Quaternion.AngleAxis(inputDegrees-90, Vector3.back);
            // inputDegrees = Mathf.SmoothDampAngle(inputDegrees, rawInputDegrees, ref angleChangeVelocity, settings.angleChangeSmoothTime);
            // transform.rotation = Quaternion.AngleAxis(inputDegrees-90, Vector3.back);
        }
        // spline.GetRotationAtArcLength(splineArcLength).Rotate(Vector3.up * (MathX.Sign(velocity) == 1 ? 0 : 180));


        // camera.transform.position = transform.position + Vector3.back;
    }
}
