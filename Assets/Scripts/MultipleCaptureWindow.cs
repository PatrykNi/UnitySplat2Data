using UnityEngine;
using UnityEditor;

#if UNITY_EDITOR
public class MultipleCaptureWindow : EditorWindow
{
    // UI Variables
    private string projectName = "MyProject";
    private int photosPerCycle = 10;
    private int numberOfCycles = 1;

    // Reference
    private SceneManager sceneManager;

    [MenuItem("Tools/Generator/Start Multiple Capture Cycles")]
    public static void ShowWindow()
    {
        GetWindow<MultipleCaptureWindow>("Capture Cycles");
    }

    private void OnEnable()
    {
        // Find SceneManager automatically when window opens
        sceneManager = FindObjectOfType<SceneManager>();
        
        // If found, pre-fill the window with current settings from the scene object
        if (sceneManager != null)
        {
            projectName = sceneManager.projectName;
            photosPerCycle = sceneManager.totalScreenshots;
            numberOfCycles = sceneManager.numberOfCaptureCycles;
        }
    }

    private void OnGUI()
    {
        GUILayout.Label("Multiple Capture Configuration", EditorStyles.boldLabel);
        EditorGUILayout.Space();

        // 1. Scene Manager Reference
        sceneManager = (SceneManager)EditorGUILayout.ObjectField("Scene Manager", sceneManager, typeof(SceneManager), true);
        
        if (sceneManager == null)
        {
            EditorGUILayout.HelpBox("SceneManager not found! Please assign it to proceed.", MessageType.Error);
            return; // Stop drawing if no manager
        }

        EditorGUILayout.Space();

        // 2. Input Fields
        projectName = EditorGUILayout.TextField("Project Name", projectName);
        photosPerCycle = EditorGUILayout.IntField("Photos Per Cycle", photosPerCycle);
        numberOfCycles = EditorGUILayout.IntField("Number of Cycles", numberOfCycles);

        EditorGUILayout.Space();
        EditorGUILayout.Space();

        // 3. Validation & Start Button
        if (!Application.isPlaying)
        {
            EditorGUILayout.HelpBox("You must be in PLAY MODE to start the capture process.", MessageType.Warning);
            GUI.enabled = false; // Disable button
        }

        if (GUILayout.Button("START CAPTURE CYCLES", GUILayout.Height(40)))
        {
            StartCaptureProcess();
        }

        GUI.enabled = true; // Re-enable GUI just in case
    }

    private void StartCaptureProcess()
    {
        if (sceneManager == null) return;

        // 1. Push values from Window to SceneManager
        sceneManager.projectName = projectName;
        sceneManager.totalScreenshots = photosPerCycle;
        sceneManager.numberOfCaptureCycles = numberOfCycles;
        
        // Also update the camera capture reference inside SceneManager just in case
        if (sceneManager.cameraCapture != null)
        {
            sceneManager.cameraCapture.SetTotalScreenshots(photosPerCycle);
            sceneManager.cameraCapture.SetProjectName(projectName);
        }

        // 2. Mark scene as dirty so Unity saves the new values (optional but good practice)
        EditorUtility.SetDirty(sceneManager);

        // 3. Trigger the logic
        Debug.Log($"[Window] Starting Capture: Project='{projectName}', Photos={photosPerCycle}, Cycles={numberOfCycles}");
        sceneManager.TriggerMultipleCaptureCycles();
        
        // Optional: Close window after start
        Close(); 
    }
}
#endif