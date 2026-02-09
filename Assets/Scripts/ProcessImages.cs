using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

public class ImageProcessor : MonoBehaviour
{
    [Header("Folder Paths")]
    public string folderObjects = "Photos/WithoutShadows";
    public string folderShadows = "Photos/ShadowsOnly";
    public string folderOutput = "Photos/FinishedPhotos";

    [Header("Shadow Parameters")]
    [Range(0.0f, 3.0f)]
    public float shadowMultiplier = 1f;
    [Range(0.0f, 1.0f)]
    public float shadowTransparency = 0.7f;
    [Range(0.0f, 2.0f)]
    public float shadowContrast = 1.0f;

    [Header("Highlight Parameters")]
    [Range(0.0f, 10.0f)]
    public float highlightIntensity = 1.8f;
    [Range(0.0f, 1.0f)]
    public float highlightStrength = 0.1f;
    [Range(0.0f, 2.0f)]
    public float highlightContrast = 1.0f;

    [Header("Blur Settings")]
    [Range(0, 20)]
    public int blurSize = 5;

    [Header("Sigmoid Parameters")]
    public bool useShadowSigmoid = true;
    [Range(0, 255)]
    public int shadowSigmoidCenter = 120;
    [Range(0.001f, 1.0f)]
    public float shadowSigmoidSteepness = 0.09f;

    public bool useHighlightSigmoid = true;
    [Range(0, 255)]
    public int highlightSigmoidCenter = 250;
    [Range(0.001f, 1.0f)]
    public float highlightSigmoidSteepness = 0.66f;

    [Header("Preview")]
    public bool showPreview = false;
    public RawImage previewDisplay;

    private Texture2D previewOriginal;
    private Texture2D previewShadow;
    [HideInInspector] public Texture2D previewResult;

    private readonly string[] imageExtensions = { ".jpg", ".jpeg", ".png", ".bmp", ".gif" };

    public void ProcessImages()
    {
        // Create output directory if needed
        if (!Directory.Exists(folderOutput)) Directory.CreateDirectory(folderOutput);
        
        if (!Directory.Exists(folderObjects))
        {
            Debug.LogError($"Object folder '{folderObjects}' not found!");
            return;
        }

        // Get image files
        string[] files = Directory.GetFiles(folderObjects)
            .Where(file => imageExtensions.Contains(Path.GetExtension(file).ToLowerInvariant()))
            .Select(Path.GetFileName)
            .ToArray();

        int processedCount = 0, skippedCount = 0;

        foreach (string filename in files)
        {
            string pathObj = Path.Combine(folderObjects, filename);
            string pathShadow = Path.Combine(folderShadows, filename);
            string outputPath = Path.Combine(folderOutput, filename);

            // Skip if already processed
            if (File.Exists(outputPath))
            {
                Debug.Log($"Image {filename} already processed. Skipping.");
                skippedCount++;
                continue; 
            }

            if (!File.Exists(pathShadow))
            {
                Debug.LogWarning($"No shadow found for {filename}, skipping...");
                continue;
            }

            // Load images
            Texture2D objectTexture = LoadTexture(pathObj);
            Texture2D shadowTexture = LoadTexture(pathShadow);
            
            if (objectTexture == null || shadowTexture == null)
            {
                if (objectTexture != null) Destroy(objectTexture);
                if (shadowTexture != null) Destroy(shadowTexture);
                continue;
            }

            // Process images
            Texture2D shadowedImage = AddShadows(objectTexture, shadowTexture);
            if (shadowedImage == null)
            {
                Destroy(objectTexture);
                Destroy(shadowTexture);
                continue;
            }

            Texture2D finalImage = AddHighlight(shadowedImage, shadowTexture);
            if (finalImage == null)
            {
                Destroy(objectTexture);
                Destroy(shadowTexture);
                Destroy(shadowedImage);
                continue;
            }

            // Save result
            SaveTexture(finalImage, outputPath);
            processedCount++;

            // Clean up
            Destroy(objectTexture);
            Destroy(shadowTexture);
            Destroy(shadowedImage);
            Destroy(finalImage);
        }
        
        Debug.Log($"Processing completed! Processed: {processedCount} images. Skipped: {skippedCount} images.");
    }

    private Texture2D LoadTexture(string filePath)
    {
        if (!File.Exists(filePath))
        {
            Debug.LogError($"File not found: {filePath}");
            return null;
        }

        byte[] fileData = File.ReadAllBytes(filePath);
        Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, false); 
        if (texture.LoadImage(fileData)) return texture;
        
        Debug.LogError($"Failed to load image data from: {filePath}");
        DestroyImmediate(texture);
        return null;
    }

    private void SaveTexture(Texture2D texture, string filePath)
    {
        byte[] bytes;
        string extension = Path.GetExtension(filePath).ToLowerInvariant();
        
        bytes = (extension == ".jpg" || extension == ".jpeg") ? 
            texture.EncodeToJPG() : texture.EncodeToPNG();
        
        try { File.WriteAllBytes(filePath, bytes); }
        catch (System.Exception e) { Debug.LogError($"Failed to save texture to {filePath}: {e.Message}"); }
    }

    private Texture2D AddShadows(Texture2D image, Texture2D shadowsInput)
    {
        if (image == null || shadowsInput == null) return null;

        int width = image.width;
        int height = image.height;

        // Convert shadow to grayscale
        Texture2D grayShadow = new Texture2D(shadowsInput.width, shadowsInput.height, TextureFormat.RGBA32, false);
        Color[] shadowColors = shadowsInput.GetPixels();
        Color[] grayShadowColors = new Color[shadowColors.Length];

        float minGray = float.MaxValue, maxGray = float.MinValue;

        for (int i = 0; i < shadowColors.Length; i++)
        {
            Color c = shadowColors[i];
            float gray = (c.r + c.g + c.b) / 3f;
            grayShadowColors[i] = new Color(gray, gray, gray, 1f);
            minGray = Mathf.Min(minGray, gray);
            maxGray = Mathf.Max(maxGray, gray);
        }

        grayShadow.SetPixels(grayShadowColors);
        grayShadow.Apply();

        // Normalize shadow values
        if (maxGray > minGray)
        {
            for (int i = 0; i < grayShadowColors.Length; i++)
            {
                float normalizedGray = (grayShadowColors[i].r - minGray) / (maxGray - minGray);
                grayShadowColors[i] = new Color(normalizedGray, normalizedGray, normalizedGray, 1f);
            }
            grayShadow.SetPixels(grayShadowColors);
            grayShadow.Apply();
        }

        // Apply blur and resize if needed
        Texture2D blurredShadow = ApplyGaussianBlur(grayShadow, blurSize);
        if (blurredShadow == null) { Destroy(grayShadow); return null; }

        Texture2D shadowToProcess = blurredShadow;
        if (blurredShadow.width != width || blurredShadow.height != height)
        {
            Texture2D resizedShadow = ResizeTexture(blurredShadow, width, height);
            if (resizedShadow == null) { Destroy(grayShadow); Destroy(blurredShadow); return null; }
            Destroy(blurredShadow); 
            shadowToProcess = resizedShadow;
        }

        // Process shadow values
        Color[] processedShadowPixels = shadowToProcess.GetPixels();
        for (int i = 0; i < processedShadowPixels.Length; i++)
        {
            float value = processedShadowPixels[i].r; 
            value = ApplyContrast(value, shadowContrast);
            value = value * shadowMultiplier;
            value = Mathf.Clamp01(value);

            if (useShadowSigmoid)
            {
                float sigmoidCenterNormalized = shadowSigmoidCenter / 255f;
                value = ApplySigmoid(value, sigmoidCenterNormalized, shadowSigmoidSteepness);
            }
            processedShadowPixels[i] = new Color(value, value, value, 1f);
        }
        shadowToProcess.SetPixels(processedShadowPixels);
        shadowToProcess.Apply();

        // Apply shadow to image
        Texture2D result = new Texture2D(width, height, image.format, false);
        Color[] imagePixels = image.GetPixels();
        Color[] resultPixels = new Color[imagePixels.Length];

        for (int i = 0; i < imagePixels.Length; i++)
        {
            float shadowPixelValue = shadowToProcess.GetPixel(i % width, i / width).r;
            float shadowFactor = Mathf.Lerp(shadowPixelValue, 1.0f, shadowTransparency);

            resultPixels[i] = new Color(
                imagePixels[i].r * shadowFactor,
                imagePixels[i].g * shadowFactor,
                imagePixels[i].b * shadowFactor,
                imagePixels[i].a
            );
        }

        result.SetPixels(resultPixels);
        result.Apply();

        Destroy(grayShadow);
        Destroy(shadowToProcess);

        return result;
    }

    private Texture2D AddHighlight(Texture2D objImg, Texture2D highlightImgInput)
    {
        if (objImg == null || highlightImgInput == null) return null;

        int width = objImg.width;
        int height = objImg.height;
        
        // Resize highlight if needed
        Texture2D highlightToProcess = highlightImgInput;
        if (highlightImgInput.width != width || highlightImgInput.height != height)
        {
            highlightToProcess = ResizeTexture(highlightImgInput, width, height);
            if (highlightToProcess == null) return null;
        }
        // Zmieniono format na RGBA32
        else 
        {
            highlightToProcess = new Texture2D(highlightImgInput.width, highlightImgInput.height, TextureFormat.RGBA32, false);
            Graphics.CopyTexture(highlightImgInput, highlightToProcess);
        }


        // Process highlight mask
        Color[] highlightPixels = highlightToProcess.GetPixels();
        float[] highlightMask = new float[highlightPixels.Length];

        for (int i = 0; i < highlightPixels.Length; i++)
        {
            Color c = highlightPixels[i];
            float gray = (c.r + c.g + c.b) / 3f;
            gray = ApplyContrast(gray, highlightContrast);
            
            float maskValue = gray;
            if (useHighlightSigmoid)
            {
                float sigmoidCenterNormalized = highlightSigmoidCenter / 255f;
                maskValue = ApplySigmoid(gray, sigmoidCenterNormalized, highlightSigmoidSteepness);
            }
            highlightMask[i] = Mathf.Clamp01(maskValue * highlightStrength);
        }

        // Apply highlights
        Texture2D result = new Texture2D(width, height, objImg.format, false);
        Color[] objPixels = objImg.GetPixels();
        Color[] resultPixels = new Color[objPixels.Length];

        for (int i = 0; i < objPixels.Length; i++)
        {
            float mask = highlightMask[i];
            Color original = objPixels[i];
            
            // Add a white component scaled by mask factor and highlight intensity
            Color addedLight = Color.white * mask * highlightIntensity;

            // Combine original color with the added light
            Color finalColor = original + addedLight;

            // Clamp each channel to ensure it stays within [0, 1] range
            finalColor.r = Mathf.Clamp01(finalColor.r);
            finalColor.g = Mathf.Clamp01(finalColor.g);
            finalColor.b = Mathf.Clamp01(finalColor.b);
            finalColor.a = original.a; // Preserve original alpha
            
            resultPixels[i] = finalColor;
        }

        result.SetPixels(resultPixels);
        result.Apply();

        // Destroy highlightToProcess only if it was a new texture created for resizing/copying
        if (highlightToProcess != highlightImgInput)
        {
            DestroyImmediate(highlightToProcess);
        }
        
        return result;
    }

    private float ApplySigmoid(float value, float center, float steepness)
    {
        if (steepness <= 0) steepness = 0.001f;
        return 1f / (1f + Mathf.Exp(-steepness * 100f * (value - center))); 
    }

    private float ApplyContrast(float value, float contrast)
    {
        return Mathf.Clamp01(((value - 0.5f) * contrast) + 0.5f);
    }

    private Texture2D ApplyGaussianBlur(Texture2D source, int blurRadius)
    {
        if (source == null) return null;
        if (blurRadius <= 0) {
            Texture2D noBlurCopy = new Texture2D(source.width, source.height, source.format, false);
            Graphics.CopyTexture(source, noBlurCopy);
            return noBlurCopy;
        }

        int width = source.width;
        int height = source.height;
        Color[] sourcePixels = source.GetPixels();
        Color[] resultPixels = new Color[sourcePixels.Length];
        Color[] tempPixels = new Color[sourcePixels.Length];

        // Two-pass box blur (horizontal + vertical)
        // Horizontal pass
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float r = 0, g = 0, b = 0, a = 0;
                int count = 0;
                for (int kx = -blurRadius; kx <= blurRadius; kx++)
                {
                    int sampleX = Mathf.Clamp(x + kx, 0, width - 1);
                    int sampleIndex = y * width + sampleX;
                    Color pixel = sourcePixels[sampleIndex];
                    r += pixel.r; g += pixel.g; b += pixel.b; a += pixel.a;
                    count++;
                }
                tempPixels[y * width + x] = new Color(r / count, g / count, b / count, a / count);
            }
        }

        // Vertical pass
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float r = 0, g = 0, b = 0, a = 0;
                int count = 0;
                for (int ky = -blurRadius; ky <= blurRadius; ky++)
                {
                    int sampleY = Mathf.Clamp(y + ky, 0, height - 1);
                    int sampleIndex = sampleY * width + x;
                    Color pixel = tempPixels[sampleIndex];
                    r += pixel.r; g += pixel.g; b += pixel.b; a += pixel.a;
                    count++;
                }
                resultPixels[y * width + x] = new Color(r / count, g / count, b / count, a / count);
            }
        }

        Texture2D result = new Texture2D(width, height, source.format, false);
        result.SetPixels(resultPixels);
        result.Apply();
        return result;
    }

    private Texture2D ResizeTexture(Texture2D source, int targetWidth, int targetHeight)
    {
        if (source == null) return null;
        
        RenderTexture rt = RenderTexture.GetTemporary(targetWidth, targetHeight, 0, RenderTextureFormat.Default, RenderTextureReadWrite.Linear);
        Graphics.Blit(source, rt);
        
        RenderTexture previous = RenderTexture.active;
        RenderTexture.active = rt;

        Texture2D result = new Texture2D(targetWidth, targetHeight, TextureFormat.RGBA32, false);
        result.ReadPixels(new Rect(0, 0, targetWidth, targetHeight), 0, 0);
        result.Apply();

        RenderTexture.active = previous;
        RenderTexture.ReleaseTemporary(rt);
        return result;
    }

    public void EnsureDirectoryExists(string path)
    {
        if (!Directory.Exists(path))
        {
            Directory.CreateDirectory(path);
            Debug.Log("Utworzono katalog: " + path);
        }
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (showPreview && previewDisplay != null)
        {
            UnityEditor.EditorApplication.delayCall += () => {
                if (this != null && showPreview && previewDisplay != null)
                {
                    GeneratePreview();
                    previewDisplay.gameObject.SetActive(true);
                }
                else if (this != null && previewDisplay != null)
                {
                    previewDisplay.gameObject.SetActive(false);
                }
            };
        }
        else if (previewDisplay != null)
        {
            previewDisplay.gameObject.SetActive(false);
        }
    }

    /// <summary>
    /// Generates a preview of image processing in the editor.
    /// </summary>
    [ContextMenu("Generate Preview")]
    public void GeneratePreview()
    {
        if (previewDisplay == null || !Directory.Exists(folderObjects))
        {
            Debug.LogWarning("Preview Display (RawImage) is not assigned or Object folder does not exist. Cannot generate preview.");
            return;
        }
        
        string[] objectFiles = Directory.GetFiles(folderObjects)
            .Where(s => imageExtensions.Contains(Path.GetExtension(s).ToLowerInvariant()))
            .ToArray();

        if (objectFiles.Length == 0)
        {
            Debug.LogWarning("No object files found in preview folder: " + folderObjects);
            return;
        }
            
        // Clean up previous preview objects
        if (previewOriginal != null) DestroyImmediate(previewOriginal);
        if (previewShadow != null) DestroyImmediate(previewShadow);
        if (previewResult != null) DestroyImmediate(previewResult);

        // Load preview images
        string firstObjectPath = objectFiles[0];
        previewOriginal = LoadTexture(firstObjectPath);
        if (previewOriginal == null) return;

        string firstObjectName = Path.GetFileName(firstObjectPath);
        string shadowPath = Path.Combine(folderShadows, firstObjectName);

        if (File.Exists(shadowPath))
        {
            previewShadow = LoadTexture(shadowPath);
        }
        
        if (previewShadow == null)
        {
            Debug.LogWarning("Brak pliku cienia do podgl du. Tworz  bia   tekstur  cienia.");
            previewShadow = new Texture2D(previewOriginal.width, previewOriginal.height, TextureFormat.RGBA32, false);
            Color[] whitePixels = Enumerable.Repeat(Color.white, previewShadow.width * previewShadow.height).ToArray();
            previewShadow.SetPixels(whitePixels);
            previewShadow.Apply();
        }

        // Process preview
        Texture2D shadowedImage = AddShadows(previewOriginal, previewShadow);
        if (shadowedImage == null) return;

        previewResult = AddHighlight(shadowedImage, previewShadow);
        DestroyImmediate(shadowedImage);
        
        if (previewResult == null) return;

        previewDisplay.texture = previewResult;
        previewDisplay.gameObject.SetActive(showPreview);
    }
#endif
}
