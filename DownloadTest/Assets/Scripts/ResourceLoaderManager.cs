using UnityEngine;
using System;
using System.Collections.Generic;
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
    // GroupID -> AssetID -> Data
    private Dictionary<string, Dictionary<string, DataCache>> cacheData = new();
    // GroupID -> AssetID -> Resource
    private Dictionary<string, Dictionary<string, object>> cacheResource = new();
    private Dictionary<string, Action<object>> pendingCallbacks = new Dictionary<string, Action<object>>();
    private PriorityQueue<ResourceRequest> requestQueue = new PriorityQueue<ResourceRequest>();
    protected LoaderFactory loaderFactory;
    protected DownloadHandler downloadHandler;

    // The Game ID, may determine which game ID/Assets to keep/unload
    public string AssetGroupId { get; private set; }
    

    public int maxThreads;
    public float downloadSpeed = 0f;
    private const int MAX_RETRIES = 3;

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
        SetAssetGroupId("TestingDownload");
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

    public void SetAssetGroupId(string assetGroupID)
    {
        AssetGroupId = assetGroupID;
        EnsureAssetGroupIdInCache();
    }

    public void EnsureAssetGroupIdInCache()
    {
        if (!cacheData.ContainsKey(AssetGroupId))
        {
            cacheData[AssetGroupId] = new Dictionary<string, DataCache>();
        }
        if (!cacheResource.ContainsKey(AssetGroupId))
        {
            cacheResource[AssetGroupId] = new Dictionary<string, object>();
        }
    }

    public void GetResource(ResourceType type, string url, Action<object> callback, int priority = 0, object user = null)
    {
        if (cacheData[AssetGroupId].TryGetValue(url, out DataCache cachedResource))
        {
            callback?.Invoke(cachedResource);
            //AddResourceUser(url, user);
            return;
        }

        EnqueueRequest(type, url, callback, priority);
    }

    public async UniTask<object> GetResourceAsync(ResourceType type, string url, int priority = 0)
    {
        // Check if the resource is already cached
        if (cacheResource[AssetGroupId].TryGetValue(url, out object cachedResource))
        {
            return cachedResource; // If it's cached, return immediately without creating a new task
        }

        // Create a UniTaskCompletionSource to manage task completion manually
        var completionSource = new UniTaskCompletionSource<object>();

        // Define a callback to be called when the resource is loaded
        void Callback(object resource)
        {
            // When the resource is ready, complete the task with the resource as the result
            completionSource.TrySetResult(resource);
        }

        // Start the resource loading process, passing in the callback
        EnqueueRequest(type, url, Callback, priority);

        // Return the task, which will complete when TrySetResult is called
        return await completionSource.Task;
    }

    protected void EnqueueRequest(ResourceType type, string url, Action<object> callback, int priority = 0)
    {
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
        List<string> callbackKeys = new List<string>();

        while (!requestQueue.IsEmpty)
        {
            ResourceRequest request = requestQueue.Dequeue();

            // Start the download immediately and add the task to the list
            UniTask downloadTask = DownloadResource(request);
            downloadTasks.Add(downloadTask);
            callbackKeys.Add(request.Url);
        }
        
        if (downloadTasks.Count > 0)
        {
            stopWatchDownloadSpeed.Restart();
            await UniTask.WhenAll(downloadTasks); // Wait for the current batch to complete
            downloadTasks.Clear(); // Clear the list for the next batch
            stopWatchDownloadSpeed.Stop();
            Debug.LogWarning($"Resource Loader Download Time = {stopWatchDownloadSpeed.ElapsedMilliseconds}");
        }

        ProcessAssets(callbackKeys);
        InvokeAllCallbacks(callbackKeys);

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
                if (cacheResource[AssetGroupId].ContainsKey(id))
                {
                    callback?.Invoke(cacheResource[AssetGroupId][id]);
                }
                else
                {
                    callback?.Invoke($"Miss cache id = {id}");
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
                    //var response = await downloader.GetAsync(request.Url);
                    if (request.Type != ResourceType.Audio)
                    {
                        var response = await downloadHandler.DownloadByteAsync(request.Url);
                        if (response.IsSuccessStatusCode)
                        {
                            var data = await response.Content.ReadAsByteArrayAsync();
                            DataCache assetCache = new DataCache()
                            { Content = data, Type = request.Type, Id = request.Url };
                            cacheData[AssetGroupId][request.Url] = assetCache;
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
                        cacheResource[AssetGroupId][request.Url] = data;
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
                            cacheData[AssetGroupId][request.Url] = assetCache;
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
                        cacheResource[AssetGroupId][request.Url] = data;
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
        Debug.Log($"Time = {elapsedTime}, bytes = {bytes}");
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
            if (cacheResource[AssetGroupId].TryGetValue(url, out object obj))
            {
                callback?.Invoke(obj);
            }
        }
        pendingCallbacks.Remove(url);
    }

    public void ReleaseAllResources()
    {
        foreach (var dict in cacheData)
        {
            dict.Value.Clear();
        }
        foreach (var dict in cacheResource)
        {
            dict.Value.Clear();
        }
        cacheData.Clear();
        cacheResource.Clear();
        Resources.UnloadUnusedAssets();
        //resourceUsers.Clear();
    }

    public long GetTotalSize()
    {
        long total = 0;
        foreach (var item in cacheData[AssetGroupId])
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
            if (cacheData[AssetGroupId].ContainsKey(id))
            {
                ProcessAsset(cacheData[AssetGroupId][id]);
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
                cacheResource[AssetGroupId][data.Id] = obj;
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
                cacheResource[AssetGroupId][data.Id] = obj;
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
        ReleaseAllResources();
        semaphore.Dispose();
        var dictTimeLoadResource = dictTypeToTimeLoad;
        foreach (ResourceType type in Enum.GetValues(typeof(ResourceType)))
        {
            dictTypeToTimeLoad[type] = 0.0f;
        }
        SetAssetGroupId(AssetGroupId);
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
