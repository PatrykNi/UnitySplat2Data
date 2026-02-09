using UnityEngine;
using System.Collections; 

public enum MovementType
{
    Linear,   
    Circular  
}

[System.Serializable]
public class CameraWaypoint
{
    [Tooltip("The target position, rotation, and FOV for this waypoint.")]
    public Transform targetTransform;

    [Tooltip("The type of movement used to reach this waypoint from the previous one.")]
    public MovementType movementToThisPoint = MovementType.Linear;

    [Tooltip("Required if Movement Type is Circular. This point defines the curve.")]
    public Transform circularIntermediatePoint;

    [Tooltip("Number of screenshots to capture during the movement segment leading to this point.")]
    public int numberOfScreenshots = 10; // Define screenshots per segment
}
