using UnityEngine;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// Controls the movement of an object (usually the parent of lights)
/// between defined points (waypoints) in a loop.
/// </summary>
public class LightController : MonoBehaviour
{
    [Header("Objects to Control")]
    [Tooltip("GameObject (parent) that contains the lights and will be moved.")]
    public GameObject lightParent;

    [Tooltip("List of Transform objects whose positions and rotations will be used as waypoints.")]
    public List<Transform> waypoints;

    [Header("Movement Settings")]
    [Tooltip("Speed at which the object will move between waypoints.")]
    public float movementSpeed = 5.0f;

    [Tooltip("Speed at which the object will rotate toward the waypoint.")]
    public float rotationSpeed = 100.0f;

    [Tooltip("How close the object must be to consider the waypoint reached.")]
    public float waypointReachedThreshold = 0.1f;

    private int _currentWaypointIndex = 0;
    private bool _isMoving = false;
    private bool _isInitialized = false;

    /// <summary>
    /// Method for initializing the light controller.
    /// Should be called by the SceneManager.
    /// </summary>
    public void InitializeController(GameObject parent, List<Transform> newWaypoints)
    {
        lightParent = parent;
        waypoints = newWaypoints;

        if (lightParent == null)
        {
            Debug.LogError("LightController: The ‘lightParent’ object is not assigned during initialization! Disabling script.", this);
            enabled = false;
            _isInitialized = false;
            return;
        }

        if (waypoints == null || waypoints.Count == 0)
        {
            Debug.LogError("LightController: The ‘waypoints’ list is empty during initialization! Disabling script.", this);
            enabled = false;
            _isInitialized = false;
            return;
        }

        for (int i = 0; i < waypoints.Count; i++)
        {
            if (waypoints[i] == null)
            {
                Debug.LogError($"LightController: Waypoint at index {i} is not assigned during initialization! Disabling script.", this);
                enabled = false;
                _isInitialized = false;
                return;
            }
        }

        lightParent.transform.position = waypoints[0].position;
        lightParent.transform.rotation = waypoints[0].rotation;

        _currentWaypointIndex = 0;
        _isMoving = true;
        _isInitialized = true;
        Debug.Log("LightController: Inicjalizacja zakoñczona. Rozpoczynam ruch œwiat³a.");
    }

    /// <summary>
    /// Called at the start of the game.
    /// The script is waiting for initialization by the SceneManager.
    /// </summary>
    void Start()
    {
        if (!_isInitialized)
        {
            Debug.Log("LightController: Waiting for initialization by the SceneManager.");

        }
    }

    /// <summary>
    /// Called every frame.
    /// Responsible for smoothly moving and rotating the object.
    /// </summary>
    void Update()
    {
        if (!_isMoving || !_isInitialized || lightParent == null || waypoints == null || waypoints.Count == 0)
        {
            return;
        }

        Transform targetWaypoint = waypoints[_currentWaypointIndex];
        if (targetWaypoint == null) // Dodatkowe zabezpieczenie
        {
            Debug.LogWarning($"LightController: Target (waypoint {_currentWaypointIndex}) is null. Skipping frame.");
            return;
        }


        float distanceToTarget = Vector3.Distance(lightParent.transform.position, targetWaypoint.position);

        if (distanceToTarget > waypointReachedThreshold)
        {
            lightParent.transform.position = Vector3.MoveTowards(
                lightParent.transform.position,
                targetWaypoint.position,
                movementSpeed * Time.deltaTime
            );

            lightParent.transform.rotation = Quaternion.RotateTowards(
                lightParent.transform.rotation,
                targetWaypoint.rotation,
                rotationSpeed * Time.deltaTime
            );
        }
        else
        {
            //Debug.Log($"LightController: Reached waypoint {_currentWaypointIndex} ({targetWaypoint.name}).");
            _currentWaypointIndex++;
            if (_currentWaypointIndex >= waypoints.Count)
            {
                _currentWaypointIndex = 0;
                //Debug.Log("LightController: Powrót do pierwszego waypointu.");
            }
        }
    }

    /// <summary>
    /// Allows dynamic modification of the waypoint list from another script.
    /// Should be used after initialization.
    /// </summary>
    public void UpdateWaypoints(List<Transform> newWaypoints)
    {
        if (!_isInitialized)
        {
            Debug.LogWarning("LightController: Attempt to update waypoints before initialization.");
            return;
        }

        waypoints = newWaypoints;
        _currentWaypointIndex = 0;
        _isMoving = (waypoints != null && waypoints.Count > 0 && lightParent != null);
        if (_isMoving && waypoints.Count > 0 && waypoints[0] != null) 
        {
            lightParent.transform.position = waypoints[0].position;
            lightParent.transform.rotation = waypoints[0].rotation;
        }
        Debug.Log($"LightController: Waypoint list updated. New count: {waypoints?.Count ?? 0}.");
    }

    /// <summary>
    /// Allows dynamic changing of the controlled object.
    /// Should be used after initialization.
    /// </summary>
    public void UpdateLightParent(GameObject newLightParent)
    {
         if (!_isInitialized)
        {
            Debug.LogWarning("LightController: Attempt to update lightParent before initialization.");
            // InitializeController(newLightParent, waypoints);
            return;
        }

        lightParent = newLightParent;
        _isMoving = (lightParent != null && waypoints != null && waypoints.Count > 0);
         if (_isMoving && waypoints.Count > 0 && waypoints[0] != null)
        {
            lightParent.transform.position = waypoints[0].position;
            lightParent.transform.rotation = waypoints[0].rotation;
        }
        Debug.Log($"LightController: Zaktualizowano obiekt do kontroli: {lightParent?.name ?? "None"}.");
    }

    /// <summary>
    /// Stops the light’s movement.
    /// </summary>
    public void StopMovement()
    {
        _isMoving = false;
        Debug.Log("LightController: Light movement stopped.");
    }

    /// <summary>
    /// Resumes light movement if initialized.
    /// </summary>
    public void ResumeMovement()
    {
        if (_isInitialized && lightParent != null && waypoints != null && waypoints.Count > 0)
        {
            _isMoving = true;
            Debug.Log("LightController: Ruch œwiat³a wznowiony.");
        }
        else
        {
            Debug.LogWarning("LightController: Cannot resume movement – controller is not properly initialized.");
        }
    }


    void OnDrawGizmos()
    {
        if (waypoints == null || waypoints.Count < 1) 
        {
            return;
        }

        for (int i = 0; i < waypoints.Count; i++)
        {
            Transform currentWaypoint = waypoints[i];
            if (currentWaypoint == null) continue;

            Gizmos.color = (_currentWaypointIndex == i && Application.isPlaying) ? Color.green : Color.yellow;
            Gizmos.DrawSphere(currentWaypoint.position, 0.3f);

            if (waypoints.Count > 1) // Rysuj linie tylko jeœli jest wiêcej ni¿ 1 waypoint
            {
                 Transform nextWaypoint = waypoints[(i + 1) % waypoints.Count]; 
                 if (nextWaypoint != null)
                 {
                    Gizmos.DrawLine(currentWaypoint.position, nextWaypoint.position);
                 }
            }
        }
    }
}
