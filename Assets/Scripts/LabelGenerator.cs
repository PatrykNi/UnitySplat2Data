using UnityEngine;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Globalization;
using System.Text;


public struct YoloLabel
{
    public int ClassId;
    public float CenterX;
    public float CenterY;
    public float Width;
    public float Height;

    public override string ToString()
    {
        return string.Format(CultureInfo.InvariantCulture,
                             "{0} {1:F6} {2:F6} {3:F6} {4:F6}",
                             ClassId, CenterX, CenterY, Width, Height);
    }
}

public struct YoloSegmentationLabel
{
    public int ClassId;
    public List<Vector2> NormalizedPoints; 

    public override string ToString()
    {
        if (NormalizedPoints == null || NormalizedPoints.Count < 3) 
        {
            return string.Empty;
        }

        StringBuilder sb = new StringBuilder();
        sb.Append(ClassId);
        foreach (var point in NormalizedPoints)
        {
            sb.AppendFormat(CultureInfo.InvariantCulture, " {0:F6} {1:F6}", point.x, point.y);
        }
        return sb.ToString();
    }
}


public class LabelGenerator : MonoBehaviour
{

    [Header("Masking Settings")]
    [SerializeField] private Color _maskBackgroundColor = Color.green;
    [SerializeField] private Color _combinedMaskClearColor = Color.black;
    [SerializeField] private string _maskLayerName = "MaskLayer";

    [Header("Debugging")]
    [SerializeField] private bool _saveDebugMasks = true;
    [SerializeField] private string _debugMasksSubfolder = "DebugMasks";
    [SerializeField] private float _colorComparisonThresholdBBox = 0.05f;

    [Header("Label Filtering")]
    [Tooltip("Ignores mask parts smaller than this percentage of the largest part for a given object.")]
    [Range(0f, 1f)]
    [SerializeField] private float _minSizeThreshold = 0.15f;


    [Header("Segmentation Settings")]
    [SerializeField] private bool _saveSegmentationLabels = true;
    [SerializeField] private string _segmentationLabelsSubfolder = "Labels_Segmentation";
    [Tooltip("Tolerance for polygon contour simplification. Lower value = more detail.")]
    [SerializeField] private float _segmentationSimplificationTolerance = 1.5f;


    private Dictionary<Renderer, Color> _rendererToMaskColorMap;
    private Dictionary<Color, string> _maskColorToClassMap;
    private Dictionary<string, int> _classToIdMap;
    private List<string> _uniqueClasses;

    private RenderTexture _singleObjectRenderTexture;
    private Texture2D _readTexture2D;

    private int _maskLayerValue;

    private CameraClearFlags _originalClearFlags;
    private Color _originalBackgroundColor;
    private int _originalCullingMask;
    private RenderTexture _originalTargetTexture;

    private bool _isInitialized = false;
    private int _renderWidth;
    private int _renderHeight;
    private int _instanceId;


    void Awake()
    {
        _instanceId = GetInstanceID();
        Debug.Log($"LabelGenerator (ID: {_instanceId}): Awake() - Starting.");
        _isInitialized = false;
        _maskLayerValue = LayerMask.NameToLayer(_maskLayerName);
        if (_maskLayerValue == -1)
        {
            Debug.LogError($"LabelGenerator (ID: {_instanceId}): Layer '{_maskLayerName}' does not exist!");
            enabled = false;
            Debug.LogWarning($"LabelGenerator (ID: {_instanceId}): Awake() - Component turned off.");
            return;
        }
        _rendererToMaskColorMap = new Dictionary<Renderer, Color>();
        _maskColorToClassMap = new Dictionary<Color, string>();
        _classToIdMap = new Dictionary<string, int>();
        _uniqueClasses = new List<string>();
        Debug.Log($"LabelGenerator (ID: {_instanceId}): Awake() - Finished.");
    }

    public void Initialize(List<RenderableObject> objectsToTrack, int renderWidth, int renderHeight)
    {
        Debug.Log($"LabelGenerator (ID: {_instanceId}): Starting Initialize().");
        if (!enabled)
        {
            Debug.LogError($"LabelGenerator (ID: {_instanceId}): Attempted Initialize() on a disabled component. Aborting.");
            _isInitialized = false;
            return;
        }

        if (_isInitialized)
        {
            Debug.LogWarning($"LabelGenerator (ID: {_instanceId}): Reinitialization via CleanUp().");
            CleanUp();
        }
        _isInitialized = false;

        _rendererToMaskColorMap = new Dictionary<Renderer, Color>();
        _maskColorToClassMap = new Dictionary<Color, string>();
        _classToIdMap = new Dictionary<string, int>();
        _uniqueClasses = new List<string>();

        _renderWidth = renderWidth;
        _renderHeight = renderHeight;

        if (objectsToTrack == null)
        {
            Debug.LogError($"LabelGenerator (ID: {_instanceId}): The ‘objectsToTrack’ list is null. Aborting.");
            return;
        }

        float hueIncrement = 0.618033988749895f;
        float currentHue = Random.value;

        foreach (var renderableObj in objectsToTrack)
        {
            if (renderableObj == null || string.IsNullOrEmpty(renderableObj.objectClass) || renderableObj.meshObject == null) continue;

            Renderer meshRenderer = renderableObj.meshObject.GetComponent<Renderer>();
            if (meshRenderer != null)
            {
                if (!_uniqueClasses.Contains(renderableObj.objectClass))
                {
                    _uniqueClasses.Add(renderableObj.objectClass);
                    _classToIdMap[renderableObj.objectClass] = _uniqueClasses.Count - 1;
                }

                Color uniqueColor;
                int attempts = 0;
                do
                {
                    currentHue = (currentHue + hueIncrement) % 1.0f;
                    uniqueColor = Color.HSVToRGB(currentHue, Random.Range(0.7f, 1.0f), Random.Range(0.7f, 1.0f));
                    uniqueColor.a = 1.0f;
                    attempts++;
                    if (attempts > objectsToTrack.Count * 2 + 100) break;
                } while (IsColorTooSimilar(uniqueColor, _maskBackgroundColor, 0.2f) ||
                         IsColorTooSimilar(uniqueColor, _combinedMaskClearColor, 0.2f) ||
                         _maskColorToClassMap.Keys.Any(existingColor => IsColorTooSimilar(uniqueColor, existingColor, _colorComparisonThresholdBBox)));

                if (!_maskColorToClassMap.Keys.Any(existingColor => IsColorTooSimilar(uniqueColor, existingColor, _colorComparisonThresholdBBox)) && attempts <= objectsToTrack.Count * 2 + 99)
                {
                    _rendererToMaskColorMap[meshRenderer] = uniqueColor;
                    _maskColorToClassMap[uniqueColor] = renderableObj.objectClass;
                }
            }
        }

        if (_renderWidth <= 0 || _renderHeight <= 0)
        {
            Debug.LogError($"LabelGenerator (ID: {_instanceId}): Invalid rendering dimensions. Aborting.");
            return;
        }

        if (_singleObjectRenderTexture != null)
        {
            if (_singleObjectRenderTexture.IsCreated()) _singleObjectRenderTexture.Release();
            Destroy(_singleObjectRenderTexture);
        }
        _singleObjectRenderTexture = new RenderTexture(_renderWidth, _renderHeight, 24, RenderTextureFormat.ARGB32);
        _singleObjectRenderTexture.name = "LabelGenerator_SingleObjectRT";
        if (!_singleObjectRenderTexture.Create())
        {
            Debug.LogError($"LabelGenerator (ID: {_instanceId}): Failed to create _singleObjectRenderTexture. Aborting.");
            return;
        }

        if (_readTexture2D != null) { Destroy(_readTexture2D); }
        _readTexture2D = new Texture2D(_renderWidth, _renderHeight, TextureFormat.ARGB32, false);
        _readTexture2D.name = "LabelGenerator_CombinedMaskCPU";

        Debug.Log($"LabelGenerator (ID: {_instanceId}): Inicjalizacja zakoñczona pomyœlnie.");
        _isInitialized = true;
    }


    public void GenerateLabelsForFrame(Camera mainCamera, string imageFilePath, List<Renderer> sortedMeshRenderers)
    {
        if (!enabled || !_isInitialized)
        {
            Debug.LogError($"LabelGenerator (ID: {_instanceId}): GenerateLabelsForFrame was called, but it is not initialized or is disabled.");
            return;
        }

        PrepareCameraAndRenderers(mainCamera, sortedMeshRenderers);

        string imageDirectory = Path.GetDirectoryName(imageFilePath);
        DirectoryInfo parentDir = Directory.GetParent(imageDirectory);

        if (parentDir == null)
        {
            Debug.LogError($"LabelGenerator (ID: {_instanceId}): Unable to determine the parent folder for ‘{imageDirectory}’.");
            RestoreCameraSettings(mainCamera);
            return;
        }

        string projectDataPath = parentDir.FullName;
        if (_saveDebugMasks)
        {
            SaveDebugMask(projectDataPath, imageFilePath);
        }

        if (sortedMeshRenderers != null && sortedMeshRenderers.Count > 0)
        {
            var (bboxLabels, segLabels) = ProcessMaskAndGenerateLabels(_readTexture2D);

            if (bboxLabels.Count > 0)
            {
                SaveLabelsToFile(bboxLabels, projectDataPath, "Labels_Detection", imageFilePath);
            }

            
            if (_saveSegmentationLabels && segLabels.Count > 0)
            {
                SaveLabelsToFile(segLabels, projectDataPath, _segmentationLabelsSubfolder, imageFilePath);
            }
        }

        RestoreCameraSettings(mainCamera);
    }

    private void PrepareCameraAndRenderers(Camera mainCamera, List<Renderer> sortedMeshRenderers)
    {
        _originalClearFlags = mainCamera.clearFlags;
        _originalBackgroundColor = mainCamera.backgroundColor;
        _originalCullingMask = mainCamera.cullingMask;
        _originalTargetTexture = mainCamera.targetTexture;

        mainCamera.clearFlags = CameraClearFlags.SolidColor;
        mainCamera.backgroundColor = _maskBackgroundColor;
        mainCamera.cullingMask = (1 << _maskLayerValue);
        mainCamera.targetTexture = _singleObjectRenderTexture;

        Color[] clearPixels = new Color[_renderWidth * _renderHeight];
        for (int i = 0; i < clearPixels.Length; i++) clearPixels[i] = _combinedMaskClearColor;
        _readTexture2D.SetPixels(clearPixels);
        _readTexture2D.Apply();

        Dictionary<Renderer, int> originalLayers = new Dictionary<Renderer, int>();

        if (sortedMeshRenderers != null)
        {
            foreach (Renderer currentRenderer in sortedMeshRenderers)
            {
                if (currentRenderer == null || !currentRenderer.gameObject.activeInHierarchy || !currentRenderer.enabled) continue;
                if (!_rendererToMaskColorMap.TryGetValue(currentRenderer, out Color objectSpecificColor)) continue;

                originalLayers[currentRenderer] = currentRenderer.gameObject.layer;
                SetLayerRecursively(currentRenderer.gameObject, _maskLayerValue);

                mainCamera.Render();

                RenderTexture.active = _singleObjectRenderTexture;
                Texture2D tempSingleMask = new Texture2D(_renderWidth, _renderHeight, TextureFormat.ARGB32, false);
                tempSingleMask.ReadPixels(new Rect(0, 0, _renderWidth, _renderHeight), 0, 0);
                tempSingleMask.Apply();
                RenderTexture.active = null;

                Color[] currentCombinedPixels = _readTexture2D.GetPixels();
                Color[] singleMaskPixels = tempSingleMask.GetPixels();

                for (int i = 0; i < singleMaskPixels.Length; i++)
                {
                    if (!IsColorTooSimilar(singleMaskPixels[i], _maskBackgroundColor, 0.1f) && singleMaskPixels[i].a > 0.1f)
                    {
                        currentCombinedPixels[i] = objectSpecificColor;
                    }
                }
                _readTexture2D.SetPixels(currentCombinedPixels);
                _readTexture2D.Apply();
                Destroy(tempSingleMask);
                SetLayerRecursively(currentRenderer.gameObject, originalLayers[currentRenderer]);
            }
        }
    }

    private void RestoreCameraSettings(Camera mainCamera)
    {
        mainCamera.clearFlags = _originalClearFlags;
        mainCamera.backgroundColor = _originalBackgroundColor;
        mainCamera.cullingMask = _originalCullingMask;
        mainCamera.targetTexture = _originalTargetTexture;
    }



    /// <summary>
    /// Main function for processing the mask. Finds all components, filters them,
    /// and then generates Bounding Box labels and (optionally) Segmentation labels.
    /// </summary>
    private (List<YoloLabel> BboxLabels, List<YoloSegmentationLabel> SegLabels) ProcessMaskAndGenerateLabels(Texture2D maskTexture)
    {
        var bboxLabels = new List<YoloLabel>();
        var segLabels = new List<YoloSegmentationLabel>();

        if (maskTexture == null || _maskColorToClassMap == null || _classToIdMap == null) return (bboxLabels, segLabels);

        int width = maskTexture.width;
        int height = maskTexture.height;
        Color32[] pixels = maskTexture.GetPixels32();
        bool[,] visited = new bool[width, height];

        var allFoundComponents = new Dictionary<Color, List<(RectInt Bbox, HashSet<Vector2Int> Pixels)>>();

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                if (visited[x, y]) continue;

                Color32 pixelColor = pixels[y * width + x];
                if (IsColorTooSimilar(pixelColor, _combinedMaskClearColor, 0.02f) || IsColorTooSimilar(pixelColor, _maskBackgroundColor, 0.02f))
                {
                    visited[x, y] = true;
                    continue;
                }

                Color matchedColor = _maskColorToClassMap.Keys.FirstOrDefault(c => IsColorTooSimilar(pixelColor, c, _colorComparisonThresholdBBox));

                if (matchedColor != default)
                {
                    var component = FindConnectedComponent(pixels, width, height, x, y, matchedColor, ref visited);
                    if (!allFoundComponents.ContainsKey(matchedColor))
                    {
                        allFoundComponents[matchedColor] = new List<(RectInt, HashSet<Vector2Int>)>();
                    }
                    allFoundComponents[matchedColor].Add(component);
                }
                else
                {
                    visited[x, y] = true;
                }
            }
        }

        foreach (var kvp in allFoundComponents)
        {
            Color objectColor = kvp.Key;
            var components = kvp.Value;
            if (components.Count == 0) continue;

            int maxArea = components.Max(c => c.Bbox.width * c.Bbox.height);
            float minAreaThreshold = maxArea * _minSizeThreshold;

            if (!_maskColorToClassMap.TryGetValue(objectColor, out string objectClass) || !_classToIdMap.TryGetValue(objectClass, out int classId)) continue;

            foreach (var (box, componentPixels) in components)
            {
                if (box.width * box.height >= minAreaThreshold)
                {
                    float yoloX = (box.xMin + box.width / 2f) / width;
                    float yoloY = 1.0f - ((box.yMin + box.height / 2f) / height);
                    float yoloW = (float)box.width / width;
                    float yoloH = (float)box.height / height;
                    bboxLabels.Add(new YoloLabel { ClassId = classId, CenterX = Mathf.Clamp01(yoloX), CenterY = Mathf.Clamp01(yoloY), Width = Mathf.Clamp01(yoloW), Height = Mathf.Clamp01(yoloH) });


                    if (_saveSegmentationLabels)
                    {
                        List<Vector2Int> contour = FindContour(componentPixels, width, height);
                        List<Vector2Int> simplifiedContour = DouglasPeucker(contour, _segmentationSimplificationTolerance);
                        
                        if (simplifiedContour != null && simplifiedContour.Count >= 3)
                        {
                            var normalizedPoints = simplifiedContour.Select(p => new Vector2((float)p.x / width, 1.0f - (float)p.y / height)).ToList();
                            segLabels.Add(new YoloSegmentationLabel { ClassId = classId, NormalizedPoints = normalizedPoints });
                        }
                    }
                }
            }
        }

        return (bboxLabels, segLabels);
    }

    /// <summary>
    /// Finds all connected pixels of the component and its Bounding Box.
    /// </summary>
    private (RectInt Bbox, HashSet<Vector2Int> Pixels) FindConnectedComponent(Color32[] pixels, int width, int height, int startX, int startY, Color targetColor, ref bool[,] visited)
    {
        var componentPixels = new HashSet<Vector2Int>();
        var queue = new Queue<Vector2Int>();

        queue.Enqueue(new Vector2Int(startX, startY));
        visited[startX, startY] = true;
        
        RectInt bbox = new RectInt(startX, startY, 1, 1);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            componentPixels.Add(current);

            bbox.xMin = Mathf.Min(bbox.xMin, current.x);
            bbox.yMin = Mathf.Min(bbox.yMin, current.y);
            bbox.xMax = Mathf.Max(bbox.xMax, current.x + 1);
            bbox.yMax = Mathf.Max(bbox.yMax, current.y + 1);

            for (int i = 0; i < 4; i++)
            {
                int nx = current.x + (i == 0 ? 1 : (i == 1 ? -1 : 0));
                int ny = current.y + (i == 2 ? 1 : (i == 3 ? -1 : 0));

                if (nx >= 0 && nx < width && ny >= 0 && ny < height && !visited[nx, ny] && IsColorTooSimilar(pixels[ny * width + nx], targetColor, _colorComparisonThresholdBBox))
                {
                    visited[nx, ny] = true;
                    queue.Enqueue(new Vector2Int(nx, ny));
                }
            }
        }
        return (bbox, componentPixels);
    }



    /// <summary>
    /// Finds the object contour using the Moore-Neighbor Tracing algorithm.
    /// </summary>
    private List<Vector2Int> FindContour(HashSet<Vector2Int> pixels, int width, int height)
    {
        var contour = new List<Vector2Int>();
        if (pixels.Count == 0) return contour;
        Vector2Int startPoint = pixels.OrderBy(p => p.y).ThenBy(p => p.x).First();
        
        Vector2Int currentPoint = startPoint;
        Vector2Int lastBacktrackPoint = new Vector2Int(startPoint.x - 1, startPoint.y); 
        
        int[] row = { -1, -1, -1, 0, 1, 1, 1, 0 };
        int[] col = { -1, 0, 1, 1, 1, 0, -1, -1 };

        do
        {
            contour.Add(currentPoint);
            
            int backtrackIndex = 0;
            for(int i=0; i<8; ++i) {
                if(currentPoint.x + col[i] == lastBacktrackPoint.x && currentPoint.y + row[i] == lastBacktrackPoint.y) {
                    backtrackIndex = i;
                    break;
                }
            }

            bool nextPointFound = false;
            for (int i = 1; i <= 8; i++)
            {
                int checkIndex = (backtrackIndex + i) % 8;
                Vector2Int next = new Vector2Int(currentPoint.x + col[checkIndex], currentPoint.y + row[checkIndex]);

                if (pixels.Contains(next))
                {
                    lastBacktrackPoint = currentPoint;
                    currentPoint = next;
                    nextPointFound = true;
                    break;
                }
            }
            if(!nextPointFound) break;

        } while (currentPoint != startPoint && contour.Count < pixels.Count * 2);

        return contour;
    }

    /// <summary>
    /// Simplifies the contour using the Ramer-Douglas-Peucker algorithm.
    /// </summary>
    private List<Vector2Int> DouglasPeucker(List<Vector2Int> points, float epsilon)
    {
        if (points == null || points.Count < 3) return points;

        int firstPoint = 0;
        int lastPoint = points.Count - 1;
        List<int> pointIndicesToKeep = new List<int> { firstPoint, lastPoint };

        DouglasPeuckerRecursive(points, firstPoint, lastPoint, epsilon, ref pointIndicesToKeep);

        pointIndicesToKeep.Sort();
        return pointIndicesToKeep.Select(index => points[index]).ToList();
    }

    private void DouglasPeuckerRecursive(List<Vector2Int> points, int firstPoint, int lastPoint, float epsilon, ref List<int> pointIndicesToKeep)
    {
        float maxDistance = 0;
        int indexFarthest = 0;

        for (int i = firstPoint + 1; i < lastPoint; i++)
        {
            float distance = PerpendicularDistance(points[firstPoint], points[lastPoint], points[i]);
            if (distance > maxDistance)
            {
                maxDistance = distance;
                indexFarthest = i;
            }
        }

        if (maxDistance > epsilon)
        {
            pointIndicesToKeep.Add(indexFarthest);
            DouglasPeuckerRecursive(points, firstPoint, indexFarthest, epsilon, ref pointIndicesToKeep);
            DouglasPeuckerRecursive(points, indexFarthest, lastPoint, epsilon, ref pointIndicesToKeep);
        }
    }

    private float PerpendicularDistance(Vector2Int point1, Vector2Int point2, Vector2Int point)
    {
        float area = Mathf.Abs(0.5f * (point1.x * (point2.y - point.y) + point2.x * (point.y - point1.y) + point.x * (point1.y - point2.y)));
        float bottom = Mathf.Sqrt(Mathf.Pow(point1.x - point2.x, 2) + Mathf.Pow(point1.y - point2.y, 2));
        return (bottom == 0) ? 0 : area / bottom * 2;
    }



    private void SaveDebugMask(string projectDataPath, string imageFilePath)
    {
        string debugSavePath = Path.Combine(projectDataPath, _debugMasksSubfolder);
        try
        {
            Directory.CreateDirectory(debugSavePath);
            string maskFileName = Path.GetFileNameWithoutExtension(imageFilePath) + "_mask.png";
            string fullMaskPath = Path.Combine(debugSavePath, maskFileName);
            byte[] bytes = _readTexture2D.EncodeToPNG();
            File.WriteAllBytes(fullMaskPath, bytes);
        }
        catch (System.Exception e)
        {
            Debug.LogError($"LabelGenerator (ID: {_instanceId}): Error saving debug mask {debugSavePath}: {e.Message}");
        }
    }

    private void SaveLabelsToFile<T>(List<T> labels, string projectDataPath, string subfolder, string imageFilePath)
    {
        string labelSavePath = Path.Combine(projectDataPath, subfolder);
        try
        {
            Directory.CreateDirectory(labelSavePath);
            string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(imageFilePath);
            string labelFileName = fileNameWithoutExtension + ".txt";
            string fullLabelPath = Path.Combine(labelSavePath, labelFileName);

            using (StreamWriter writer = new StreamWriter(fullLabelPath, false, System.Text.Encoding.ASCII))
            {
                foreach (var label in labels)
                {
                    string labelString = label.ToString();
                    if (!string.IsNullOrEmpty(labelString))
                    {
                        writer.WriteLine(labelString);
                    }
                }
            }
            Debug.Log($"LabelGenerator (ID: {_instanceId}): Successfully saved {labels.Count} labels to: {fullLabelPath}");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"LabelGenerator (ID: {_instanceId}): Error saving labels to ‘{labelSavePath}’: {e.Message}");
        }
    }
    
    private bool IsColorTooSimilar(Color c1, Color c2, float threshold = 0.1f)
    {
        float deltaR = Mathf.Abs(c1.r - c2.r);
        float deltaG = Mathf.Abs(c1.g - c2.g);
        float deltaB = Mathf.Abs(c1.b - c2.b);
        return (deltaR + deltaG + deltaB) / 3f < threshold;
    }

    private void SetLayerRecursively(GameObject obj, int newLayer)
    {
        if (obj == null) return;
        obj.layer = newLayer;
        foreach (Transform child in obj.transform)
        {
            if (child == null) continue;
            SetLayerRecursively(child.gameObject, newLayer);
        }
    }

    private void CleanUp()
    {
        Debug.Log($"LabelGenerator (ID: {_instanceId}): Starting CleanUp().");
        if (_singleObjectRenderTexture != null)
        {
            if (_singleObjectRenderTexture.IsCreated()) _singleObjectRenderTexture.Release();
            Destroy(_singleObjectRenderTexture);
            _singleObjectRenderTexture = null;
        }
        if (_readTexture2D != null)
        {
            Destroy(_readTexture2D);
            _readTexture2D = null;
        }
        _isInitialized = false;
        Debug.Log($"LabelGenerator (ID: {_instanceId}): CleanUp finished.");
    }

    void OnDestroy() { CleanUp(); }
    void OnApplicationQuit() { CleanUp(); }
}
