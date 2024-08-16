using System;
using System.Collections.Generic;
using System.Resources;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;
using UnityEngine.UIElements;
using static ResourceLoaderManager;

public class TestResourceManager : MonoBehaviour
{
    public string filePath = "AssetLog"; // Path within Resources (exclude the .txt extension)
    public UIScript uiScript;
    public int totalSize = 0;
    public RawImage image;

    public string currentLog;

    public Dictionary<string, byte[]> dictURLtoByte = new Dictionary<string, byte[]>();

    int numDownloaded = 0;
    int totalDownloads = 0;
    System.Diagnostics.Stopwatch timer = new System.Diagnostics.Stopwatch();

    public void OnClickButtonTestDownload()
    {
        timer.Reset();
        totalSize = 0;
        ResourceLoaderManager.Instance.ResetForNextTest();
        ResourceLoaderManager.Instance.semaphore = new System.Threading.SemaphoreSlim(int.Parse(uiScript.infNumConcurrent.text));
        currentLog = $"Current Mode: {ResourceLoaderManager.Instance.CurLoaderState}";
        UpdateLog();
        DownloadFilesAsync();
    }

    void UpdateLog()
    {
        var result = $"Speed = {ResourceLoaderManager.Instance.downloadSpeed} Bytes = {ResourceLoaderManager.Instance.numByteDownloaded} (exclude Audio)\n";
        result += currentLog;
        uiScript.log.text = result;
    }

    private void Update()
    {
        UpdateLog();
        if (Input.GetKeyDown(KeyCode.F))
        {
            ResourceLoaderManager.Instance.CurLoaderState = LoaderState.FocusDownloading;
        }
        if (Input.GetKeyDown(KeyCode.B))
        {
            ResourceLoaderManager.Instance.CurLoaderState = LoaderState.Balance;
        }
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
            DownloadFiles();
        }
    }

    void DownloadFiles()
    {
        timer.Restart();
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
            ResourceLoaderManager.Instance.GetResource(type, url, obj =>
            {
                numDownloaded++;
                Debug.Log($"Downloaded num = {numDownloaded} {obj.GetType()} Complete ");
            });
        }
    }

    void DownloadFilesAsync()
    {
        timer.Restart();
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
            GetResourceAsync(type, url).Forget();
        }
    }

    async UniTask GetResourceAsync(ResourceType type, string url)
    {
        var obj = await ResourceLoaderManager.Instance.GetResourceAsync(type, url);
        if (obj is Texture2D)
        {
            var texture = (Texture2D)obj;
            if (texture != null)
            {
                image.texture = texture;
            }
        }
        Debug.LogWarning($"Download Complete {obj.GetType()}");
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

    public ResourceLoaderManager.ResourceType GetResourceTypeByExtension(string url)
    {
        var extension = UrlUtils.GetExtension(url);
        if (extension == "webp")
        {
            return ResourceLoaderManager.ResourceType.Webp;
        }
        else if (extension == "mp3" || extension == "wav")
        {
            return ResourceLoaderManager.ResourceType.Audio;
        }
        else if (extension == "zip")
        {
            return ResourceType.JObject;
        }
        else if (extension == "json")
        {
            return ResourceType.Json;
        }
        return ResourceLoaderManager.ResourceType.Default;
    }

    public void OnOperationComplete()
    {
        if (timer.IsRunning)
        {
            timer.Stop();
            currentLog = $"Current Mode: {ResourceLoaderManager.Instance.CurLoaderState}\n";
            currentLog += $"MaxThread = {ResourceLoaderManager.Instance.semaphore.CurrentCount} Downloaded And Processed in {timer.ElapsedMilliseconds} ms";
            float totalTimeSync = 0.0f, totalTimeLoadInThread = 0.0f;
            var dictTimeSync = ResourceLoaderManager.Instance.dictURLToTimeWaitAsync;
            var dictTimeLoad = ResourceLoaderManager.Instance.dictTypeToURLToTimeLoad;
            var dictTimeLoadResource = ResourceLoaderManager.Instance.dictTypeToTimeLoad;
            var dictTimeDownloadInThread = new Dictionary<ResourceLoaderManager.ResourceType, float>();
            foreach (var item in dictTimeSync)
            {
                totalTimeSync += item.Value;
            }
            foreach (var item in dictTimeLoad)
            {
                dictTimeDownloadInThread[item.Key] = 0.0f;
                foreach (var kvp in item.Value)
                {
                    totalTimeLoadInThread += kvp.Value;
                    dictTimeDownloadInThread[item.Key] += kvp.Value;
                }
            }
            var totalSize = ResourceLoaderManager.Instance.GetTotalSize();
            currentLog += $"\nTime Download {ResourceLoaderManager.Instance.stopWatchDownloadSpeed.ElapsedMilliseconds}";
            foreach (var item in dictTimeDownloadInThread)
            {
                currentLog += $"\nTime Download In thread {item.Key} = {item.Value}";
            }
            //currentLog += $"\n TotalTimeSync in Thread = {totalTimeSync}, totalTimeLoad in Thread = {totalTimeLoadInThread}, TotalSize = {totalSize}";
            foreach (var kvp in dictTimeLoadResource)
            {
                currentLog += $"\nTime Load {kvp.Key} = {kvp.Value}";
            }
            UpdateLog();
        }
    }
}
