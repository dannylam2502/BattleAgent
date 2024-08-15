using System;
using System.Collections.Generic;
using System.Resources;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Rendering;
using static ResourceLoaderManager;

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
        totalSize = 0;
        currentLog = string.Empty;
        ResourceLoaderManager.Instance.ReleaseAllResources();
        ResourceLoaderManager.Instance.semaphore.Dispose();
        ResourceLoaderManager.Instance.semaphore = new System.Threading.SemaphoreSlim(int.Parse(uiScript.infNumConcurrent.text));
        ResourceLoaderManager.Instance.SetLoaderState(ResourceLoaderManager.LoaderState.FocusDownloading);
        var dictTimeLoadResource = ResourceLoaderManager.Instance.dictTypeToTimeLoad;
        foreach (ResourceType type in Enum.GetValues(typeof(ResourceType)))
        {
            ResourceLoaderManager.Instance.dictTypeToTimeLoad[type] = 0.0f;
        }
        currentLog = "Downloading";
        UpdateLog();
        DownloadFilesAsync();
    }

    void UpdateLog()
    {
        var result = $"Speed = {ResourceLoaderManager.Instance.downloadSpeed}\n";
        result += currentLog;
        uiScript.log.text = result;
    }

    private void Update()
    {
        UpdateLog();
        if (Input.GetKeyDown(KeyCode.T))
        {
            timer.Reset();
            totalSize = 0;
            currentLog = string.Empty;
            ResourceLoaderManager.Instance.ReleaseAllResources();
            ResourceLoaderManager.Instance.semaphore.Dispose();
            ResourceLoaderManager.Instance.UpdateThreadCount();
            ResourceLoaderManager.Instance.semaphore = new System.Threading.SemaphoreSlim(ResourceLoaderManager.Instance.maxThreads);
            currentLog = "Downloading";
            UpdateLog();
            DownloadFilesAsync();
        }
    }

    void DownloadFilesAsync()
    {
        string[] urls = ReadFileLines();

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
            var type = GetResourceTypeByExtension(url);
            ResourceLoaderManager.Instance.GetResource(type, url, OnDownloadComplete);
        }
        timer.Restart();
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
            currentLog = $"MaxThread = {ResourceLoaderManager.Instance.semaphore.CurrentCount} Downloaded And Process in {timer.ElapsedMilliseconds} ms";
            float totalTimeSync = 0.0f, totalTimeLoadInThread = 0.0f;
            var dictTimeSync = ResourceLoaderManager.Instance.dictURLToTimeWaitAsync;
            var dictTimeLoad = ResourceLoaderManager.Instance.dictURLToTimeLoad;
            var dictTimeLoadResource = ResourceLoaderManager.Instance.dictTypeToTimeLoad;
            foreach (var item in dictTimeSync)
            {
                totalTimeSync += item.Value;
            }
            foreach (var item in dictTimeLoad)
            {
                totalTimeLoadInThread += item.Value;
            }
            var totalSize = ResourceLoaderManager.Instance.GetTotalSize();
            currentLog += $"\nTime Download {ResourceLoaderManager.Instance.stopWatchDownloadSpeed.ElapsedMilliseconds}";
            //currentLog += $"\n TotalTimeSync in Thread = {totalTimeSync}, totalTimeLoad in Thread = {totalTimeLoadInThread}, TotalSize = {totalSize}";
            foreach (var kvp in dictTimeLoadResource)
            {
                currentLog += $"\nTime Load {kvp.Key} = {kvp.Value}";
            }
            UpdateLog();
        }
        Debug.Log($"Downloaded num = {numDownloaded} {obj} Complete ");
    }

    public ResourceLoaderManager.ResourceType GetResourceTypeByExtension(string url)
    {
        if (url.Contains(".webp"))
        {
            return ResourceLoaderManager.ResourceType.Webp;
        }
        else if (url.Contains(".zip"))
        {

        }
        return ResourceLoaderManager.ResourceType.Default;
    }
}
