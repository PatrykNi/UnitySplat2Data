using UnityEngine;

/// <summary>
/// Defines a renderable object in the scene, potentially consisting of a mesh and/or a splat object,
/// along with its class and placement settings.
/// This class needs to be in its own file: RenderableObject.cs
/// </summary>
[System.Serializable]
public class RenderableObject
{
    [Tooltip("Mesh object to be rendered and used for transformations.")]
    public GameObject meshObject;

    [Tooltip("Gaussian Splat object to be rendered and used for transformations.")]
    public GameObject splatObject;

    [Tooltip("Class assigned to this object, e.g. car, tree.")]
    public string objectClass;

    [Header("Randomize Rotation Axes for Placement")]
    [Tooltip("Whether to randomize rotation around the X axis for this object during placement?")]
    public bool randomizeX = true;
    [Tooltip("Whether to randomize rotation around the Y axis for this object during placement?")]
    public bool randomizeY = true;
    [Tooltip("Whether to randomize rotation around the Z axis for this object during placement?")]
    public bool randomizeZ = true;
}
