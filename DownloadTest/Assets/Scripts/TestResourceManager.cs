using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;

public class TestResourceManager : MonoBehaviour
{
    public string filePath = "AssetLog"; // Path within Resources (exclude the .txt extension)
    public UIScript uiScript;
    public int totalSize = 0;

    public string currentLog;

    public Dictionary<string, byte[]> dictURLtoByte = new Dictionary<string, byte[]>();

    int numDownloaded = 0;
    int totalDownloads = 0;
    System.Diagnostics.Stopwatch timer = new System.Diagnostics.Stopwatch();

    public void OnClickButtonTestDownload()
    {
        timer.Reset();
        timer.Start();
        totalSize = 0;
        currentLog = string.Empty;
        DownloadFilesAsync(); // Start the UniTask and forget to avoid warnings
    }

    void UpdateLog()
    {
        uiScript.log.text = currentLog;
    }

    void DownloadFilesAsync()
    {
        string[] urls = ReadFileLines(); // Use synchronous file reading from Resources

        if (urls == null || urls.Length == 0)
        {
            Debug.LogError("Failed to load URLs from Resources.");
            currentLog += "Failed to load URLs from Resources.\n";
            UpdateLog();
            return;
        }

        totalDownloads = urls.Length;
        numDownloaded = 0;

        foreach (string url in urls)
        {
            // Call the ResourceLoaderManager to get resources asynchronously
            ResourceLoaderManager.Instance.GetResource<Texture2D>(url, OnDownloadComplete);
        }
        ResourceLoaderManager.Instance.ProcessQueue().Forget();
    }

    string[] ReadFileLines()
    {
        TextAsset textAsset = Resources.Load<TextAsset>(filePath);
        if (textAsset == null)
        {
            Debug.LogError($"Failed to load {filePath} from Resources.");
            return null;
        }

        return textAsset.text.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
    }

    public void OnDownloadComplete(object obj)
    {
        numDownloaded++;
        if (timer.IsRunning)
        {
            timer.Stop();
            Debug.LogError($"Done Downloaded in {timer.ElapsedMilliseconds} ms");
            currentLog = $"MaxThread = {ResourceLoaderManager.Instance.maxThreads} Downloaded in {timer.ElapsedMilliseconds} ms";
            UpdateLog();
        }
        Debug.Log($"Downloaded num = {numDownloaded} {obj} Complete ");
        currentLog += $"Downloaded num = {numDownloaded} {obj} Complete ";
        UpdateLog();
    }
}
