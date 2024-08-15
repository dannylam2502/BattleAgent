using UnityEngine;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Net.Http;
using Cysharp.Threading.Tasks;
using System.Threading;
using System.Linq;

public class ResourceLoaderManager : MonoBehaviour
{
    private static ResourceLoaderManager _instance;
    public static ResourceLoaderManager Instance
    {
        get
        {
            if (_instance == null)
            {
                GameObject go = new GameObject("ResourceLoaderManager");
                _instance = go.AddComponent<ResourceLoaderManager>();
                DontDestroyOnLoad(go);
            }
            return _instance;
        }
    }

    // In MB
    private Dictionary<string, DataCache> cacheData = new Dictionary<string, DataCache>();
    private Dictionary<string, Action<object>> pendingCallbacks = new Dictionary<string, Action<object>>();
    private PriorityQueue<ResourceRequest> requestQueue = new PriorityQueue<ResourceRequest>();
    protected LoaderFactory loaderFactory = new LoaderFactory();
    

    public int maxThreads;
    public float downloadSpeed = 0f;
    private const int MAX_RETRIES = 3;

    private HttpClient downloader;
    private Dictionary<ResourceType, IResourceLoader> providers;
    public SemaphoreSlim semaphore = new SemaphoreSlim(1);

    protected LoaderState CurLoaderState { get; private set; }

    // DEBUG, BENCHMARK
    public System.Diagnostics.Stopwatch stopWatchDownloadSpeed = new System.Diagnostics.Stopwatch();
    public System.Diagnostics.Stopwatch stopWatchProcess = new System.Diagnostics.Stopwatch();
    public Dictionary<string, float> dictURLToTimeLoad = new Dictionary<string, float>();
    public Dictionary<string, float> dictURLToTimeWaitAsync = new Dictionary<string, float>();
    public Dictionary<ResourceType, float> dictTypeToTimeLoad = new Dictionary<ResourceType, float>();

    public long numByteDownloaded = 0;
    public float timeStartDownload = 0.0f;
        
    private void Awake()
    {
        maxThreads = Mathf.Min(SystemInfo.processorCount * 2, 16);  // Cap at 16 threads
        //maxThreads = 4;
        downloader = new HttpClient();
        semaphore = new SemaphoreSlim(maxThreads); // You can set maxThreads based on your needs

        foreach (ResourceType type in Enum.GetValues(typeof(ResourceType)))
        {
            dictTypeToTimeLoad.Add(type, 0.0f);
        }
        
    }

    private void OnDestroy()
    {
    }

    public async void Update()
    {
        if (!requestQueue.IsEmpty)
        {
            await ProcessQueue();
        }
    }

    public void GetResource(ResourceType type, string url, Action<object> callback, int priority = 0, object user = null)
    {
        if (cacheData.TryGetValue(url, out DataCache cachedResource))
        {
            callback?.Invoke(cachedResource);
            //AddResourceUser(url, user);
            return;
        }

        if (pendingCallbacks.ContainsKey(url))
        {
            pendingCallbacks[url] += callback;
        }
        else
        {
            pendingCallbacks[url] = callback;
        }

        requestQueue.Enqueue(new ResourceRequest(url, type, priority));
    }

    public async UniTask ProcessQueue()
    {
        timeStartDownload = Time.realtimeSinceStartup;
        numByteDownloaded = 0;
        List<UniTask> downloadTasks = new List<UniTask>();

        while (!requestQueue.IsEmpty)
        {
            ResourceRequest request = requestQueue.Dequeue();

            // Start the download immediately and add the task to the list
            UniTask downloadTask = DownloadResource(request);
            downloadTasks.Add(downloadTask);
        }
        
        if (downloadTasks.Count > 0)
        {
            stopWatchDownloadSpeed.Restart();
            await UniTask.WhenAll(downloadTasks); // Wait for the current batch to complete
            downloadTasks.Clear(); // Clear the list for the next batch
            stopWatchDownloadSpeed.Stop();
            Debug.LogError($"Resource Loader Download Time = {stopWatchDownloadSpeed.ElapsedMilliseconds}");
        }

        if (CurLoaderState == LoaderState.FocusDownloading)
        {
            ProcessAssets(cacheData.Keys.ToList());
        }

        InvokeAllCallbacks(pendingCallbacks.Keys.ToList());
    }

    public void InvokeAllCallbacks(List<string> callbackIds)
    {
        foreach (var id in callbackIds)
        {
            if (cacheData.TryGetValue(id, out DataCache obj))
            {
                long size = 0;
                size = obj.Content.LongLength;
                if (pendingCallbacks.ContainsKey(id))
                {
                    var callback = pendingCallbacks[id];
                    callback?.Invoke($"{id} size = {size}");
                }
            }
        }
        foreach (var id in callbackIds)
        {
            pendingCallbacks.Remove(id, out Action<object> obj);
        }
    }

    private async UniTask DownloadResource(ResourceRequest request)
    {
        var sw = new System.Diagnostics.Stopwatch();

        // Measure semaphore wait time
        sw.Start();
        await semaphore.WaitAsync(); // Wait for a slot to become available
        sw.Stop();
        dictURLToTimeWaitAsync[request.Url] = sw.ElapsedMilliseconds;

        // Measure download time
        sw.Restart();
        try
        {
            for (int retry = 0; retry < MAX_RETRIES; retry++)
            {
                try
                {
                    sw.Restart(); // Restart stopwatch for each retry to measure only download time
                    var response = await downloader.GetAsync(request.Url);
                    if (response.IsSuccessStatusCode)
                    {
                        var data = await response.Content.ReadAsByteArrayAsync();
                        DataCache assetCache = new DataCache()
                        { Content = data, Type = request.Type, Id = request.Url};
                        cacheData[request.Url] = assetCache;
                        Debug.Log($"Downloaded {request.Url}");
                        dictURLToTimeLoad[request.Url] = sw.ElapsedMilliseconds; // Record download time
                        numByteDownloaded += data.LongLength;
                        UpdateDownloadSpeed(numByteDownloaded);
                        return;
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"Download failed (Attempt {retry + 1}/{MAX_RETRIES}): {e.Message}");
                    await UniTask.Delay(Mathf.FloorToInt(1000 * Mathf.Pow(2, retry)));
                }
            }

            Debug.LogError($"Failed to download resource after {MAX_RETRIES} attempts: {request.Url}");
        }
        finally
        {
            semaphore.Release(); // Release the semaphore slot for the next task
        }
    }


    private async UniTask<(string, object)> DownloadAndProcessResource(string url, Type type)
    {
        ////var startTime = Time.realtimeSinceStartup;
        //var response = await httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        //response.EnsureSuccessStatusCode();

        ////var contentLength = response.Content.Headers.ContentLength ?? 0;
        //var content = await response.Content.ReadAsByteArrayAsync();

        ////UpdateDownloadSpeed(content.Length, startTime);

        //return (url, content);
        return (null, null);
    }

    public void SetLoaderState(LoaderState newState)
    {
        CurLoaderState = newState;
    }

    private void UpdateDownloadSpeed(long bytes)
    {
        float elapsedTime = Time.realtimeSinceStartup - timeStartDownload;
        Debug.LogError($"Time = {elapsedTime}, bytes = {bytes}");
        downloadSpeed = bytes / (elapsedTime * 1024 * 1024); // MB per second
    }

    public void UpdateThreadCount()
    {
        maxThreads = Mathf.Min(SystemInfo.processorCount * 2, Math.Max(2, Mathf.CeilToInt(downloadSpeed * 2)));
    }

    private void InvokeCallback(string url, object resource)
    {
        if (pendingCallbacks.TryGetValue(url, out Action<object> callback))
        {
            callback?.Invoke(resource);
        }
    }

    public void ReleaseAllResources()
    {
        cacheData.Clear();
        //resourceUsers.Clear();
    }

    public long GetTotalSize()
    {
        long total = 0;
        foreach (var item in cacheData)
        {
            byte[] data = item.Value.Content;
            total += data.Length;
        }
        return total;
    }

    public void ProcessAssets(List<string> assetIds)
    {
        foreach (var id in assetIds)
        {
            if (cacheData.ContainsKey(id))
            {
                ProcessAsset(cacheData[id]);
            }
        }
    }

    public void ProcessAsset(DataCache data)
    {
        stopWatchProcess.Restart();
        UnityEngine.Object obj = null;
        if (data != null)
        {
            if (data.Type == ResourceType.Webp)
            {
                obj = loaderFactory.LoadResource(data.Content, false, true);
                stopWatchProcess.Stop();
                if (obj != null)
                {
                    dictTypeToTimeLoad[data.Type] += stopWatchProcess.ElapsedMilliseconds;
                }
            }
            if (obj != null)
            {
                Debug.Log($"Processed Data Type {data.Type} ID = {data.Id} time = {stopWatchProcess.ElapsedMilliseconds}ms");
            }
            else
            {
                Debug.Log($"obj is null Processed Data Type {data.Type} ID = {data.Id}");
            }
        }
    }

    public enum ResourceType
    {
        Default = 0,
        Webp,
        Png,
        Audio,
        Json,
        Binary
    }

    public class DataCache
    {
        public string Id;
        public byte[] Content { get; set; }
        public ResourceType Type { get; set; }
    }

    public class ResourceRequest : IComparable<ResourceRequest>
    {
        public string Url { get; }
        public ResourceType Type { get; }
        public int Priority { get; }

        public ResourceRequest(string url, ResourceType type, int priority)
        {
            Url = url;
            Type = type;
            Priority = priority;
        }

        public int CompareTo(ResourceRequest other)
        {
            return other.Priority.CompareTo(Priority); // Max-heap: higher priority comes first
        }
    }

    public enum LoaderState
    {
        Default = 0,
        FocusDownloading, // Prioritize download, all tasks are for download. Handle things in mainthread after done
        Balance, // Process assets as long as it's finished downloading
    }

    public class PriorityQueue<T> where T : IComparable<T>
    {
        private List<T> data;

        public PriorityQueue()
        {
            data = new List<T>();
        }

        // Adds an item to the priority queue
        public void Enqueue(T item)
        {
            data.Add(item);
            int currentIndex = data.Count - 1;
            while (currentIndex > 0)
            {
                int parentIndex = (currentIndex - 1) / 2;
                if (data[currentIndex].CompareTo(data[parentIndex]) <= 0) break;

                Swap(currentIndex, parentIndex);
                currentIndex = parentIndex;
            }
        }

        // Removes and returns the item with the highest priority (i.e., root of the heap)
        public T Dequeue()
        {
            if (IsEmpty)
                throw new InvalidOperationException("PriorityQueue is empty.");

            int lastIndex = data.Count - 1;
            T frontItem = data[0];
            data[0] = data[lastIndex];
            data.RemoveAt(lastIndex);

            lastIndex--;
            int parentIndex = 0;
            while (true)
            {
                int leftChildIndex = 2 * parentIndex + 1;
                if (leftChildIndex > lastIndex) break;

                int rightChildIndex = leftChildIndex + 1;
                int swapIndex = leftChildIndex;

                if (rightChildIndex <= lastIndex && data[rightChildIndex].CompareTo(data[leftChildIndex]) > 0)
                {
                    swapIndex = rightChildIndex;
                }

                if (data[parentIndex].CompareTo(data[swapIndex]) >= 0) break;

                Swap(parentIndex, swapIndex);
                parentIndex = swapIndex;
            }

            return frontItem;
        }

        // Returns the item with the highest priority without removing it
        public T Peek()
        {
            if (IsEmpty)
                throw new InvalidOperationException("PriorityQueue is empty.");

            return data[0];
        }

        // Returns true if the priority queue is empty
        public bool IsEmpty => data.Count == 0;

        // Returns the number of items in the priority queue
        public int Count => data.Count;

        // Swaps two elements in the list
        private void Swap(int indexA, int indexB)
        {
            T temp = data[indexA];
            data[indexA] = data[indexB];
            data[indexB] = temp;
        }
    }
}
