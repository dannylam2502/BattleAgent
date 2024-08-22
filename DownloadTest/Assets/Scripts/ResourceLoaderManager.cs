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
        // The maximum number of tasks for parsing json to JObject
        public const int MAX_PARSING_TASKS = 5;
        public const int MAX_RETRIES = 3;

        // In MB
        // GroupID -> AssetID -> Asset
        private CancellationTokenSource cancellationTokenSource;
        private Dictionary<string, Dictionary<string, AssetCache>> cacheAsset = new();
        private Dictionary<string, Action<AssetCache>> pendingCallbacks = new Dictionary<string, Action<AssetCache>>();
        private PriorityQueue<ResourceRequest> requestQueue = new PriorityQueue<ResourceRequest>();
        public LoaderFactory loaderFactory;
        protected DownloadHandler downloadHandler;

        // The Game ID, may determine which game ID/Assets to keep/unload
        public string AssetGroupId { get; private set; }

        public int maxThreads;
        public float downloadSpeed = 0f;

        public SemaphoreSlim semaphoreDownload;
        public SemaphoreSlim semaphoreParsing;

        // A flag to know when all tasks are completed
        public bool IsAllTasksCompleted { get; private set; }

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
            cancellationTokenSource = new CancellationTokenSource();
            maxThreads = SystemInfo.processorCount * 2; // Cap at 2*NumCores
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

        protected async void OnDestroy()
        {
            cancellationTokenSource.Cancel(); // Cancel all tasks
            await UniTask.Delay(100); // Short delay to allow tasks to cancel
            cancellationTokenSource.Dispose(); // Clean up the cancellation token source
            ReleaseAllAssetExclude();
            downloadHandler.Dispose();
        }

        private void OnApplicationQuit()
        {
            cancellationTokenSource.Cancel(); // Trigger cancellation on quit
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
            if (!cacheAsset.ContainsKey(AssetGroupId))
            {
                cacheAsset[AssetGroupId] = new Dictionary<string, AssetCache>();
            }
        }

        public async UniTask<T> GetResourceAsync<T>(AssetType type, string url, GameObject requester, AssetProviderConfig config = null, int priority = 0)
        {
            // Check if the resource is already cached
            if (cacheAsset[AssetGroupId].TryGetValue(url, out AssetCache asset))
            {
                if (requester != null)
                {
                    // Add Requester to owner for autorelease
                    asset.Owners.Add(requester);
                    //AttachOwnerTracker(requester, asset); // attach the owner tracker for auto release
                }
                return (T)asset.Asset; // If it's cached, return immediately without creating a new task
            }

            // Create a UniTaskCompletionSource to manage task completion manually
            var completionSource = new UniTaskCompletionSource<T>();

            // Define a callback to be called when the resource is loaded
            void Callback(AssetCache asset)
            {
                if (asset != null && requester != null)
                {
                    // Add Requester to owner for autorelease
                    asset.Owners.Add(requester);
                    //AttachOwnerTracker(requester, asset); // attach the owner tracker for auto release
                }
                else
                {
                    Debug.LogError("Sending null requester");
                }
                // When the resource is ready, complete the task with the resource as the result
                completionSource.TrySetResult((T)asset.Asset);
            }

            // Start the resource loading process, passing in the callback
            EnqueueRequest(type, url, Callback, config, priority);

            // Return the task, which will complete when TrySetResult is called
            return await completionSource.Task;
        }

        protected void EnqueueRequest(AssetType type, string url, Action<AssetCache> callback, AssetProviderConfig config, int priority = 0)
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
            Debug.Log("Resource Loader Start Process Request!");
            IsAllTasksCompleted = false;
            timeStartDownload = Time.realtimeSinceStartup;
            numByteDownloaded = 0;
            List<UniTask> tasks = new List<UniTask>();

            while (!requestQueue.IsEmpty)
            {
                ResourceRequest request = requestQueue.Dequeue();

                // Start the download immediately and add the task to the list
                var cancellationToken = cancellationTokenSource.Token;
                UniTask downloadTask = DownloadAndProcessResource(request, cancellationToken);
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
            // We may have more tasks to come in during the load
            IsAllTasksCompleted = requestQueue.IsEmpty;
            FindObjectOfType<TestResourceManager>().OnOperationComplete();
        }

        private async UniTask DownloadAndProcessResource(ResourceRequest request, CancellationToken cancellationToken)
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
                            if (cancellationToken.IsCancellationRequested)
                            {
                                Debug.Log("Download cancelled");
                                return;
                            }
                            if (response.IsSuccessStatusCode)
                            {
                                var data = await response.Content.ReadAsByteArrayAsync();
                                Debug.Log($"Downloaded {request.Url}");
                                dictTypeToURLToTimeLoad[request.Type][request.Url] = sw.ElapsedMilliseconds; // Record download time
                                numByteDownloaded += data.LongLength;
                                UpdateDownloadSpeed(numByteDownloaded);

                                // Process asset async here
                                await ProcessAssetAsync(request, data, request.Config, cancellationToken);
                                // Switch to main thread to void Race Condition
                                //await UniTask.SwitchToMainThread();
                                InvokeCallback(request.Url);
                                return;
                            }
                        }
                        else if (request.Type == AssetType.Audio)
                        {
                            // For Audio, download and decode at the same time by Unity Web Request *WARNING*
                            var audioClip = await downloadHandler.DownloadAudioClip(request.Url);
                            if (cancellationToken.IsCancellationRequested)
                            {
                                Debug.Log("Download cancelled");
                                return;
                            }
                            dictTypeToURLToTimeLoad[request.Type][request.Url] = sw.ElapsedMilliseconds; // Record download time
                            AddAssetToCache(request, audioClip);
                            // Switch to main thread to void Race Condition
                            await UniTask.SwitchToMainThread();
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
            if (pendingCallbacks.TryGetValue(url, out Action<AssetCache> callback))
            {
                if (cacheAsset[AssetGroupId].TryGetValue(url, out AssetCache asset))
                {
                    callback?.Invoke(asset);
                }
            }
            pendingCallbacks.Remove(url);
        }

        // DANNY TODO the old code controls if tasks should be aborts or reschedule
        // Not sure if the current release flow is well, need more test
        public void ReleaseAssetGroup(string assetGroupId)
        {
            if (cacheAsset.ContainsKey(assetGroupId))
            {
                cacheAsset[assetGroupId].Clear();
                //cacheAsset.Remove(assetGroupId);
                Resources.UnloadUnusedAssets();
            }
        }

        // DANNY TODO the old code controls if tasks should be aborts or reschedule
        public void ReleaseAllAssetExclude(string gameId = "")
        {
            List<string> keyToRemoves = new List<string>();
            foreach (var dict in cacheAsset)
            {
                if (dict.Key == gameId)
                {
                    continue;
                }
                dict.Value.Clear();
                keyToRemoves.Add(dict.Key);
            }
            foreach (var keyToRemove in keyToRemoves)
            {
                cacheAsset.Remove(keyToRemove);
            }
            Resources.UnloadUnusedAssets();
            //resourceUsers.Clear();
        }

        public async UniTask ProcessAssetAsync(ResourceRequest request, byte[] byteContent, AssetProviderConfig config, CancellationToken cancellationToken)
        {
            // Check for cancellation before processing
            if (cancellationToken.IsCancellationRequested)
            {
                Debug.Log("Asset processing was cancelled.");
                return;
            }
            System.Diagnostics.Stopwatch swAsync = new();
            (string extension, bool isZipped) = UrlUtils.GetFileExtensionAndZipStatus(request.Url);
            swAsync.Start();
            object obj = null;
            if (request != null)
            {
                if (request.Type == AssetType.Texture2D
                    || request.Type == AssetType.Sprite
                    || request.Type == AssetType.SpriteWithColliderPath
                    || request.Type == AssetType.SpriteGridFrame)
                {
                    if (extension == "webp")
                    {
                        obj = await loaderFactory.LoadAssetWebpAsync(byteContent, config, false, true, isZipped: isZipped);
                    }
                    // TODO handle PNG
                }
                else if (request.Type == AssetType.Json)
                {
                    obj = await loaderFactory.LoadAssetJsonAsync(byteContent, config, isZipped: isZipped);
                }
                else if (request.Type == AssetType.JObject)
                {
                    await semaphoreParsing.WaitAsync();
                    //System.Diagnostics.Stopwatch swParsing = new System.Diagnostics.Stopwatch();
                    //swParsing.Start();
                    obj = await loaderFactory.LoadAssetJObjectAsync(byteContent, config, isZipped: isZipped);
                    //swParsing.Stop();
                    //dictTypeToTimeLoad[AssetType.Parsing].total += swParsing.ElapsedMilliseconds;
                    //dictTypeToTimeLoad[AssetType.Parsing].count++;
                    //dictTypeToTimeLoad[AssetType.Parsing].byteCount += data.Content.LongLength;
                    semaphoreParsing.Release();
                }
                if (obj != null)
                {
                    // Convert to the type we want after download
                    if (request.Type == AssetType.Sprite)
                    {
                        obj = loaderFactory.GetSpriteFromTexture(obj as Texture2D, config);
                    }
                    else if (request.Type == AssetType.SpriteWithColliderPath)
                    {
                        obj = loaderFactory.GetSpriteWithColliderPath(obj as Texture2D, config);
                    }
                    else if (request.Type == AssetType.SpriteGridFrame)
                    {
                        obj = loaderFactory.GetSpriteGridFrame(obj as Texture2D, config);
                    }
                    // Important
                    AddAssetToCache(request, obj);
                    swAsync.Stop();
                    dictTypeToTimeLoad[request.Type].total += swAsync.ElapsedMilliseconds;
                    dictTypeToTimeLoad[request.Type].count++;
                    dictTypeToTimeLoad[request.Type].byteCount += byteContent.LongLength;
                    Debug.Log($"Processed Data Type {request.Type} ID = {request.Url} time = {swAsync.ElapsedMilliseconds}ms");
                }
                else
                {
                    Debug.Log($"obj is null Processed Data Type {request.Type} ID = {request.Url}");
                }
            }
        }

        public void AddAssetToCache(ResourceRequest request, object asset)
        {
            var newAsset = new AssetCache()
            {
                Id = request.Url,
                Type = request.Type,
                Asset = asset,
                Owners = new HashSet<GameObject>()
            };
            cacheAsset[AssetGroupId][newAsset.Id] = newAsset;
        }

        private void AttachOwnerTracker(GameObject owner, AssetCache assetCache)
        {
            AssetOwnerTracker tracker = owner.GetComponent<AssetOwnerTracker>();
            if (tracker == null)
            {
                tracker = owner.AddComponent<AssetOwnerTracker>();
            }

            tracker.OnOwnerDestroyed += (destroyedOwner) =>
            {
                assetCache.Owners.Remove(destroyedOwner);

                // If no more owners, release the asset
                if (assetCache.Owners.Count == 0)
                {
                    ReleaseAsset(assetCache.Id);
                }
            };
        }

        public void ReleaseAsset(string assetId)
        {
            if (cacheAsset.ContainsKey(AssetGroupId))
            {
                if (cacheAsset[AssetGroupId].TryGetValue(assetId, out AssetCache assetCache))
                {
                    // Remove the asset from cache
                    cacheAsset[AssetGroupId].Remove(assetId);

                    // Unload the asset if necessary
                    // SpriteWithColliderPath is special, it's nested inside
                    if (assetCache.Type == AssetType.SpriteWithColliderPath)
                    {
                        var spriteWithCollider = assetCache.Asset as SpriteWithColliderPath;
                        if (spriteWithCollider != null)
                        {
                            Destroy(spriteWithCollider.Sprite);
                            spriteWithCollider.Paths.Clear();
                        }
                    }
                    else
                    {
                        Destroy(assetCache.Asset as UnityEngine.GameObject);
                    }

                    Debug.Log($"Asset with ID {assetId} has been released.");
                }
            }
        }

        // Testing
        public void ResetForNextTest()
        {
            ReleaseAllAssetExclude();
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

        // DEFINE & CONSTANTS

        public enum AssetType
        {
            Default = 0,
            Texture2D,
            Audio,
            Json,
            JObject,
            Sprite,
            SpriteWithColliderPath,
            SpriteGridFrame
        }

        public class AssetCache
        {
            public string Id;
            public object Asset;
            public AssetType Type;
            public HashSet<GameObject> Owners;
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
}
