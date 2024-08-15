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
    private Dictionary<string, object> cacheResource = new Dictionary<string, object>();
    private Dictionary<string, Action<object>> pendingCallbacks = new Dictionary<string, Action<object>>();
    private PriorityQueue<ResourceRequest> requestQueue = new PriorityQueue<ResourceRequest>();
    protected LoaderFactory loaderFactory;
    protected DownloadHandler downloadHandler;
    

    public int maxThreads;
    public float downloadSpeed = 0f;
    private const int MAX_RETRIES = 3;

    private Dictionary<ResourceType, IResourceLoader> providers;
    public SemaphoreSlim semaphore = new SemaphoreSlim(1);

    public LoaderState CurLoaderState { get; set; }

    // DEBUG, BENCHMARK
    public System.Diagnostics.Stopwatch stopWatchDownloadSpeed = new System.Diagnostics.Stopwatch();
    public System.Diagnostics.Stopwatch stopWatchProcess = new System.Diagnostics.Stopwatch();
    public Dictionary<ResourceType, Dictionary<string, float>> dictTypeToURLToTimeLoad = new();
    public Dictionary<string, float> dictURLToTimeWaitAsync = new Dictionary<string, float>();
    public Dictionary<ResourceType, float> dictTypeToTimeLoad = new Dictionary<ResourceType, float>();

    public long numByteDownloaded = 0;
    public float timeStartDownload = 0.0f;
        
    private void Awake()
    {
        maxThreads = Mathf.Min(SystemInfo.processorCount * 2, 16);  // Cap at 16 threads
        //maxThreads = 4;
        loaderFactory = new LoaderFactory();
        downloadHandler = new DownloadHandler();
        semaphore = new SemaphoreSlim(maxThreads); // You can set maxThreads based on your needs
        CurLoaderState = LoaderState.Balance;
        foreach (ResourceType type in Enum.GetValues(typeof(ResourceType)))
        {
            dictTypeToTimeLoad.Add(type, 0.0f);
            dictTypeToURLToTimeLoad.Add(type, new Dictionary<string, float>());
        }
        
    }

    private void OnDestroy()
    {
        downloadHandler.Dispose();
    }

    public async void Update()
    {
        if (!requestQueue.IsEmpty)
        {
            if (CurLoaderState == LoaderState.FocusDownloading)
            {
                await ProcessQueueFocusMode();
            }
            else
            {
                await ProcessQueueBalanceMode();
            }
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

    public async UniTask ProcessQueueFocusMode()
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

        ProcessAssets(cacheData.Keys.ToList());
        InvokeAllCallbacks(pendingCallbacks.Keys.ToList());

        // DEBUG Complete
        FindObjectOfType<TestResourceManager>().OnOperationComplete();
    }

    public async UniTask ProcessQueueBalanceMode()
    {
        timeStartDownload = Time.realtimeSinceStartup;
        numByteDownloaded = 0;
        List<UniTask> tasks = new List<UniTask>();

        while (!requestQueue.IsEmpty)
        {
            ResourceRequest request = requestQueue.Dequeue();

            // Start the download immediately and add the task to the list
            UniTask downloadTask = DownloadAndProcessResource(request);
            tasks.Add(downloadTask);
        }

        if (tasks.Count > 0)
        {
            stopWatchDownloadSpeed.Restart();
            await UniTask.WhenAll(tasks); // Wait for the current batch to complete
            tasks.Clear(); // Clear the list for the next batch
            stopWatchDownloadSpeed.Stop();
            Debug.LogError($"Resource Loader Download Time = {stopWatchDownloadSpeed.ElapsedMilliseconds}");
        }


        // DEBUG Complete
        FindObjectOfType<TestResourceManager>().OnOperationComplete();
    }

    public void InvokeAllCallbacks(List<string> callbackIds)
    {
        foreach (var id in callbackIds)
        {
            if (pendingCallbacks.ContainsKey(id))
            {
                var callback = pendingCallbacks[id];
                callback?.Invoke($"{id}");
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
                    //var response = await downloader.GetAsync(request.Url);
                    if (request.Type != ResourceType.Audio)
                    {
                        var response = await downloadHandler.DownloadByteAsync(request.Url);
                        if (response.IsSuccessStatusCode)
                        {
                            var data = await response.Content.ReadAsByteArrayAsync();
                            DataCache assetCache = new DataCache()
                            { Content = data, Type = request.Type, Id = request.Url };
                            cacheData[request.Url] = assetCache;
                            Debug.Log($"Downloaded {request.Url}");
                            dictTypeToURLToTimeLoad[request.Type][request.Url] = sw.ElapsedMilliseconds; // Record download time
                            numByteDownloaded += data.LongLength;
                            UpdateDownloadSpeed(numByteDownloaded);
                            return;
                        }
                    }
                    else if (request.Type == ResourceType.Audio)
                    {
                        // For Audio, download and decode at the same time *WARNING*
                        var data = await downloadHandler.DownloadAudioClip(request.Url);
                        dictTypeToURLToTimeLoad[request.Type][request.Url] = sw.ElapsedMilliseconds; // Record download time
                        cacheResource[request.Url] = data;
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


    private async UniTask DownloadAndProcessResource(ResourceRequest request)
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
                    //var response = await downloader.GetAsync(request.Url);
                    if (request.Type != ResourceType.Audio)
                    {
                        var response = await downloadHandler.DownloadByteAsync(request.Url);
                        if (response.IsSuccessStatusCode)
                        {
                            var data = await response.Content.ReadAsByteArrayAsync();
                            DataCache assetCache = new DataCache()
                            { Content = data, Type = request.Type, Id = request.Url };
                            cacheData[request.Url] = assetCache;
                            Debug.Log($"Downloaded {request.Url}");
                            dictTypeToURLToTimeLoad[request.Type][request.Url] = sw.ElapsedMilliseconds; // Record download time
                            numByteDownloaded += data.LongLength;
                            UpdateDownloadSpeed(numByteDownloaded);

                            // Process asset async here
                            await ProcessAssetAsync(assetCache);
                            InvokeCallback(request.Url);
                            return;
                        }
                    }
                    else if (request.Type == ResourceType.Audio)
                    {
                        // For Audio, download and decode at the same time *WARNING*
                        var data = await downloadHandler.DownloadAudioClip(request.Url);
                        dictTypeToURLToTimeLoad[request.Type][request.Url] = sw.ElapsedMilliseconds; // Record download time
                        cacheResource[request.Url] = data;
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

    private void InvokeCallback(string url)
    {
        if (pendingCallbacks.TryGetValue(url, out Action<object> callback))
        {
            if (cacheResource.TryGetValue(url, out object obj))
            {
                callback?.Invoke(obj);
            }
        }
        pendingCallbacks.Remove(url);
    }

    public void ReleaseAllResources()
    {
        cacheData.Clear();
        cacheResource.Clear();
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
        object obj = null;
        if (data != null)
        {
            if (data.Type == ResourceType.Webp)
            {
                obj = loaderFactory.LoadResourceWebp(data.Content, false, true);
            }
            else if (data.Type == ResourceType.Json)
            {
                obj = loaderFactory.LoadResourceJson(data.Content);
            }
            if (obj != null)
            {
                cacheResource[data.Id] = obj;
                stopWatchProcess.Stop();
                if (obj != null)
                {
                    dictTypeToTimeLoad[data.Type] += stopWatchProcess.ElapsedMilliseconds;
                }
                Debug.Log($"Processed Data Type {data.Type} ID = {data.Id} time = {stopWatchProcess.ElapsedMilliseconds}ms");
            }
            else
            {
                Debug.Log($"obj is null Processed Data Type {data.Type} ID = {data.Id}");
            }
        }
    }

    public async UniTask ProcessAssetAsync(DataCache data)
    {
        stopWatchProcess.Restart();
        object obj = null;
        if (data != null)
        {
            if (data.Type == ResourceType.Webp)
            {
                obj = await loaderFactory.LoadResourceWebpAsync(data.Content, false, true);
            }
            else if (data.Type == ResourceType.Json)
            {
                obj = await loaderFactory.LoadResourceJsonAsync(data.Content);
            }
            if (obj != null)
            {
                cacheResource[data.Id] = obj;
                stopWatchProcess.Stop();
                if (obj != null)
                {
                    dictTypeToTimeLoad[data.Type] += stopWatchProcess.ElapsedMilliseconds;
                }
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

    public void ResetForNextTest()
    {
        ResourceLoaderManager.Instance.ReleaseAllResources();
        ResourceLoaderManager.Instance.semaphore.Dispose();
        var dictTimeLoadResource = ResourceLoaderManager.Instance.dictTypeToTimeLoad;
        foreach (ResourceType type in Enum.GetValues(typeof(ResourceType)))
        {
            ResourceLoaderManager.Instance.dictTypeToTimeLoad[type] = 0.0f;
        }
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
