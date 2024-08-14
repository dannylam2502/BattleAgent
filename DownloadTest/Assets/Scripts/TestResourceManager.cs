using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Rendering;

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

    public async void OnClickButtonTestDownload()
    {
        timer.Reset();
        totalSize = 0;
        currentLog = string.Empty;
        ResourceLoaderManager.Instance.ReleaseAllResources();
        ResourceLoaderManager.Instance.semaphore.Dispose();
        ResourceLoaderManager.Instance.semaphore = new System.Threading.SemaphoreSlim(int.Parse(uiScript.infNumConcurrent.text));
        currentLog = "Downloading";
        UpdateLog();
        await DownloadFilesAsync(); // Start the UniTask and forget to avoid warnings
    }

    void UpdateLog()
    {
        uiScript.log.text = currentLog;
    }

    async UniTask DownloadFilesAsync()
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
        timer.Restart();
        await ResourceLoaderManager.Instance.ProcessQueue();
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
            var time = ResourceLoaderManager.Instance.stopwatch.ElapsedMilliseconds;
            currentLog = $"MaxThread = {ResourceLoaderManager.Instance.semaphore.CurrentCount} Downloaded in {timer.ElapsedMilliseconds} ms";
            float totalTimeSync = 0.0f, totalTimeLoad = 0.0f;
            var dictTimeSync = ResourceLoaderManager.Instance.dictURLToTimeWaitAsync;
            var dictTimeLoad = ResourceLoaderManager.Instance.dictURLToTimeLoad;
            foreach (var item in dictTimeSync)
            {
                totalTimeSync += item.Value;
            }
            foreach (var item in dictTimeLoad)
            {
                totalTimeLoad += item.Value;
            }
            currentLog += $"\n TotalTimeSync in Thread = {totalTimeSync}, totalTimeLoad in Thread = {totalTimeLoad}";
            UpdateLog();
        }
        Debug.Log($"Downloaded num = {numDownloaded} {obj} Complete ");
    }
}
