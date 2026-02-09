using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;

public class CameraMovementAndCapture : MonoBehaviour
{
    private List<Camera> _cameraPoints = new List<Camera>();
    public string fileNamePrefix = "capture_"; 
    private int _totalScreenshots = 10;
    private bool isMoving = false;
    public Camera activeCamera;

    private Vector3 initialPosition;
    private Quaternion initialRotation;
    private float initialFOV;

    private string _savePathBase;
    public string savePath { get; private set; }

    [Header("Image Processing")]
    public bool processImagesAfterCapture = true;
    public ImageProcessor imageProcessor;

    private LabelGenerator _labelGenerator;
    private SceneManager _sceneManagerRef;
    private int _captureWidth = 1920;
    private int _captureHeight = 1080;

    [Header("Post Processing")]
    public PostProcessingRandomizer postProcessingRandomizer;

   
    void Awake()
    {
        _savePathBase = Path.Combine(Application.dataPath, "../GeneratedData/");

        if (processImagesAfterCapture && imageProcessor == null)
        {
            imageProcessor = GetComponent<ImageProcessor>();
            if (imageProcessor == null)
            {
                imageProcessor = gameObject.AddComponent<ImageProcessor>();
            }
        }

        if (postProcessingRandomizer == null)
        {
            postProcessingRandomizer = FindObjectOfType<PostProcessingRandomizer>();
        }
     }

    void Start()
    {
        if (_cameraPoints.Count < 1 && (cameraPointsFromSceneManager == null || cameraPointsFromSceneManager.Count < 1))         {
            Debug.LogError("CameraMovementAndCapture: Need at least one camera point!");
            return;
        }

        var currentCameraPoints = (cameraPointsFromSceneManager != null && cameraPointsFromSceneManager.Count >=1) ? cameraPointsFromSceneManager : _cameraPoints;


        if (currentCameraPoints.Count > 0 && currentCameraPoints[0] != null)
        {
            initialPosition = currentCameraPoints[0].transform.position;
            initialRotation = currentCameraPoints[0].transform.rotation;
            initialFOV = currentCameraPoints[0].fieldOfView;
        }
        else
        {
             Debug.LogError("CameraMovementAndCapture: First camera point is null or list is empty after setup. Cannot set initial camera state.");
        }

        if (postProcessingRandomizer != null)
        {
            postProcessingRandomizer.InitializeEffects();
        }
        else
        {
            Debug.LogWarning("CameraMovementAndCapture: PostProcessingRandomizer Not found");
        }

        EnsureAllDirectoriesExist();
    }

    private List<Camera> cameraPointsFromSceneManager;

    public void SetCameraPoints(List<Camera> newCameraPoints)
    {
        if (newCameraPoints != null)
        {
            cameraPointsFromSceneManager = newCameraPoints;
            _cameraPoints = new List<Camera>(newCameraPoints);
        }
    }

    public void SetTotalScreenshots(int totalShots)
    {
        _totalScreenshots = totalShots;
    }

    public void SetProjectName(string projectName)
    {
        if (string.IsNullOrEmpty(projectName)) projectName = "DefaultProject";
        savePath = Path.Combine(_savePathBase, projectName);
        EnsureAllDirectoriesExist();
    }

    public void SetCaptureDimensions(int width, int height)
    {
        _captureWidth = width;
        _captureHeight = height;
    }

    public void SetLabelGenerator(LabelGenerator generator)
    {
        _labelGenerator = generator;
    }

    public void SetSceneManagerRef(SceneManager manager)
    {
        _sceneManagerRef = manager;
    }

    public void SetPostProcessingRandomizer(PostProcessingRandomizer randomizer)
    {
        postProcessingRandomizer = randomizer;
        if (postProcessingRandomizer != null && Application.isPlaying)
        {
            postProcessingRandomizer.InitializeEffects();
        }
    }

    private void EnsureAllDirectoriesExist()
    {
        if (!Directory.Exists(savePath)) Directory.CreateDirectory(savePath);
        if (imageProcessor != null)
        {
            imageProcessor.EnsureDirectoryExists(Path.Combine(savePath, "WithoutShadows"));
            imageProcessor.EnsureDirectoryExists(Path.Combine(savePath, "ShadowsOnly"));
            imageProcessor.EnsureDirectoryExists(Path.Combine(savePath, "FinishedPhotos"));
            if (_labelGenerator != null)
            {
                 imageProcessor.EnsureDirectoryExists(Path.Combine(savePath, "ShadowsOnly"));
            }
        }
    }

    public IEnumerator MoveAndCapture()
    {
        var currentCameraPoints = (cameraPointsFromSceneManager != null && cameraPointsFromSceneManager.Count >= 1) ? cameraPointsFromSceneManager : _cameraPoints;
        if (currentCameraPoints.Count < 1)
        {
            Debug.LogError("CameraMovementAndCapture: You need atleast one camera!");
            yield break;
        }
        if (_sceneManagerRef == null && _labelGenerator != null)
        {
             Debug.LogError("SceneManager reference not set, cannot generate labels.");
             yield break;
        }

        activeCamera = currentCameraPoints[0];

        if (activeCamera != null)
        {
            initialPosition = activeCamera.transform.position;
            initialRotation = activeCamera.transform.rotation;
            initialFOV = activeCamera.fieldOfView;
        }
        else
        {
            Debug.LogError("First camera point is null!");
            yield break;
        }

        isMoving = true;

        List<CameraKeyframe> cameraKeyframes = GenerateCameraKeyframes(currentCameraPoints);

        if (_labelGenerator != null)
        {
            string labelsDir = Path.Combine(savePath, "ShadowsOnly");
            if (!Directory.Exists(labelsDir)) Directory.CreateDirectory(labelsDir);
        }

        SetLayer("GaussianSplatsHDRPPass", "Default");
        activeCamera.cullingMask &= ~(1 << LayerMask.NameToLayer("Default"));
        yield return StartCoroutine(CapturePhaseWithKeyframes(cameraKeyframes, "WithoutShadows/"));

        SetLayer("GaussianSplatsHDRPPass", "Gaussian");
        activeCamera.cullingMask |= (1 << LayerMask.NameToLayer("Default"));
        yield return new WaitForSeconds(0.1f);
        yield return StartCoroutine(CapturePhaseWithKeyframes(cameraKeyframes, "ShadowsOnly/"));

        if (activeCamera != null) {
            activeCamera.transform.position = initialPosition;
            activeCamera.transform.rotation = initialRotation;
            activeCamera.fieldOfView = initialFOV;
        }

        if (postProcessingRandomizer != null)
        {
            postProcessingRandomizer.RestoreManagedEffectsToOriginalSessionState();
        }

        if (processImagesAfterCapture && imageProcessor != null)
        {
            imageProcessor.folderObjects = Path.Combine(savePath, "WithoutShadows");
            imageProcessor.folderShadows = Path.Combine(savePath, "ShadowsOnly");
            imageProcessor.folderOutput = Path.Combine(savePath, "FinishedPhotos");
            imageProcessor.ProcessImages();
        }
        isMoving = false;
        Debug.Log("CameraMovementAndCapture: Finished proces MoveAndCapture.");
    }

    private class CameraKeyframe
    {
        public Vector3 Position;
        public Quaternion Rotation;
        public float FieldOfView;
        public CameraKeyframe(Vector3 pos, Quaternion rot, float fov) { Position = pos; Rotation = rot; FieldOfView = fov; }
    }

    private List<CameraKeyframe> GenerateCameraKeyframes(List<Camera> camPoints)
    {
        List<CameraKeyframe> keyframes = new List<CameraKeyframe>();
        if (camPoints == null || camPoints.Count < 1) return keyframes;

        if (camPoints.Count == 1)
        {
            if (camPoints[0] != null)
            {
                 keyframes.Add(new CameraKeyframe(camPoints[0].transform.position, camPoints[0].transform.rotation, camPoints[0].fieldOfView));
            }
            return keyframes;
        }

        int segmentsCount = camPoints.Count - 1;
        int[] screenshotsPerSegment = DistributeScreenshotsAcrossSegments(_totalScreenshots, segmentsCount);

        for (int segmentIndex = 0; segmentIndex < segmentsCount; segmentIndex++)
        {
            Camera startCamera = camPoints[segmentIndex];
            Camera endCamera = camPoints[segmentIndex + 1];

            if (startCamera == null || endCamera == null) continue;

            int segmentScreenshots = screenshotsPerSegment[segmentIndex];
            for (int i = 0; i < segmentScreenshots; i++)
            {
                float segmentT = (segmentScreenshots > 1 && i > 0) ? (float)i / (segmentScreenshots - 1) : 0f;
                if (segmentScreenshots == 1) segmentT = 0f;

                Vector3 position = Vector3.Lerp(startCamera.transform.position, endCamera.transform.position, segmentT);
                Quaternion rotation = Quaternion.Slerp(startCamera.transform.rotation, endCamera.transform.rotation, segmentT);
                float fov = Mathf.Lerp(startCamera.fieldOfView, endCamera.fieldOfView, segmentT);
                keyframes.Add(new CameraKeyframe(position, rotation, fov));
            }
        }
        if (_totalScreenshots == 1 && camPoints.Count > 0 && keyframes.Count == 0 && camPoints[0] != null)
        {
             keyframes.Add(new CameraKeyframe(camPoints[0].transform.position, camPoints[0].transform.rotation, camPoints[0].fieldOfView));
        }
        return keyframes;
    }

    IEnumerator CapturePhaseWithKeyframes(List<CameraKeyframe> keyframes, string folderName)
    {
        string fullPath = Path.Combine(savePath, folderName);
        if (imageProcessor != null) imageProcessor.EnsureDirectoryExists(fullPath);
        else if (!Directory.Exists(fullPath)) Directory.CreateDirectory(fullPath);

        int startIndex = GetNextAvailableIndex(fullPath);

        for (int i = 0; i < keyframes.Count; i++)
        {
            if (activeCamera == null) yield break;

            activeCamera.transform.position = keyframes[i].Position;
            activeCamera.transform.rotation = keyframes[i].Rotation;
            activeCamera.fieldOfView = keyframes[i].FieldOfView;

            if (postProcessingRandomizer != null)
            {
                postProcessingRandomizer.RandomizeAndApplyEffectsForNextCapture();
                yield return null;
            }

            string filename = Path.Combine(fullPath, fileNamePrefix + (startIndex + i) + ".png");

            List<Renderer> sortedRenderersForFrame = null;
            if (_labelGenerator != null && folderName == "ShadowsOnly/" && _sceneManagerRef != null)
            {
                sortedRenderersForFrame = MeshAndSplatRenderOrderUtility.GetSortedMeshRenderersForLabeling(
                    _sceneManagerRef.GetRenderableObjects(),
                    activeCamera
                );
            }

            CaptureScreenshot(filename, sortedRenderersForFrame);
        }
    }

    private int GetNextAvailableIndex(string folderPath)
    {
        if (!Directory.Exists(folderPath)) return 0;
        string[] files = Directory.GetFiles(folderPath, fileNamePrefix + "*.png");
        int highestIndex = -1;
        foreach (string file in files)
        {
            string fileNameOnly = Path.GetFileNameWithoutExtension(file);
            if (fileNameOnly.StartsWith(fileNamePrefix))
            {
                string indexStr = fileNameOnly.Substring(fileNamePrefix.Length);
                if (int.TryParse(indexStr, out int index))
                {
                    if (index > highestIndex) highestIndex = index;
                }
            }
        }
        return highestIndex + 1;
    }

    private int[] DistributeScreenshotsAcrossSegments(int totalShots, int segments)
    {
        if (segments <= 0) {
             if (segments == 0 && totalShots > 0) return new int[] { totalShots };
             return new int[0];
        }

        int[] distribution = new int[segments];
        int baseScreenshotsPerSegment = totalShots / segments;
        int remainingScreenshots = totalShots % segments;
        for (int i = 0; i < segments; i++)
        {
            distribution[i] = baseScreenshotsPerSegment;
            if (remainingScreenshots > 0)
            {
                distribution[i]++;
                remainingScreenshots--;
            }
        }
        return distribution;
    }

    bool CaptureScreenshot(string filename, List<Renderer> sortedRenderersForFrame)
    {
        if (activeCamera == null) return false;

        RenderTexture rt = new RenderTexture(_captureWidth, _captureHeight, 24, RenderTextureFormat.ARGB32); 
        activeCamera.targetTexture = rt;
        Texture2D screenshot = new Texture2D(_captureWidth, _captureHeight, TextureFormat.ARGB32, false); 

        activeCamera.Render();
        RenderTexture.active = rt;
        screenshot.ReadPixels(new Rect(0, 0, _captureWidth, _captureHeight), 0, 0);
        screenshot.Apply();

        activeCamera.targetTexture = null;
        RenderTexture.active = null; 
        Destroy(rt);

        byte[] bytes = screenshot.EncodeToPNG(); 
        File.WriteAllBytes(filename, bytes);
        Destroy(screenshot);

        if (_labelGenerator != null && sortedRenderersForFrame != null)
        {
            _labelGenerator.GenerateLabelsForFrame(activeCamera, filename, sortedRenderersForFrame);
        }
        return true;
    }

    void SetLayer(string objectName, string layerName)
    {
        GameObject obj = GameObject.Find(objectName);
        if (obj) obj.layer = LayerMask.NameToLayer(layerName);
        else Debug.LogWarning($"SetLayer: Object '{objectName}' not found.");
    }
}