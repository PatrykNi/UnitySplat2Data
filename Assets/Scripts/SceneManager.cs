using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.IO;

#if UNITY_EDITOR
using UnityEditor;
#endif

public class FinishWindow : EditorWindow
{
    public static void ShowWindow()
{
    FinishWindow window = GetWindow<FinishWindow>("Finished"); 
    Vector2 fixedSize = new Vector2(320, 160);
    window.minSize = fixedSize;
    window.maxSize = fixedSize;
}
    private void OnGUI()
    {
        GUILayout.Label("All capture cycles finished.", EditorStyles.boldLabel);
        GUILayout.Space(20);
        GUILayout.Label("If you want to generate YOLO dataset clisck Tools/Generator/Create YOLO Dataset");
        GUILayout.Space(20);
        if (GUILayout.Button("Close"))
        {
            this.Close();
        }
    }
}

public class SceneManager : MonoBehaviour
{
    [Header("Project Settings")]
    [Tooltip("Project name. Will be used as the main folder for saving images.")]
    public string projectName = "MyUnityProject";

    [Header("Renderable Objects Configuration")]
    [Tooltip("List of Mesh and Splat objects along with their classes. Requires RenderableObject.cs.")]
    public List<RenderableObject> renderableObjects = new List<RenderableObject>();

    [Header("Random Object Placement Physics Settings")]
    [Tooltip("Object defining the placement area. Should have a BoxCollider component.")]
    public GameObject placementAreaObject;
    public float settleTime = 2.0f;
    public bool makeKinematicAfterSettling = true;
    public bool freezeRotationX = true;
    public bool freezeRotationY = true;
    public bool freezeRotationZ = true;

    [Header("Camera Path Settings")]
    public List<Camera> cameraPoints = new List<Camera>();
    public int totalScreenshots = 10;
    [Tooltip("Width of captured images and masks.")]
    public int captureWidth = 1920;
    [Tooltip("Height of captured images and masks.")]
    public int captureHeight = 1080;

    [Header("Light Control Settings")]
    public GameObject lightParentToControl;
    public List<Transform> lightWaypoints = new List<Transform>();
    public float lightMovementSpeed = 5.0f;
    public float lightRotationSpeed = 100.0f;
    public bool activateLightControllerOnStart = true;

    [Header("Multiple Capture Cycles")]
    [Tooltip("Number of cycles for image capture and random object placement.")]
    public int numberOfCaptureCycles = 1;
    private bool isRunningMultipleCycles = false;

    [Header("External Script References")]
    public RandomObjectPlacer randomObjectPlacer;
    public CameraMovementAndCapture cameraCapture;
    public SplatTransformer splatTransformer;
    public MeshAndSplatRenderOrderUtility renderOrderUtility;
    public LabelGenerator labelGenerator;
    public LightController lightController;
    public PostProcessingRandomizer postProcessingRandomizer;

    void Start()
    {
        if (randomObjectPlacer == null) randomObjectPlacer = FindObjectOfType<RandomObjectPlacer>();
        if (cameraCapture == null) cameraCapture = FindObjectOfType<CameraMovementAndCapture>();
        if (splatTransformer == null) splatTransformer = FindObjectOfType<SplatTransformer>();
        if (renderOrderUtility == null) renderOrderUtility = FindObjectOfType<MeshAndSplatRenderOrderUtility>();
        if (labelGenerator == null)
        {
            labelGenerator = FindObjectOfType<LabelGenerator>();
            if (labelGenerator == null)
            {
                Debug.LogWarning("SceneManager: LabelGenerator not found in scene. Adding it to this GameObject.");
                labelGenerator = gameObject.AddComponent<LabelGenerator>();
            }
        }
        if (lightController == null)
        {
            lightController = FindObjectOfType<LightController>();
            if (lightController == null && activateLightControllerOnStart)
            {
                Debug.LogWarning("SceneManager: LightController not found in scene but 'activateLightControllerOnStart' is true. Light control will not function.");
            }
        }
        if (postProcessingRandomizer == null)
        {
            postProcessingRandomizer = FindObjectOfType<PostProcessingRandomizer>();
            if (postProcessingRandomizer == null)
            {
                Debug.LogWarning("SceneManager: PostProcessingRandomizer not found in scene. Adding it to this GameObject.");
                postProcessingRandomizer = gameObject.AddComponent<PostProcessingRandomizer>();
            }
        }

        if (randomObjectPlacer == null) Debug.LogError("SceneManager: RandomObjectPlacer not found! Object placement will not work.", this);
        if (cameraCapture == null) Debug.LogError("SceneManager: CameraMovementAndCapture not found! Image capture will not work.", this);
        if (splatTransformer == null) Debug.LogError("SceneManager: SplatTransformer not found! Splat transforms will not be updated.", this);

        InitializeExternalScripts();
    }

    private void InitializeExternalScripts()
    {
        if (splatTransformer != null && renderableObjects != null)
        {
            List<MeshSplatPair> meshSplatPairs = renderableObjects
                .Where(ro => ro != null && ro.meshObject != null && ro.splatObject != null)
                .Select(ro => new MeshSplatPair { meshObject = ro.meshObject, splatObject = ro.splatObject })
                .ToList();
            splatTransformer.SetMeshSplatPairs(meshSplatPairs);
        }

        if (renderOrderUtility != null && renderableObjects != null)
        {
            List<MeshAndSplatRenderOrderUtility.RenderablePair> renderablePairs = renderableObjects
                .Where(ro => ro != null &&
                             ((ro.meshObject != null && ro.meshObject.GetComponent<MeshRenderer>() != null) ||
                              (ro.splatObject != null && ro.splatObject.GetComponent<GaussianSplatting.Runtime.GaussianSplatRenderer>() != null)))
                .Select(ro => new MeshAndSplatRenderOrderUtility.RenderablePair
                {
                    meshRenderer = ro.meshObject?.GetComponent<MeshRenderer>(),
                    splatRenderer = ro.splatObject?.GetComponent<GaussianSplatting.Runtime.GaussianSplatRenderer>()
                })
                .ToList();
            renderOrderUtility.SetRenderablePairs(renderablePairs);

            if (cameraPoints != null && cameraPoints.Count > 0 && cameraPoints[0] != null)
            {
                renderOrderUtility.SetCameraToUse(cameraPoints[0]);
            }
            else
            {
                Debug.LogWarning("SceneManager: CameraPoints list is empty or first camera is null for RenderOrderUtility.", this);
            }
        }

        if (randomObjectPlacer != null)
        {
            if (placementAreaObject == null)
            {
                Debug.LogError("SceneManager: 'Placement Area Object' is not assigned in SceneManager! RandomObjectPlacer cannot be initialized correctly.", this);
            }
            randomObjectPlacer.SetPlacementSettings(
                placementAreaObject,
                settleTime,
                makeKinematicAfterSettling,
                freezeRotationX,
                freezeRotationY,
                freezeRotationZ
            );

            if (renderableObjects != null)
            {
                List<ObjectPlacementSettings> placementSettings = renderableObjects
                    .Where(ro => ro != null && ro.meshObject != null)
                    .Select(ro => new ObjectPlacementSettings
                    {
                        objectToPlace = ro.meshObject,
                        randomizeX = ro.randomizeX,
                        randomizeY = ro.randomizeY,
                        randomizeZ = ro.randomizeZ
                    })
                    .ToList();
                randomObjectPlacer.SetObjectPlacementSettings(placementSettings);
            }
        }

        if (labelGenerator != null && renderableObjects != null)
        {
            var trackableObjects = renderableObjects
                .Where(ro => ro != null && ro.meshObject != null && !string.IsNullOrEmpty(ro.objectClass) && ro.meshObject.GetComponent<Renderer>() != null)
                .ToList();
            labelGenerator.Initialize(trackableObjects, captureWidth, captureHeight);
        }

        if (cameraCapture != null)
        {
            cameraCapture.SetProjectName(projectName);
            cameraCapture.SetCameraPoints(cameraPoints);
            cameraCapture.SetTotalScreenshots(totalScreenshots);
            cameraCapture.SetLabelGenerator(labelGenerator);
            cameraCapture.SetSceneManagerRef(this);
            cameraCapture.SetCaptureDimensions(captureWidth, captureHeight);
            if (postProcessingRandomizer != null)
            {
                cameraCapture.SetPostProcessingRandomizer(postProcessingRandomizer);
            }
        }

        if (lightController != null)
        {
            if (lightParentToControl != null && lightWaypoints != null && lightWaypoints.Count > 0)
            {
                lightController.movementSpeed = this.lightMovementSpeed;
                lightController.rotationSpeed = this.lightRotationSpeed;
                lightController.InitializeController(lightParentToControl, lightWaypoints);
                lightController.enabled = activateLightControllerOnStart;
                if (activateLightControllerOnStart)
                {
                    lightController.ResumeMovement();
                }
                else
                {
                    lightController.StopMovement();
                }
            }
            else if (activateLightControllerOnStart)
            {
                Debug.LogWarning("SceneManager: LightController is assigned and 'activateLightControllerOnStart' is true, but 'Light Parent To Control' or 'Light Waypoints' are not set up correctly. LightController will not be initialized.", this);
                lightController.enabled = false;
            }
            else
            {
                lightController.enabled = false;
            }
        }
    }

    [ContextMenu("1. Randomly Place Objects (Single Run)")]
    public void PlaceObjects()
    {
        if (randomObjectPlacer != null)
        {
            if (placementAreaObject == null)
            {
                Debug.LogError("Placement Area Object is not assigned in SceneManager! Cannot place objects.", this);
                return;
            }
            if (placementAreaObject.GetComponent<BoxCollider>() == null)
            {
                Debug.LogError($"SceneManager: 'Placement Area Object' ({placementAreaObject.name}) does not have a BoxCollider component. Object placement will likely fail or be incorrect. Add a BoxCollider.", placementAreaObject);
            }
            Debug.Log("SceneManager: Calling RandomObjectPlacer to place objects (Single Run).", this);
            StartCoroutine(randomObjectPlacer.PlaceObjectsRandomlyCoroutine());
        }
        else Debug.LogError("RandomObjectPlacer is not assigned in SceneManager!", this);
    }

    [ContextMenu("2. Capture Images (Single Run)")]
    public void CaptureImages()
    {
        if (cameraCapture != null)
        {
            if (cameraPoints == null || cameraPoints.Count == 0 || cameraPoints.Any(c => c == null))
            {
                Debug.LogError("SceneManager: 'Camera Path Settings' are not configured correctly. Ensure the list has at least one camera and no null entries.", this);
                return;
            }
            Debug.Log("SceneManager: Calling CameraMovementAndCapture to capture images (Single Run).", this);
            StartCoroutine(cameraCapture.MoveAndCapture());
        }
        else Debug.LogError("CameraMovementAndCapture is not assigned in SceneManager!", this);
    }

    [ContextMenu("3. Transfer Splat Transforms")]
    public void TransferSplatTransforms()
    {
        if (splatTransformer != null)
        {
            Debug.Log("SceneManager: Calling SplatTransformer to transfer transforms.", this);
            splatTransformer.TransferTransformsToSplats();
        }
        else Debug.LogError("SplatTransformer is not assigned in SceneManager!", this);
    }

    [ContextMenu("4. Calculate Render Order")]
    public void CalculateRenderOrder()
    {
        if (renderOrderUtility != null)
        {
            if (renderOrderUtility.isActiveAndEnabled)
            {
                Debug.Log("SceneManager: Calling MeshAndSplatRenderOrderUtility to calculate render order.", this);
                renderOrderUtility.CalculateRenderOrderFromInspector();
            }
            else
            {
                Debug.LogWarning("SceneManager: MeshAndSplatRenderOrderUtility is assigned but not active. Cannot calculate render order.", renderOrderUtility);
            }
        }
        else Debug.LogError("MeshAndSplatRenderOrderUtility is not assigned in SceneManager!", this);
    }

    [ContextMenu("5. Start/Resume Light Movement")]
    public void StartLightMovement()
    {
        if (lightController != null)
        {
            if (lightController.lightParent == null || lightController.waypoints == null || lightController.waypoints.Count == 0)
            {
                Debug.LogWarning("SceneManager: Attempting to start LightController, but it's not properly configured (missing lightParent or waypoints). Attempting to re-initialize.", this);
                InitializeLightControllerRuntime();
            }

            if (lightController.lightParent != null && lightController.waypoints != null && lightController.waypoints.Count > 0)
            {
                lightController.enabled = true;
                lightController.ResumeMovement();
                Debug.Log("SceneManager: Light movement started/resumed.", this);
            }
            else
            {
                Debug.LogError("SceneManager: Cannot start LightController - still not properly configured after re-initialization attempt.", this);
            }
        }
        else Debug.LogError("LightController is not assigned in SceneManager!", this);
    }

    [ContextMenu("5. Stop Light Movement")]
    public void StopLightMovement()
    {
        if (lightController != null)
        {
            lightController.StopMovement();
            Debug.Log("SceneManager: Light movement stopped.", this);
        }
        else Debug.LogError("LightController is not assigned in SceneManager!", this);
    }

    [ContextMenu("6. Start Multiple Capture Cycles")]
    public void TriggerMultipleCaptureCycles()
    {
        if (isRunningMultipleCycles)
        {
            Debug.LogWarning("Multiple capture cycles are already running. Please wait for them to complete or stop them manually if needed.");
            return;
        }
        if (numberOfCaptureCycles <= 0)
        {
            Debug.LogWarning("Number of capture cycles is set to 0 or less. Nothing to do.");
            return;
        }
        StartCoroutine(MultipleCaptureCyclesCoroutine());
    }

    private IEnumerator MultipleCaptureCyclesCoroutine()
    {
        isRunningMultipleCycles = true;
        Debug.Log($"Starting multiple capture cycles. Total cycles: {numberOfCaptureCycles}");

        for (int i = 0; i < numberOfCaptureCycles; i++)
        {
            Debug.Log($"--- Starting Capture Cycle {i + 1} of {numberOfCaptureCycles} ---");

            // 1. Capture Images
            if (cameraCapture != null)
            {
                if (cameraPoints == null || cameraPoints.Count == 0 || cameraPoints.Any(c => c == null))
                {
                    Debug.LogError($"Cycle {i + 1}: 'Camera Path Settings' are not configured correctly. Skipping image capture for this cycle.", this);
                }
                else
                {
                    Debug.Log($"Cycle {i + 1}: Starting image capture.");
                    yield return StartCoroutine(cameraCapture.MoveAndCapture());
                    Debug.Log($"Cycle {i + 1}: Image capture finished.");
                }
            }
            else
            {
                Debug.LogError($"Cycle {i + 1}: CameraMovementAndCapture is not assigned in SceneManager! Skipping image capture.");
            }

            // 2. Randomly Place Objects
            if (randomObjectPlacer != null)
            {
                if (placementAreaObject == null || placementAreaObject.GetComponent<BoxCollider>() == null)
                {
                    Debug.LogError($"Cycle {i + 1}: Placement Area Object is not correctly configured (missing or no BoxCollider). Skipping object placement for this cycle.", this);
                }
                else
                {
                    Debug.Log($"Cycle {i + 1}: Starting random object placement.");
                    yield return StartCoroutine(randomObjectPlacer.PlaceObjectsRandomlyCoroutine());
                    Debug.Log($"Cycle {i + 1}: Random object placement finished.");
                }
            }
            else
            {
                Debug.LogError($"Cycle {i + 1}: RandomObjectPlacer is not assigned in SceneManager! Skipping object placement.");
            }

            Debug.Log($"--- Finished Capture Cycle {i + 1} of {numberOfCaptureCycles} ---");
        }

        Debug.Log("All capture cycles finished.");
        FinishWindow.ShowWindow();
        isRunningMultipleCycles = false;
    }


    private void InitializeLightControllerRuntime()
    {
        if (lightController != null)
        {
            if (lightParentToControl != null && lightWaypoints != null && lightWaypoints.Count > 0)
            {
                lightController.movementSpeed = this.lightMovementSpeed;
                lightController.rotationSpeed = this.lightRotationSpeed;
                lightController.InitializeController(lightParentToControl, lightWaypoints);
            }
            else
            {
                Debug.LogWarning("SceneManager (Runtime Init): LightController is assigned, but 'Light Parent To Control' or 'Light Waypoints' are not set up correctly. LightController will not be initialized.", this);
                if (lightController.isActiveAndEnabled) lightController.StopMovement();
                lightController.enabled = false;
            }
        }
    }

    public List<RenderableObject> GetRenderableObjects()
    {
        if (this.renderableObjects == null)
        {
            this.renderableObjects = new List<RenderableObject>();
            Debug.LogWarning("SceneManager: renderableObjects list was null, initialized to empty list.", this);
        }
        return this.renderableObjects;
    }

    void OnDrawGizmosSelected()
    {
        if (placementAreaObject != null && placementAreaObject.activeInHierarchy)
        {
            BoxCollider boxCollider = placementAreaObject.GetComponent<BoxCollider>();
            if (boxCollider != null)
            {
                Gizmos.color = new Color(0, 1, 0, 0.5f);
                Matrix4x4 originalMatrix = Gizmos.matrix;
                Gizmos.matrix = Matrix4x4.TRS(
                    boxCollider.transform.TransformPoint(boxCollider.center),
                    boxCollider.transform.rotation,
                    boxCollider.transform.lossyScale
                );
                Gizmos.DrawWireCube(Vector3.zero, boxCollider.size);
                Gizmos.matrix = originalMatrix;
            }
            else
            {
                MeshRenderer areaRenderer = placementAreaObject.GetComponent<MeshRenderer>();
                Collider areaCollider = placementAreaObject.GetComponent<Collider>();
                if (areaRenderer != null || areaCollider != null)
                {
                    Bounds gizmoBounds = (areaRenderer != null) ? areaRenderer.bounds : areaCollider.bounds;
                    Gizmos.color = new Color(1, 0.5f, 0, 0.3f);
                    Gizmos.matrix = Matrix4x4.identity;
                    Gizmos.DrawCube(gizmoBounds.center, gizmoBounds.size);
                    Debug.LogWarning($"SceneManager Gizmo: 'placementAreaObject' ({placementAreaObject.name}) is missing a BoxCollider component. Drawing AABB (which may be inaccurate for rotated objects). Adding a BoxCollider is recommended for precise area visualization.", placementAreaObject);
                }
                else
                {
                    Gizmos.color = Color.red;
                    Gizmos.DrawSphere(placementAreaObject.transform.position, 0.5f);
                    Debug.LogError($"SceneManager Gizmo: 'placementAreaObject' ({placementAreaObject.name}) has no BoxCollider, MeshRenderer, or any other Collider. Unable to visualize the area.", placementAreaObject);
                }
            }
        }
    }

    // =======================================================================
    // NOWY KOD: Metoda pomocnicza do pobierania nazw klas dla generatora YOLO
    // =======================================================================
    public List<string> GetClassNames()
    {
        List<string> names = new List<string>();
        if (renderableObjects != null)
        {
            foreach (var obj in renderableObjects)
            {
                if (obj != null && !string.IsNullOrEmpty(obj.objectClass))
                {
                    names.Add(obj.objectClass);
                }
                else
                {
                    names.Add("Unnamed");
                }
            }
        }
        return names;
    }
}

// =========================================================================================
// TOP MENU ITEMS - This creates the "Scene Manager" menu in the top Unity bar.
// =========================================================================================
#if UNITY_EDITOR
public class SceneManagerMenu
{
    // Helper function to find the SceneManager in the scene
    private static SceneManager GetSceneManager()
    {
        SceneManager manager = Object.FindObjectOfType<SceneManager>();
        if (manager == null)
        {
            Debug.LogError("Error: Could not find a 'SceneManager' object in the current scene. Please ensure one exists.");
        }
        return manager;
    }

    // -------------------------------------------------------------------------------------
    // VALIDATORS - These verify if the game is playing.
    // If Application.isPlaying is false, the menu item will be grayed out.
    // The path in MenuItem MUST be identical to the action method for the validator to work.
    // -------------------------------------------------------------------------------------

    [MenuItem("Tools/Generator/Other/Randomly Place Objects (Single Run)", true)]
    static bool ValidatePlaceObjects() { return Application.isPlaying; }

    [MenuItem("Tools/Generator/Other/Transfer Splat Transforms", true)]
    static bool ValidateTransferSplats() { return true; }

    [MenuItem("Tools/Generator/Other/Calculate Render Order", true)]
    static bool ValidateRenderOrder() { return Application.isPlaying; }

    [MenuItem("Tools/Generator/Other/Start or Resume LightController", true)]
    static bool ValidateStartLight() { return Application.isPlaying; }

    [MenuItem("Tools/Generator/Other/Stop LightController", true)]
    static bool ValidateStopLight() { return Application.isPlaying; }
    /*
    [MenuItem("Tools/Generator/Capture Images (Single Run)", true)]
    static bool ValidateCaptureImages() { return Application.isPlaying; }
    /*
    [MenuItem("Tools/Generator/Start Multiple Capture Cycles", true)]
    static bool ValidateMultipleCycles() { return Application.isPlaying; }
    */

    // -------------------------------------------------------------------------------------
    // MENU ACTIONS
    // -------------------------------------------------------------------------------------

    [MenuItem("Tools/Generator/Other/Randomly Place Objects (Single Run)")]
    public static void MenuPlaceObjects()
    {
        SceneManager manager = GetSceneManager();
        if (manager != null) manager.PlaceObjects();
    }

    [MenuItem("Tools/Generator/Other/Transfer Splat Transforms")]
    public static void MenuTransferSplats()
    {
        SceneManager manager = GetSceneManager();
        if (manager != null) manager.TransferSplatTransforms();
    }

    [MenuItem("Tools/Generator/Other/Calculate Render Order")]
    public static void MenuCalculateRenderOrder()
    {
        SceneManager manager = GetSceneManager();
        if (manager != null) manager.CalculateRenderOrder();
    }

    [MenuItem("Tools/Generator/Other/Start or Resume LightController")]
    public static void MenuStartLight()
    {
        SceneManager manager = GetSceneManager();
        if (manager != null) manager.StartLightMovement();
    }

    [MenuItem("Tools/Generator/Other/Stop LightController")]
    public static void MenuStopLight()
    {
        SceneManager manager = GetSceneManager();
        if (manager != null) manager.StopLightMovement();
    }
    /*
    [MenuItem("Tools/Generator/Capture Images (Single Run)")]
    public static void MenuCaptureImages()
    {
        SceneManager manager = GetSceneManager();
        if (manager != null) manager.CaptureImages();
    }
    */
    /*
    [MenuItem("Tools/Generator/Start Multiple Capture Cycles")]
    public static void MenuMultipleCycles()
    {
        SceneManager manager = GetSceneManager();
        if (manager != null) manager.TriggerMultipleCaptureCycles();
    }
    */
}
#endif