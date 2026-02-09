using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class MeshSplatPair
{
    [Tooltip("Mesh object whose transformation (position, rotation, scale) is to be copied.")]
    public GameObject meshObject; // Reference to the Mesh object

    [Tooltip("Gaussian Splat object to which the transformation is to be transferred.")]
    public GameObject splatObject; // Reference to the Splat object
}


public class SplatTransformer : MonoBehaviour
{
    [Header("Mesh and Splat Pairs")]
    [Tooltip("List of Mesh -> Gaussian Splat pairs for transformation.")]
    [SerializeField] private List<MeshSplatPair> meshSplatPairs;

    [Header("Update Settings")]
    [Tooltip("Time in seconds between transformation updates. Set to 0 or less to update every frame (Update).")]
    public float updateInterval = 0.1f; // Default 0.1 seconds, every 100ms

    private float _timer; // Timer to track elapsed time

    /// <summary>
    /// Update method is called once per frame.
    /// Used to control the frequency of transform transfer.
    /// </summary>
    void Update()
    {
        // If updateInterval is 0 or less, update every frame.
        // This is the most responsive option but can be expensive for many objects.
        if (updateInterval <= 0)
        {
            TransferTransformsToSplats();
        }
        else // Otherwise, update at the fixed time interval.
        {
            _timer += Time.deltaTime; // Add time elapsed since the last frame
            if (_timer >= updateInterval)
            {
                TransferTransformsToSplats(); // Perform transform transfer
                _timer = 0f; // Reset timer
            }
        }
    }

    /// <summary>
    /// Sets the list of MeshSplatPair. Used by SceneManager.
    /// </summary>
    /// <param name="newPairs">New list of MeshSplatPair.</param>
    public void SetMeshSplatPairs(List<MeshSplatPair> newPairs)
    {
        meshSplatPairs = newPairs;
        Debug.Log($"SplatTransformer: Updated meshSplatPairs list with {newPairs.Count} entries.");
    }

    /// <summary>
    /// Transfers position, rotation, and scale (with negated X axis)
    /// from Mesh objects to corresponding Splat objects.
    /// Can be called manually from the context menu in the Unity Inspector.
    /// </summary>
    [ContextMenu("Transfer Transforms to Splats")]
    public void TransferTransformsToSplats()
    {
        Debug.Log("Starting transform transfer from Meshes to Splats...");

        // Check if the list of pairs is empty or uninitialized
        if (meshSplatPairs == null || meshSplatPairs.Count == 0)
        {
            Debug.LogWarning("Mesh and Splat Pairs list is empty! No transforms to transfer.");
            return; // Exit the method if there is nothing to process
        }

        int transferredCount = 0; // Counter for transferred pairs

        // Iterate through each pair in the list
        foreach (MeshSplatPair pair in meshSplatPairs)
        {
            // Check if both objects in the pair are assigned
            if (pair.meshObject == null)
            {
                Debug.LogWarning("Mesh Object is not assigned in one of the pairs. Skipping this pair.");
                continue; // Continue to the next pair
            }
            if (pair.splatObject == null)
            {
                Debug.LogWarning($"Splat Object is not assigned for Mesh '{pair.meshObject.name}'. Skipping this pair.");
                continue; // Continue to the next pair
            }

            // Get world transformation properties from the Mesh object
            Vector3 meshWorldPosition = pair.meshObject.transform.position;
            Quaternion meshWorldRotation = pair.meshObject.transform.rotation;
            // Get Mesh local scale to apply modification
            Vector3 meshLocalScale = pair.meshObject.transform.localScale;

            // Apply world position and rotation to the Splat object
            pair.splatObject.transform.position = meshWorldPosition;
            pair.splatObject.transform.rotation = meshWorldRotation;

            // Apply scale with negated X component to the Splat object's LOCAL scale.
            // Important note: Assumes the Splat object does not have a parent with non-identity scale.
            // If it does, applying modified local scale may result in unexpected world scale.
            Vector3 splatLocalScale = new Vector3(
                meshLocalScale.x * -1, // Negate X scale
                meshLocalScale.y,
                meshLocalScale.z
            );
            pair.splatObject.transform.localScale = splatLocalScale;

            transferredCount++; // Increment transferred pairs counter
        }

        Debug.Log($"Transform transfer complete. Transferred transforms for {transferredCount} pairs.");
    }
}