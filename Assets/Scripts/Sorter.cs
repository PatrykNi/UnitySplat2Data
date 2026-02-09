using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using System.IO;
#if UNITY_EDITOR
using UnityEditor;
#endif
using GaussianSplatting.Runtime;

public class MeshAndSplatRenderOrderUtility : MonoBehaviour
{
    [System.Serializable]
    public class RenderablePair
    {
        public MeshRenderer meshRenderer;
        public GaussianSplatRenderer splatRenderer;
    }

    [SerializeField] private List<RenderablePair> renderableObjects;
    private Camera cameraToUse;
    [SerializeField] private int baseRenderOrderForSplats = 5;

    [Header("Sampling Settings")]
    [SerializeField] private int voxelsPerObject = 100;
    [SerializeField] private float voxelSamplingDensity = 1.0f;

    [Header("Console Logging")]
    [SerializeField] private bool logOrderToConsole = true;

    [Header("Gizmo Debugging")]
    [SerializeField] private bool showDebugCentersGizmo = true;
    [SerializeField] private float debugCenterSizeGizmo = 0.5f;
    [SerializeField] private float debugDurationGizmo = 2f;

    [Header("2D Debug Image Generation")] [Tooltip("Enable 2D debug image generation showing voxels, centers, and the camera.")] [SerializeField] private bool generateDebugImage = true;
    [Tooltip("Folder path in Assets for saving the debug image. E.g. 'Assets/DebugOutput/SorterImages''")] [SerializeField] private string debugImageFolderPath = "Assets/DebugOutput/SorterImages";
    [Tooltip("File name for the debug image.")] [SerializeField] private string debugImageFileName = "SorterDebugImage.png";
    [Tooltip("Maximum dimension (width or height) for the debug image.")] [SerializeField] private int debugImageMaxDimension = 1024;
    [Tooltip("Padding around scene content in world units for the debug image.")] [SerializeField] private float debugImageWorldPadding = 5.0f;
    [SerializeField] private Color debugImageBackgroundColor = Color.black; [SerializeField] private Color cameraMarkerColor = Color.red; [SerializeField] private int cameraMarkerSize = 10; // Radius in pixels
    [SerializeField] private Color objectCenterMarkerColor = Color.yellow; [SerializeField] private int objectCenterMarkerSize = 7; // Radius in pixels
    [SerializeField] private int voxelPixelSize = 2; // Draw voxel as a square of this size
    [Tooltip("Colors to toggle for object voxels.")] [SerializeField] private List<Color> objectVoxelColors = new List<Color>() { Color.green, Color.blue, Color.magenta, Color.cyan, new Color(1f, 0.5f, 0f), // Orange new Color(0.5f, 0f, 1f) // Purple
    };


    [Header("Automatic Calculation")]
    [SerializeField] private float autoCalculateStartTime = 1.0f;
    [SerializeField] private float autoCalculateRepeatRate = 5.0f;

    [SerializeField, ReadOnly] private string[] renderOrderResults;

    public struct ProcessedObjectData
    {
        public GaussianSplatRenderer SplatRenderer;
        public Vector2 Center2D;
        public float Distance2D;
        public List<Vector3> SampledVoxels;
        public Color DebugColor;
    }

    private void OnEnable()
    {
        if (autoCalculateRepeatRate > 0)
        {
            InvokeRepeating(nameof(CalculateRenderOrderFromInspector), autoCalculateStartTime, autoCalculateRepeatRate);
        }
        else if (autoCalculateStartTime >= 0)
        {
            Invoke(nameof(CalculateRenderOrderFromInspector), autoCalculateStartTime);
        }
    }

    private void OnDisable()
    {
        CancelInvoke(nameof(CalculateRenderOrderFromInspector));
    }

    public void SetRenderablePairs(List<RenderablePair> newPairs)
    {
        renderableObjects = newPairs;
    }

    public void SetCameraToUse(Camera newCamera)
    {
        cameraToUse = newCamera;
    }

    [ContextMenu("Calculate Render Order (2D Distance)")]
    public void CalculateRenderOrderFromInspector()
    {
        if (renderableObjects == null || renderableObjects.Count == 0)
        {
            Debug.LogWarning("MeshAndSplatRenderOrderUtility: No assigned objects (pairs) for splat sorting.");
            return;
        }
        if (cameraToUse == null)
        {
            Debug.LogError("MeshAndSplatRenderOrderUtility: Camera has not been assigned for splat sorting.");
            return;
        }

        
        CalculateRenderOrderForSplatRenderables(renderableObjects, cameraToUse, baseRenderOrderForSplats, logOrderToConsole, this);

        
    }

    [ContextMenu("Generate Debug Image Manually")]
    public void GenerateDebugImageManually()
    {
        if (!generateDebugImage)
        {
            Debug.LogWarning("MeshAndSplatRenderOrderUtility: Debug image generation is disabled in the inspector (generateDebugImage = false).");
            return;
        }

        if (renderableObjects == null || renderableObjects.Count == 0)
        {
            Debug.LogWarning("MeshAndSplatRenderOrderUtility: No assigned objects (pairs) for splat sorting. Cannot generate debug image.");
            return;
        }
        if (cameraToUse == null)
        {
            Debug.LogError("MeshAndSplatRenderOrderUtility: Camera has not been assigned. Cannot generate debug image.");
            return;
        }

        List<ProcessedObjectData> processedData = CalculateRenderOrderForSplatRenderables(renderableObjects, cameraToUse, baseRenderOrderForSplats, false, this); 

        if (processedData != null && processedData.Count > 0)
        {
            GenerateAndSaveDebugImage(processedData, cameraToUse, this);
        }
        else
        {
            Debug.LogWarning("MeshAndSplatRenderOrderUtility: No data to process for debug image generation. Will attempt to draw only the camera if available.");
            if (cameraToUse != null)
            {
                GenerateAndSaveDebugImage(new List<ProcessedObjectData>(), cameraToUse, this);
            }
        }
    }




    public static List<ProcessedObjectData> CalculateRenderOrderForSplatRenderables(List<RenderablePair> pairs, Camera camera, int baseSplatOrder, bool logToConsoleThisCall, MeshAndSplatRenderOrderUtility utilityInstance)
    {
        if (camera == null) return null;
        if (utilityInstance == null)
        {
            Debug.LogError("No MeshAndSplatRenderOrderUtility instance found for settings.");
            return null;
        }

        var validPairs = pairs.Where(p => p != null && p.meshRenderer != null && p.splatRenderer != null).ToList();
        if (validPairs.Count == 0)
        {
            if (utilityInstance.logOrderToConsole && logToConsoleThisCall) Debug.LogWarning("No valid Mesh+Splat pairs found in the provided list.");
            return new List<ProcessedObjectData>();
        }

        List<ProcessedObjectData> objectDataList = new List<ProcessedObjectData>();
        Vector2 cameraPos2D = new Vector2(camera.transform.position.x, camera.transform.position.z);
        int colorIndex = 0;

        foreach (var pair in validPairs)
        {
            if (pair.meshRenderer == null || pair.splatRenderer == null) continue;

            (Vector2 objectCenter2D, List<Vector3> sampledVoxels) = Calculate2DCenterAndVoxels(pair, utilityInstance);
            float distance2D = Vector2.Distance(cameraPos2D, objectCenter2D);

            Color objectColor = Color.white;
            if (utilityInstance.objectVoxelColors != null && utilityInstance.objectVoxelColors.Count > 0)
            {
                objectColor = utilityInstance.objectVoxelColors[colorIndex % utilityInstance.objectVoxelColors.Count];
                colorIndex++;
            }

            objectDataList.Add(new ProcessedObjectData
            {
                SplatRenderer = pair.splatRenderer,
                Center2D = objectCenter2D,
                Distance2D = distance2D,
                SampledVoxels = sampledVoxels,
                DebugColor = objectColor
            });

            if (utilityInstance.showDebugCentersGizmo)
            {
                Vector3 center3D = new Vector3(objectCenter2D.x, pair.meshRenderer.bounds.center.y, objectCenter2D.y);
                Debug.DrawRay(center3D, Vector3.up * utilityInstance.debugCenterSizeGizmo, Color.cyan, utilityInstance.debugDurationGizmo);
                Debug.DrawRay(center3D, Vector3.right * utilityInstance.debugCenterSizeGizmo, Color.cyan, utilityInstance.debugDurationGizmo);
                Debug.DrawRay(center3D, Vector3.forward * utilityInstance.debugCenterSizeGizmo, Color.cyan, utilityInstance.debugDurationGizmo);
            }
        }

        var sortedData = objectDataList.OrderByDescending(data => data.Distance2D).ToList();

        
        if (utilityInstance.logOrderToConsole && logToConsoleThisCall) Debug.Log("==== Gaussian Splat Rendering Order (Based on 2D Distance - Back-to-Front) ====");

        List<string> resultsLog = new List<string>();
        int currentRenderOrder = baseSplatOrder;

        for (int i = 0; i < sortedData.Count; i++)
        {
            var data = sortedData[i];
            data.SplatRenderer.m_RenderOrder = currentRenderOrder;

            if (utilityInstance.logOrderToConsole && logToConsoleThisCall)
            {
                Debug.Log($"{i + 1}. {data.SplatRenderer.name} - 2D Distance: {data.Distance2D:F2} - Order: {currentRenderOrder}");
            }
            resultsLog.Add($"{data.SplatRenderer.name} - Order: {currentRenderOrder}");
            currentRenderOrder++;
        }

        if (utilityInstance != null) utilityInstance.renderOrderResults = resultsLog.ToArray();
        return sortedData;
    }

    private static (Vector2 center, List<Vector3> sampledVoxels) Calculate2DCenterAndVoxels(RenderablePair pair, MeshAndSplatRenderOrderUtility utility)
    {
        var voxels = GetLimitedVoxels(pair, utility.voxelsPerObject, utility.voxelSamplingDensity);

        if (voxels.Count == 0)
        {
            Vector3 boundsCenter = pair.meshRenderer.bounds.center;
            Vector2 center2D = new Vector2(boundsCenter.x, boundsCenter.z);
            return (center2D, voxels);
        }

        Vector2 sum = Vector2.zero;
        foreach (var voxel in voxels)
        {
            sum += new Vector2(voxel.x, voxel.z);
        }
        Vector2 average2D = sum / voxels.Count;

        return (average2D, voxels);
    }

    private static void GenerateAndSaveDebugImage(List<ProcessedObjectData> processedObjects, Camera gameCamera, MeshAndSplatRenderOrderUtility settings)
    {
        if (settings == null) return;

        List<Vector2> allPoints2D = new List<Vector2>();

        if (gameCamera != null)
        {
            allPoints2D.Add(new Vector2(gameCamera.transform.position.x, gameCamera.transform.position.z));
        }

        foreach (var objData in processedObjects)
        {
            allPoints2D.Add(objData.Center2D);
            foreach (var voxel in objData.SampledVoxels)
            {
                allPoints2D.Add(new Vector2(voxel.x, voxel.z));
            }
        }

        if (allPoints2D.Count == 0)
        {
            Debug.LogWarning("No points (camera, centers, voxels) to determine debug image boundaries. Cannot generate image.");
            return;
        }

        float minX = allPoints2D.Min(p => p.x) - settings.debugImageWorldPadding;
        float maxX = allPoints2D.Max(p => p.x) + settings.debugImageWorldPadding;
        float minZ = allPoints2D.Min(p => p.y) - settings.debugImageWorldPadding;
        float maxZ = allPoints2D.Max(p => p.y) + settings.debugImageWorldPadding;

        float worldWidth = maxX - minX;
        float worldHeight = maxZ - minZ;

        if (worldWidth <= 0 || worldHeight <= 0)
        {
            Debug.LogWarning("Invalid world dimensions for debug image. Cannot generate.");
            return;
        }

        int imageWidth, imageHeight;
        if (worldWidth > worldHeight)
        {
            imageWidth = settings.debugImageMaxDimension;
            imageHeight = Mathf.Max(1, (int)(settings.debugImageMaxDimension * (worldHeight / worldWidth)));
        }
        else
        {
            imageHeight = settings.debugImageMaxDimension;
            imageWidth = Mathf.Max(1, (int)(settings.debugImageMaxDimension * (worldWidth / worldHeight)));
        }

        Texture2D debugTexture = new Texture2D(imageWidth, imageHeight, TextureFormat.RGB24, false);

        Color[] backgroundPixels = new Color[imageWidth * imageHeight];
        for (int i = 0; i < backgroundPixels.Length; i++) backgroundPixels[i] = settings.debugImageBackgroundColor;
        debugTexture.SetPixels(backgroundPixels);

        System.Func<Vector2, Vector2Int> worldToPixel = (worldPos) =>
        {
            float uNormalized = (worldPos.x - minX) / worldWidth;
            float vNormalized = (worldPos.y - minZ) / worldHeight;
            return new Vector2Int((int)(uNormalized * imageWidth), (int)(vNormalized * imageHeight));
        };

        foreach (var objData in processedObjects)
        {
            foreach (var voxelWorldPos3D in objData.SampledVoxels)
            {
                Vector2 voxelWorldPos2D = new Vector2(voxelWorldPos3D.x, voxelWorldPos3D.z);
                Vector2Int pixelPos = worldToPixel(voxelWorldPos2D);
                DrawMarker(debugTexture, pixelPos, settings.voxelPixelSize, objData.DebugColor);
            }
        }

        foreach (var objData in processedObjects)
        {
            Vector2Int centerPixelPos = worldToPixel(objData.Center2D);
            DrawMarker(debugTexture, centerPixelPos, settings.objectCenterMarkerSize, settings.objectCenterMarkerColor);
        }

        if (gameCamera != null)
        {
            Vector2 cameraWorldPos2D = new Vector2(gameCamera.transform.position.x, gameCamera.transform.position.z);
            Vector2Int cameraPixelPos = worldToPixel(cameraWorldPos2D);
            DrawMarker(debugTexture, cameraPixelPos, settings.cameraMarkerSize, settings.cameraMarkerColor);
        }

        debugTexture.Apply();

        try
        {
            string fullFolderPath = Path.GetFullPath(settings.debugImageFolderPath);
            if (!Directory.Exists(fullFolderPath))
            {
                Directory.CreateDirectory(fullFolderPath);
            }
            string filePath = Path.Combine(fullFolderPath, settings.debugImageFileName);
            byte[] bytes = debugTexture.EncodeToPNG();
            File.WriteAllBytes(filePath, bytes);
            Debug.Log($"Debug image saved to: {filePath}");
#if UNITY_EDITOR
            AssetDatabase.Refresh();
#endif
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Debug image save error: {e.Message}");
        }
        finally
        {
            if (Application.isEditor && !Application.isPlaying)
            {
                DestroyImmediate(debugTexture);
            }
            else
            {
                Destroy(debugTexture);
            }
        }
    }

    private static void DrawMarker(Texture2D tex, Vector2Int centerPx, int radiusOrSize, Color color)
    {
        int halfSize = radiusOrSize / 2;
        for (int x = -halfSize; x <= halfSize; x++)
        {
            for (int y = -halfSize; y <= halfSize; y++)
            {
                int drawX = centerPx.x + x;
                int drawY = centerPx.y + y;
                if (drawX >= 0 && drawX < tex.width && drawY >= 0 && drawY < tex.height)
                {
                    tex.SetPixel(drawX, drawY, color);
                }
            }
        }
    }

    private static List<Vector3> GetLimitedVoxels(RenderablePair pair, int maxVoxels, float samplingDensity)
    {
        var voxels = new List<Vector3>();
        if (pair.meshRenderer == null)
        {
            return voxels;
        }

        var colliders = pair.meshRenderer.GetComponentsInChildren<Collider>(true);
        if (colliders.Length > 0)
        {
            foreach (var collider in colliders)
            {
                if (!collider.enabled) continue;
                var colliderVoxels = GenerateVoxelsFromCollider(collider, Mathf.Max(1, maxVoxels / colliders.Length)); // Ensure at least 1 voxel per collider if maxVoxels is low
                voxels.AddRange(colliderVoxels);
                if (voxels.Count >= maxVoxels) break;
            }
        }

        if (voxels.Count < maxVoxels / 2)
        {
            var meshFilter = pair.meshRenderer.GetComponent<MeshFilter>();
            if (meshFilter != null && meshFilter.sharedMesh != null)
            {
                var meshVoxels = GenerateVoxelsFromMesh(meshFilter.sharedMesh, pair.meshRenderer.transform, maxVoxels - voxels.Count, samplingDensity); // Try to fill remaining
                voxels.AddRange(meshVoxels);
            }
        }

        if (voxels.Count == 0)
        {
            voxels = GenerateBoundsVoxels(pair.meshRenderer.bounds, maxVoxels);
        }

        if (voxels.Count > maxVoxels)
        {
            voxels = voxels.Take(maxVoxels).ToList();
        }
        return voxels;
    }

    private static List<Vector3> GenerateVoxelsFromCollider(Collider collider, int targetCount)
    {
        var voxels = new List<Vector3>();
        if (targetCount <= 0) return voxels;

        if (collider is BoxCollider boxCollider)
        {
            voxels.AddRange(GenerateBoxVoxels(boxCollider, targetCount, collider.transform));
        }
        else if (collider is SphereCollider sphereCollider)
        {
            voxels.AddRange(GenerateSphereVoxels(sphereCollider, targetCount, collider.transform));
        }
        else if (collider is MeshCollider meshCollider && meshCollider.sharedMesh != null)
        {
            voxels.AddRange(GenerateVoxelsFromMesh(meshCollider.sharedMesh, collider.transform, targetCount, 1.0f));
        }
        else
        {
            voxels.AddRange(GenerateBoundsVoxels(collider.bounds, targetCount));
        }
        return voxels;
    }

    private static List<Vector3> GenerateBoxVoxels(BoxCollider boxCollider, int targetCount, Transform transform)
    {
        var voxels = new List<Vector3>();
        if (targetCount <= 0) return voxels;
        int samplesPerAxis = Mathf.Max(1, Mathf.CeilToInt(Mathf.Pow(targetCount, 1f / 3f)));
        Vector3 center = boxCollider.center;
        Vector3 size = boxCollider.size;
        for (int x = 0; x < samplesPerAxis && voxels.Count < targetCount; x++)
        for (int y = 0; y < samplesPerAxis && voxels.Count < targetCount; y++)
        for (int z = 0; z < samplesPerAxis && voxels.Count < targetCount; z++)
        {
            float xRatio = samplesPerAxis > 1 ? (float)x / (samplesPerAxis - 1) : 0.5f;
            float yRatio = samplesPerAxis > 1 ? (float)y / (samplesPerAxis - 1) : 0.5f;
            float zRatio = samplesPerAxis > 1 ? (float)z / (samplesPerAxis - 1) : 0.5f;
            Vector3 localPos = center + new Vector3(
                Mathf.Lerp(-size.x/2, size.x/2, xRatio),
                Mathf.Lerp(-size.y/2, size.y/2, yRatio),
                Mathf.Lerp(-size.z/2, size.z/2, zRatio));
            voxels.Add(transform.TransformPoint(localPos));
        }
        return voxels;
    }

    private static List<Vector3> GenerateSphereVoxels(SphereCollider sphereCollider, int targetCount, Transform transform)
    {
        var voxels = new List<Vector3>();
        if (targetCount <= 0) return voxels;
        Vector3 center = sphereCollider.center;
        float radius = sphereCollider.radius;
        float phi = Mathf.PI * (3.0f - Mathf.Sqrt(5.0f));
        for (int i = 0; i < targetCount; i++)
        {
            float y = 1 - (i / (float)(targetCount - 1)) * 2;
            float radiusAtY = Mathf.Sqrt(1 - y * y) * radius;
            float theta = phi * i;
            float x = Mathf.Cos(theta) * radiusAtY;
            float z = Mathf.Sin(theta) * radiusAtY;
            Vector3 localPos = center + new Vector3(x, y * radius, z);
            voxels.Add(transform.TransformPoint(localPos));
        }
        return voxels;
    }

    private static List<Vector3> GenerateVoxelsFromMesh(Mesh mesh, Transform transform, int maxVoxels, float samplingDensity)
    {
        var voxels = new List<Vector3>();
        if (mesh == null || !mesh.isReadable) {
             Debug.LogWarning($"Mesh '{mesh?.name}' on '{transform.name}' is not marked as Read/Write Enabled! Cannot sample voxels for debug image from its vertices. Enable Read/Write in the mesh import settings.");
             return voxels;
        }
        var vertices = mesh.vertices;
        if (vertices.Length == 0 || maxVoxels <= 0) return voxels;

        int step = Mathf.Max(1, Mathf.FloorToInt(vertices.Length / (float)maxVoxels));
        for (int i = 0; i < vertices.Length && voxels.Count < maxVoxels; i += step)
        {
            voxels.Add(transform.TransformPoint(vertices[i]));
        }
        return voxels;
    }

    private static List<Vector3> GenerateBoundsVoxels(Bounds bounds, int targetCount)
    {
        var voxels = new List<Vector3>();
        if (targetCount <= 0 || bounds.size == Vector3.zero) return voxels;
        int samplesPerAxis = Mathf.Max(1, Mathf.CeilToInt(Mathf.Pow(targetCount, 1f / 3f)));
        for (int x = 0; x < samplesPerAxis && voxels.Count < targetCount; x++)
        for (int y = 0; y < samplesPerAxis && voxels.Count < targetCount; y++)
        for (int z = 0; z < samplesPerAxis && voxels.Count < targetCount; z++)
        {
            float xRatio = samplesPerAxis > 1 ? (float)x / (samplesPerAxis - 1) : 0.5f;
            float yRatio = samplesPerAxis > 1 ? (float)y / (samplesPerAxis - 1) : 0.5f;
            float zRatio = samplesPerAxis > 1 ? (float)z / (samplesPerAxis - 1) : 0.5f;
            Vector3 pos = new Vector3(
                Mathf.Lerp(bounds.min.x, bounds.max.x, xRatio),
                Mathf.Lerp(bounds.min.y, bounds.max.y, yRatio),
                Mathf.Lerp(bounds.min.z, bounds.max.z, zRatio));
            voxels.Add(pos);
        }
        return voxels;
    }

    public static List<Renderer> GetSortedMeshRenderersForLabeling(IEnumerable<RenderableObject> allSceneRenderableObjects, Camera camera)
    {
        if (camera == null || allSceneRenderableObjects == null) return new List<Renderer>();

        var distances = new List<(Renderer renderer, float distance)>();
        Vector2 cameraPos2D = new Vector2(camera.transform.position.x, camera.transform.position.z);

        foreach (var ro in allSceneRenderableObjects)
        {
            if (ro == null) continue;
            if (ro.meshObject != null)
            {
                Renderer meshRend = ro.meshObject.GetComponent<Renderer>();
                if (meshRend != null && meshRend.enabled && ro.meshObject.activeInHierarchy)
                {
                    Vector2 objectPos2D = new Vector2(meshRend.bounds.center.x, meshRend.bounds.center.z);
                    float distance2D = Vector2.Distance(cameraPos2D, objectPos2D);
                    distances.Add((meshRend, distance2D));
                }
            }
        }

        distances.Sort((a, b) => b.distance.CompareTo(a.distance));
        return distances.Select(d => d.renderer).ToList();
    }


    public class ReadOnlyAttribute : PropertyAttribute { }

#if UNITY_EDITOR
    [CustomPropertyDrawer(typeof(ReadOnlyAttribute))]
    public class ReadOnlyDrawer : PropertyDrawer
    {
        public override float GetPropertyHeight(SerializedProperty property, GUIContent label) => EditorGUI.GetPropertyHeight(property, label, true);
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            GUI.enabled = false;
            EditorGUI.PropertyField(position, property, label, true);
            GUI.enabled = true;
        }
    }
#endif
}
