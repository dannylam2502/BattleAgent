using UnityEngine;
using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using System.Threading;
using System.Linq;
using Astro.Engine;
using Unity.VisualScripting;

namespace Astro.Engine
{
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
        public const int MAX_PARSING_TASKS = 5;

        // In MB
        // GroupID -> AssetID -> Data
        private Dictionary<string, Dictionary<string, DataCache>> cacheData = new();
        // GroupID -> AssetID -> Asset
        private Dictionary<string, Dictionary<string, object>> cacheAsset = new();
        private Dictionary<string, Action<object>> pendingCallbacks = new Dictionary<string, Action<object>>();
        private PriorityQueue<ResourceRequest> requestQueue = new PriorityQueue<ResourceRequest>();
        public LoaderFactory loaderFactory;
        protected DownloadHandler downloadHandler;

        // The Game ID, may determine which game ID/Assets to keep/unload
        public string AssetGroupId { get; private set; }


        public int maxThreads;
        public float downloadSpeed = 0f;
        private const int MAX_RETRIES = 3;

        public SemaphoreSlim semaphoreDownload;
        public SemaphoreSlim semaphoreParsing;

        public LoaderState CurLoaderState { get; set; }

        // DEBUG, BENCHMARK
        public System.Diagnostics.Stopwatch stopWatchDownloadSpeed = new System.Diagnostics.Stopwatch();
        public System.Diagnostics.Stopwatch stopWatchProcess = new System.Diagnostics.Stopwatch();
        public Dictionary<AssetType, Dictionary<string, float>> dictTypeToURLToTimeLoad = new();
        public Dictionary<string, float> dictURLToTimeWaitAsync = new Dictionary<string, float>();
        public Dictionary<AssetType, BenchmarkResource> dictTypeToTimeLoad = new Dictionary<AssetType, BenchmarkResource>();

        public class BenchmarkResource
        {
            public int count;
            public float total;
            public long byteCount;

            public BenchmarkResource(int count, float total)
            {
                this.count = count;
                this.total = total;
                this.byteCount = 0;
            }
        }

        public long numByteDownloaded = 0;
        public float timeStartDownload = 0.0f;

        protected void Awake()
        {
            maxThreads = Mathf.Min(SystemInfo.processorCount * 2, 16);  // Cap at 16 threads
            loaderFactory = new LoaderFactory();
            downloadHandler = new DownloadHandler();
            semaphoreDownload = new SemaphoreSlim(maxThreads); // You can set maxThreads based on your needs
            semaphoreParsing = new SemaphoreSlim(MAX_PARSING_TASKS);
            CurLoaderState = LoaderState.Balance;
            foreach (AssetType type in Enum.GetValues(typeof(AssetType)))
            {
                dictTypeToTimeLoad.Add(type, new BenchmarkResource(0, 0.0f));
                dictTypeToURLToTimeLoad.Add(type, new Dictionary<string, float>());
            }

        }

        protected void OnDestroy()
        {
            downloadHandler.Dispose();
        }

        public async void Update()
        {
            if (!requestQueue.IsEmpty)
            {
                if (CurLoaderState == LoaderState.Balance)
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
            if (!cacheAsset.ContainsKey(AssetGroupId))
            {
                cacheAsset[AssetGroupId] = new Dictionary<string, object>();
            }
        }


        public async UniTask<object> GetResource(AssetType type, string url, AssetProviderConfig config = null, int priority = 0)
        {
            // Check if the resource is already cached
            if (cacheAsset[AssetGroupId].TryGetValue(url, out object cachedResource))
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
            EnqueueRequest(type, url, Callback, config, priority);

            // Return the task, which will complete when TrySetResult is called
            return await completionSource.Task;
        }

        protected void EnqueueRequest(AssetType type, string url, Action<object> callback, AssetProviderConfig config, int priority = 0)
        {
            if (pendingCallbacks.ContainsKey(url))
            {
                pendingCallbacks[url] += callback;
            }
            else
            {
                pendingCallbacks[url] = callback;
            }

            requestQueue.Enqueue(new ResourceRequest(type, url, config, priority));
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
                Debug.LogWarning($"Resource Loader Download Time = {stopWatchDownloadSpeed.ElapsedMilliseconds}");
            }

            // Debug
            await UniTask.SwitchToMainThread();
            FindObjectOfType<TestResourceManager>().OnOperationComplete();
        }

        public void InvokeAllCallbacks(List<string> callbackIds)
        {
            foreach (var id in callbackIds)
            {
                if (pendingCallbacks.ContainsKey(id))
                {
                    var callback = pendingCallbacks[id];
                    if (cacheAsset[AssetGroupId].ContainsKey(id))
                    {
                        callback?.Invoke(cacheAsset[AssetGroupId][id]);
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

        private async UniTask DownloadAndProcessResource(ResourceRequest request)
        {
            var sw = new System.Diagnostics.Stopwatch();

            // Measure semaphore wait time
            sw.Start();
            await semaphoreDownload.WaitAsync(); // Wait for a slot to become available
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
                        if (request.Type != AssetType.Audio)
                        {
                            var response = await downloadHandler.DownloadByteAsync(request.Url);
                            if (response.IsSuccessStatusCode)
                            {
                                var data = await response.Content.ReadAsByteArrayAsync();
                                DataCache assetCache = new DataCache()
                                { Content = data, Type = request.Type, Id = request.Url };
                                //cacheData[AssetGroupId][request.Url] = assetCache;
                                Debug.Log($"Downloaded {request.Url}");
                                dictTypeToURLToTimeLoad[request.Type][request.Url] = sw.ElapsedMilliseconds; // Record download time
                                numByteDownloaded += data.LongLength;
                                UpdateDownloadSpeed(numByteDownloaded);

                                // Process asset async here
                                await ProcessAssetAsync(assetCache, request.Config);
                                InvokeCallback(request.Url);
                                return;
                            }
                        }
                        else if (request.Type == AssetType.Audio)
                        {
                            // For Audio, download and decode at the same time by Unity Web Request *WARNING*
                            var data = await downloadHandler.DownloadAudioClip(request.Url);
                            dictTypeToURLToTimeLoad[request.Type][request.Url] = sw.ElapsedMilliseconds; // Record download time
                            cacheAsset[AssetGroupId][request.Url] = data;
                            InvokeCallback(request.Url);
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
                semaphoreDownload.Release(); // Release the semaphore slot for the next task
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
                if (cacheAsset[AssetGroupId].TryGetValue(url, out object obj))
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
            foreach (var dict in cacheAsset)
            {
                dict.Value.Clear();
            }
            cacheData.Clear();
            cacheAsset.Clear();
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

        public async UniTask ProcessAssetAsync(DataCache data, AssetProviderConfig config)
        {
            System.Diagnostics.Stopwatch swAsync = new();
            (string extension, bool isZipped) = UrlUtils.GetFileExtensionAndZipStatus(data.Id);
            swAsync.Start();
            object obj = null;
            if (data != null)
            {
                if (data.Type == AssetType.Texture2D)
                {
                    if (extension == "webp")
                    {
                        obj = await loaderFactory.LoadAssetWebpAsync(data.Content, config != null ? config as TextureProviderConfig : new TextureProviderConfig(), false, true, isZipped: isZipped);
                    }
                    // TODO handle PNG
                }
                else if (data.Type == AssetType.Json)
                {
                    obj = await loaderFactory.LoadAssetJsonAsync(data.Content, config, isZipped: isZipped);
                }
                else if (data.Type == AssetType.JObject)
                {
                    await semaphoreParsing.WaitAsync();
                    System.Diagnostics.Stopwatch swParsing = new System.Diagnostics.Stopwatch();
                    swParsing.Start();
                    obj = await loaderFactory.LoadAssetJObjectAsync(data.Content, config, isZipped: isZipped);
                    swParsing.Stop();
                    dictTypeToTimeLoad[AssetType.Parsing].total += swParsing.ElapsedMilliseconds;
                    dictTypeToTimeLoad[AssetType.Parsing].count++;
                    dictTypeToTimeLoad[AssetType.Parsing].byteCount += data.Content.LongLength;
                    semaphoreParsing.Release();
                }
                if (obj != null)
                {
                    cacheAsset[AssetGroupId][data.Id] = obj;
                    swAsync.Stop();
                    dictTypeToTimeLoad[data.Type].total += swAsync.ElapsedMilliseconds;
                    dictTypeToTimeLoad[data.Type].count++;
                    dictTypeToTimeLoad[data.Type].byteCount += data.Content.LongLength;
                    Debug.Log($"Processed Data Type {data.Type} ID = {data.Id} time = {swAsync.ElapsedMilliseconds}ms");
                }
                else
                {
                    Debug.Log($"obj is null Processed Data Type {data.Type} ID = {data.Id}");
                }
            }
        }

        public enum AssetType
        {
            Default = 0,
            Texture2D,
            Audio,
            Json,
            Binary,
            JObject,
            Parsing
        }

        public class DataCache
        {
            public string Id;
            public byte[] Content { get; set; }
            public AssetType Type { get; set; }
        }

        public class ResourceRequest : IComparable<ResourceRequest>
        {
            public string Url { get; }
            public AssetType Type { get; }
            public int Priority { get; }

            public AssetProviderConfig Config;

            public ResourceRequest(AssetType type, string url, AssetProviderConfig config, int priority)
            {
                Type = type;
                Url = url;
                Config = config;
                Priority = priority;
            }

            public int CompareTo(ResourceRequest other)
            {
                return other.Priority.CompareTo(Priority);
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
            semaphoreDownload.Dispose();
            semaphoreParsing.Dispose();
            var dictTimeLoadResource = dictTypeToTimeLoad;
            foreach (AssetType type in Enum.GetValues(typeof(AssetType)))
            {
                dictTypeToTimeLoad[type].total = 0.0f;
                dictTypeToTimeLoad[type].count = 0;
                dictTypeToTimeLoad[type].byteCount = 0;
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
}
