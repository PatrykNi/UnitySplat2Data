using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

#if UNITY_EDITOR
public class DatasetGeneratorWindow : EditorWindow
{
    // Settings
    private SceneManager sceneManager;
    private float splitRatio = 70.0f; // Training percentage
    private bool shuffleData = true; 
    private bool isSegmentation = false; // Detection (false) vs Segmentation (true)
    private string outputFolderName = "YoloDataset_v1";

    // Project Selection
    private string[] availableProjects = new string[0];
    private int selectedProjectIndex = 0;

    // Paths
    private string projectRootPath;
    private string generatedDataPath;

    [MenuItem("Tools/Generator/Create YOLO Dataset")]
    public static void ShowWindow()
    {
        DatasetGeneratorWindow window = GetWindow<DatasetGeneratorWindow>("Dataset Creator");
        window.minSize = new Vector2(350, 400);
    }

    private void OnEnable()
    {
        // Try to automatically find SceneManager
        sceneManager = FindObjectOfType<SceneManager>();
        
        // Setup paths
        projectRootPath = Directory.GetParent(Application.dataPath).FullName;
        generatedDataPath = Path.Combine(projectRootPath, "GeneratedData");

        // Scan for existing project folders
        RefreshProjectList();
    }

    private void RefreshProjectList()
    {
        if (Directory.Exists(generatedDataPath))
        {
            // Get all subdirectories in GeneratedData
            availableProjects = Directory.GetDirectories(generatedDataPath)
                                         .Select(d => Path.GetFileName(d)) // Get only folder name
                                         .ToArray();
        }
        else
        {
            availableProjects = new string[0];
        }

        // Try to select the one matching SceneManager if possible
        if (sceneManager != null && availableProjects.Length > 0)
        {
            int index = System.Array.IndexOf(availableProjects, sceneManager.projectName);
            if (index >= 0) selectedProjectIndex = index;
            else selectedProjectIndex = 0;
        }
    }

    private void OnGUI()
    {
        GUILayout.Label("YOLO Dataset Configuration", EditorStyles.boldLabel);
        EditorGUILayout.Space();

        // 1. SceneManager Reference
        sceneManager = (SceneManager)EditorGUILayout.ObjectField("Scene Manager", sceneManager, typeof(SceneManager), true);
        if (sceneManager == null)
        {
            EditorGUILayout.HelpBox("Please assign SceneManager to retrieve class names for the YAML file.", MessageType.Warning);
        }

        EditorGUILayout.Space();

        // 2. Source Project Selection (New Feature)
        GUILayout.Label("Source Configuration", EditorStyles.boldLabel);
        GUILayout.BeginHorizontal();
        if (availableProjects.Length > 0)
        {
            selectedProjectIndex = EditorGUILayout.Popup("Source Project Folder", selectedProjectIndex, availableProjects);
        }
        else
        {
            EditorGUILayout.LabelField("Source Project Folder", "No folders found in GeneratedData!");
        }

        if (GUILayout.Button("Refresh", GUILayout.Width(60)))
        {
            RefreshProjectList();
        }
        GUILayout.EndHorizontal();

        EditorGUILayout.Space();

        // 3. Data Type Selection
        GUILayout.Label("Dataset Settings", EditorStyles.boldLabel);
        string[] options = new string[] { "Detection (Bounding Box)", "Segmentation (Polygons)" };
        int selectedIndex = isSegmentation ? 1 : 0;
        selectedIndex = EditorGUILayout.Popup("Label Type", selectedIndex, options);
        isSegmentation = (selectedIndex == 1);

        // 4. Train / Val Split
        GUILayout.Label($"Train / Val Split: {splitRatio:F0}% / {100 - splitRatio:F0}%", EditorStyles.label);
        splitRatio = EditorGUILayout.Slider(splitRatio, 10.0f, 90.0f);

        // 5. Shuffle
        shuffleData = EditorGUILayout.Toggle("Shuffle Images", shuffleData);
        if (!shuffleData)
        {
            EditorGUILayout.HelpBox("Without shuffling: The first X% of images (sorted numerically) will go to Train, the rest to Val.", MessageType.Info);
        }

        EditorGUILayout.Space();

        // 6. Output Folder Name
        outputFolderName = EditorGUILayout.TextField("Output Dataset Name", outputFolderName);
        
        EditorGUILayout.Space();
        EditorGUILayout.Space();

        // Validation before button
        bool canGenerate = sceneManager != null && availableProjects.Length > 0;
        GUI.enabled = canGenerate;

        if (GUILayout.Button("GENERATE DATASET", GUILayout.Height(40)))
        {
            GenerateDataset();
        }
        GUI.enabled = true;
    }

    private void GenerateDataset()
    {
        if (sceneManager == null)
        {
            Debug.LogError("DatasetGenerator: SceneManager not assigned!");
            return;
        }

        if (availableProjects.Length == 0)
        {
            Debug.LogError("DatasetGenerator: No source projects found.");
            return;
        }

        // 1. Get Selected Project Name
        string selectedProjectName = availableProjects[selectedProjectIndex];
        
        // Paths based on SELECTION, not SceneManager name
        string sourceImagesDir = Path.Combine(generatedDataPath, selectedProjectName, "FinishedPhotos");
        
        string labelFolder = isSegmentation ? "Labels_Segmentation" : "Labels_Detection";
        string sourceLabelsDir = Path.Combine(generatedDataPath, selectedProjectName, labelFolder);

        if (!Directory.Exists(sourceImagesDir) || !Directory.Exists(sourceLabelsDir))
        {
            Debug.LogError($"DatasetGenerator: Source folders not found for project '{selectedProjectName}':\n{sourceImagesDir}\n{sourceLabelsDir}");
            return;
        }

        // 2. Pair Files (Image + Label)
        var images = Directory.GetFiles(sourceImagesDir, "*.png");
        List<DataPair> dataPairs = new List<DataPair>();

        foreach (var imgPath in images)
        {
            string fileName = Path.GetFileNameWithoutExtension(imgPath);
            string txtPath = Path.Combine(sourceLabelsDir, fileName + ".txt");

            if (File.Exists(txtPath))
            {
                dataPairs.Add(new DataPair { ImagePath = imgPath, LabelPath = txtPath });
            }
            // else: silently skip or log warning
        }

        if (dataPairs.Count == 0)
        {
            Debug.LogError("DatasetGenerator: No valid file pairs (png + txt) found!");
            return;
        }

        // 3. Sort or Shuffle
        if (shuffleData)
        {
            System.Random rng = new System.Random();
            dataPairs = dataPairs.OrderBy(a => rng.Next()).ToList();
        }
        else
        {
            // Natural Sort
            dataPairs = dataPairs.OrderBy(p => {
                string name = Path.GetFileNameWithoutExtension(p.ImagePath);
                var match = Regex.Match(name, @"\d+");
                return match.Success ? int.Parse(match.Value) : 0;
            }).ToList();
        }

        int trainCount = Mathf.RoundToInt(dataPairs.Count * (splitRatio / 100.0f));
        var trainSet = dataPairs.Take(trainCount).ToList();
        var valSet = dataPairs.Skip(trainCount).ToList();

        // 4. Prepare Output Path
        string outputRoot = Path.Combine(projectRootPath, "FinalDatasets", outputFolderName);
        
        if (Directory.Exists(outputRoot))
        {
            bool delete = EditorUtility.DisplayDialog("Folder Exists", 
                $"Folder '{outputFolderName}' already exists. Do you want to overwrite it?", "Yes, Overwrite", "Cancel");
            
            if (!delete) return;
            Directory.Delete(outputRoot, true);
        }

        // YOLO Directory Structure
        string trainImagesDir = Path.Combine(outputRoot, "images", "train");
        string valImagesDir = Path.Combine(outputRoot, "images", "val");
        string trainLabelsDir = Path.Combine(outputRoot, "labels", "train");
        string valLabelsDir = Path.Combine(outputRoot, "labels", "val");

        Directory.CreateDirectory(trainImagesDir);
        Directory.CreateDirectory(valImagesDir);
        Directory.CreateDirectory(trainLabelsDir);
        Directory.CreateDirectory(valLabelsDir);

        // 5. Copy Files
        try 
        {
            EditorUtility.DisplayProgressBar("Generating Dataset", "Copying Training Set...", 0.2f);
            CopyFiles(trainSet, trainImagesDir, trainLabelsDir);

            EditorUtility.DisplayProgressBar("Generating Dataset", "Copying Validation Set...", 0.6f);
            CopyFiles(valSet, valImagesDir, valLabelsDir);

            // 6. Create YAML
            EditorUtility.DisplayProgressBar("Generating Dataset", "Creating YAML file...", 0.9f);
            CreateYamlFile(outputRoot);

            Debug.Log($"<color=green>SUCCESS! Dataset created at: {outputRoot}</color>");
            Debug.Log($"Source: {selectedProjectName} | Train: {trainSet.Count} | Val: {valSet.Count}");
            
            EditorUtility.RevealInFinder(outputRoot);
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Error during generation: {e.Message}");
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }
    }

    private void CopyFiles(List<DataPair> pairs, string destImgDir, string destLblDir)
    {
        foreach (var pair in pairs)
        {
            string fName = Path.GetFileName(pair.ImagePath);
            string txtName = Path.GetFileName(pair.LabelPath);

            File.Copy(pair.ImagePath, Path.Combine(destImgDir, fName));
            File.Copy(pair.LabelPath, Path.Combine(destLblDir, txtName));
        }
    }

    private void CreateYamlFile(string rootPath)
    {
        List<string> classNames = sceneManager.GetClassNames();
        string yamlPath = Path.Combine(rootPath, "data.yaml");

        StringBuilder sb = new StringBuilder();
        
        sb.AppendLine("train: images/train");
        sb.AppendLine("val: images/val");
        
        sb.AppendLine("");
        sb.AppendLine($"nc: {classNames.Count}");
        
        sb.AppendLine("names:");
        for (int i = 0; i < classNames.Count; i++)
        {
            sb.AppendLine($"  {i}: {classNames[i]}");
        }

        File.WriteAllText(yamlPath, sb.ToString());
    }

    private struct DataPair
    {
        public string ImagePath;
        public string LabelPath;
    }
}
#endif