using System.Collections;
using System.Collections.Generic;
using UnityEngine;


[System.Serializable]
public class ObjectPlacementSettings
{
    [Tooltip("Object (GameObject) to be placed.")]
    public GameObject objectToPlace; // Reference to the object

    [Header("Randomize Rotation Axes")]
    [Tooltip("Randomize rotation around the X axis?")]
    public bool randomizeX = true; 
    [Tooltip("Randomize rotation around the Y axis?")]
    public bool randomizeY = true; 
    [Tooltip("Randomize rotation around the Z axis?")]
    public bool randomizeZ = true; 
}



public class RandomObjectPlacer : MonoBehaviour
{
    [Header("Object Placement Settings")]
    [Tooltip("List of objects to place along with their individual rotation settings.")]
    [SerializeField] private List<ObjectPlacementSettings> objectSettingsList;

    private GameObject _placementAreaObject;
    private BoxCollider _placementBoxCollider;
    private float _settleTime;
    private bool _makeKinematicAfterSettling;
    private bool _freezeRotationX;
    private bool _freezeRotationY;
    private bool _freezeRotationZ;

    /// <summary>
    /// Sets the list of object placement settings. Used by the SceneManager.
    /// </summary>
    public void SetObjectPlacementSettings(List<ObjectPlacementSettings> newSettings)
    {
        objectSettingsList = newSettings;
        Debug.Log($"RandomObjectPlacer: Updated objectSettingsList with {newSettings?.Count ?? 0} entries.");
    }

    /// <summary>
    /// Sets the placement area object (expects a BoxCollider) and physics settings. Used by the SceneManager.
    /// </summary>
    public void SetPlacementSettings(GameObject areaObject, float settleTime, bool makeKinematic, bool freezeX, bool freezeY, bool freezeZ)
    {
        _placementAreaObject = areaObject;
        _settleTime = settleTime;
        _makeKinematicAfterSettling = makeKinematic;
        _freezeRotationX = freezeX;
        _freezeRotationY = freezeY;
        _freezeRotationZ = freezeZ;

        if (_placementAreaObject != null)
        {
            _placementBoxCollider = _placementAreaObject.GetComponent<BoxCollider>();
            if (_placementBoxCollider == null)
            {
                Debug.LogError($"The placement area object '{_placementAreaObject.name}' must have a BoxCollider component to be used as a precise area! Without a BoxCollider, object placement may not work correctly.", _placementAreaObject);
            }
            else
            {
                Debug.Log($"RandomObjectPlacer: Using BoxCollider '{_placementBoxCollider.name}' as the area. Local center: {_placementBoxCollider.center}, Local size: {_placementBoxCollider.size}, Area object Transform: '{_placementAreaObject.name}'");
            }
        }
        else
        {
            Debug.LogError("RandomObjectPlacer: Placement Area Object is null. Cannot set the placement area.");
            _placementBoxCollider = null;
        }
    }


    [ContextMenu("Place Objects Randomly (Coroutine)")]
    public IEnumerator PlaceObjectsRandomlyCoroutine()
    {
        
        yield return StartCoroutine(PlaceAndSettleObjects());
    }

    
    private IEnumerator PlaceAndSettleObjects()
    {
        if (objectSettingsList == null || objectSettingsList.Count == 0)
        {
            Debug.LogWarning("No object placement settings found in the 'Object Placement Settings' list!");
            yield break;
        }

        if (_placementBoxCollider == null)
        {
            Debug.LogError("Placement area BoxCollider is not assigned or not found on the area object! Cannot place objects.");
            if (_placementAreaObject != null)
            {
                Debug.LogError($"Check the object '{_placementAreaObject.name}' to ensure it has a BoxCollider component.", _placementAreaObject);
            }
            yield break;
        }

        Debug.Log("Starting object placement (using BoxCollider as the area)...");

        foreach (ObjectPlacementSettings settings in objectSettingsList)
        {
            if (settings == null || settings.objectToPlace == null)
            {
                Debug.LogWarning("An element in the object settings list is empty or missing a reference to the object. Skipping this element.");
                continue;
            }

            GameObject obj = settings.objectToPlace;

            Collider objCollider = obj.GetComponent<Collider>();
            if (objCollider == null)
            {
                Debug.LogWarning($"Object '{obj.name}' does not have a Collider. Adding a BoxCollider.", obj);
                objCollider = obj.AddComponent<BoxCollider>();
            }

            Rigidbody objRigidbody = obj.GetComponent<Rigidbody>();
            if (objRigidbody == null)
            {
                Debug.LogWarning($"Object '{obj.name}' does not have a Rigidbody. Adding one.", obj);
                objRigidbody = obj.AddComponent<Rigidbody>();
            }

            objRigidbody.isKinematic = false;
            objRigidbody.linearVelocity = Vector3.zero;
            objRigidbody.angularVelocity = Vector3.zero;

            objRigidbody.constraints = RigidbodyConstraints.None;
            if (_freezeRotationX) objRigidbody.constraints |= RigidbodyConstraints.FreezeRotationX;
            if (_freezeRotationY) objRigidbody.constraints |= RigidbodyConstraints.FreezeRotationY;
            if (_freezeRotationZ) objRigidbody.constraints |= RigidbodyConstraints.FreezeRotationZ;

            Vector3 randomLocalPosInBox;
            Vector3 finalWorldPosForPivot;

            Vector3 areaBoxCenterLocal = _placementBoxCollider.center;
            Vector3 areaBoxSizeLocal = _placementBoxCollider.size;

            float randomX = Random.Range(areaBoxCenterLocal.x - areaBoxSizeLocal.x / 2f, areaBoxCenterLocal.x + areaBoxSizeLocal.x / 2f);
            float randomY = Random.Range(areaBoxCenterLocal.y - areaBoxSizeLocal.y / 2f, areaBoxCenterLocal.y + areaBoxSizeLocal.y / 2f);
            float randomZ = Random.Range(areaBoxCenterLocal.z - areaBoxSizeLocal.z / 2f, areaBoxCenterLocal.z + areaBoxSizeLocal.z / 2f);
            randomLocalPosInBox = new Vector3(randomX, randomY, randomZ);

            finalWorldPosForPivot = _placementBoxCollider.transform.TransformPoint(randomLocalPosInBox);
            obj.transform.position = finalWorldPosForPivot;

            Quaternion initialRotation = obj.transform.rotation;
            Vector3 initialEuler = initialRotation.eulerAngles;
            Vector3 randomEuler = Random.rotation.eulerAngles;
            Vector3 finalEuler = new Vector3(
                settings.randomizeX ? randomEuler.x : initialEuler.x,
                settings.randomizeY ? randomEuler.y : initialEuler.y,
                settings.randomizeZ ? randomEuler.z : initialEuler.z
            );
            obj.transform.rotation = Quaternion.Euler(finalEuler);
        }

        Debug.Log($"Attempting to place {objectSettingsList.Count} objects. Waiting {_settleTime} seconds for physics to settle...");
        yield return new WaitForSeconds(_settleTime);
        Debug.Log("Physics placement completed.");

        if (_makeKinematicAfterSettling)
        {
            Debug.Log("Setting objects to kinematic...");
            foreach (ObjectPlacementSettings settings in objectSettingsList)
            {
                 if (settings == null || settings.objectToPlace == null) continue;
                 Rigidbody rb = settings.objectToPlace.GetComponent<Rigidbody>();
                 if (rb != null)
                 {
                     rb.isKinematic = true;
                 }
            }
            Debug.Log("Objects are now kinematic.");
        }
        Debug.Log("Random object placement process completed.");
    }

    void OnDrawGizmos()
    {
        if (_placementBoxCollider != null && _placementAreaObject != null && _placementAreaObject.activeInHierarchy)
        {
            Gizmos.color = new Color(0,1,1, 0.3f);
            Matrix4x4 originalMatrix = Gizmos.matrix;
            Gizmos.matrix = Matrix4x4.TRS(
                _placementBoxCollider.transform.TransformPoint(_placementBoxCollider.center),
                _placementBoxCollider.transform.rotation,
                _placementBoxCollider.transform.lossyScale
            );
            Gizmos.DrawCube(Vector3.zero, _placementBoxCollider.size);
            Gizmos.matrix = originalMatrix;
        }
    }
}
