using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using UnityEngine;
using UnityEngine.UI;

public class TestPng : MonoBehaviour
{
    public RawImage image; // Assign your RawImage component in the inspector
    public string pngDirectory = "PNGFiles"; // Directory where PNG files are stored
    public UIScript uiScript;
    public TestDecode testDecode;

    public void OnClickBtnStartPngLoadTest()
    {
        var pngFiles = testDecode.dictPNGData;
        StartCoroutine(TestLoadPng(pngFiles));
    }

    private Dictionary<string, byte[]> LoadPngFiles(string directoryPath)
    {
        Dictionary<string, byte[]> pngFiles = new Dictionary<string, byte[]>();

        // Get all PNG files in the directory
        string[] files = Directory.GetFiles(directoryPath, "*.png");

        foreach (string filePath in files)
        {
            try
            {
                byte[] fileData = File.ReadAllBytes(filePath);
                string fileName = Path.GetFileName(filePath);
                pngFiles.Add(fileName, fileData);
                UnityEngine.Debug.Log($"Loaded {fileName} from {filePath}");
            }
            catch (System.Exception ex)
            {
                UnityEngine.Debug.LogError($"Failed to load file {filePath}: {ex.Message}");
            }
        }

        return pngFiles;
    }

    private IEnumerator TestLoadPng(Dictionary<string, byte[]> pngFiles)
    {
        long totalTime = 0;

        Stopwatch stopwatch = new Stopwatch();

        foreach (var pngFile in pngFiles)
        {
            string fileName = pngFile.Key;
            byte[] data = pngFile.Value;

            UnityEngine.Debug.Log($"Testing file: {fileName}");
            stopwatch.Reset();
            // Test PNG Load
            stopwatch.Start();
            LoadPng(image, data);
            stopwatch.Stop();
            UnityEngine.Debug.Log($"LoadPng Time for {fileName}: {stopwatch.ElapsedMilliseconds} ms");
            totalTime += stopwatch.ElapsedMilliseconds;

            yield return new WaitForEndOfFrame(); // Small delay to avoid performance overlap
        }

        // Log total time for loading all PNG files
        UnityEngine.Debug.Log($"Total Time for loading PNGs: {totalTime} ms");
        uiScript.PNGLog.text = $"Total Time for loading PNGs: {totalTime} ms";
    }

    private void LoadPng(RawImage image, byte[] pngBytes)
    {
        Texture2D texture = new Texture2D(2, 2);
        bool isLoaded = texture.LoadImage(pngBytes); // LoadImage will auto-resize the texture dimensions

        if (isLoaded)
        {
            image.texture = texture;
        }
        else
        {
            UnityEngine.Debug.LogError("PNG Load Error");
        }
    }
}
