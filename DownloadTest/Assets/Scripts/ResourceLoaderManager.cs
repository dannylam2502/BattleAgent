using UnityEngine;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Net.Http;
using Cysharp.Threading.Tasks;
using System.Threading;
using System.Collections.Concurrent;
using System.Linq;
using System.Collections;

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
    public const float VERY_FAST_THESH_HOLD = 2.0f;
    public const float FAST_THESH_HOLD = 1.0f;
    private Dictionary<string, object> resourceCache = new Dictionary<string, object>();
    private Dictionary<string, Action<object>> pendingCallbacks = new Dictionary<string, Action<object>>();
    private PriorityQueue<ResourceRequest> requestQueue = new PriorityQueue<ResourceRequest>();

    public int maxThreads;
    private float downloadSpeed = 0f;
    private const int MAX_RETRIES = 3;

    private HttpClient downloader;
    private Dictionary<Type, IResourceProvider> providers;
    public SemaphoreSlim semaphore = new SemaphoreSlim(1);

    // DEBUG, BENCHMARK
    public System.Diagnostics.Stopwatch stopwatch = new System.Diagnostics.Stopwatch();
    public Dictionary<string, float> dictURLToTimeLoad = new Dictionary<string, float>();
    public Dictionary<string, float> dictURLToTimeWaitAsync = new Dictionary<string, float>();
        
    private void Awake()
    {
        //maxThreads = Mathf.Min(SystemInfo.processorCount * 2, 16);  // Cap at 16 threads
        maxThreads = 4;
        downloader = new HttpClient();
        semaphore = new SemaphoreSlim(maxThreads); // You can set maxThreads based on your needs
    }

    private void OnDestroy()
    {
    }

    public void GetResource<T>(string url, Action<object> callback, int priority = 0, object user = null) where T : UnityEngine.Object
    {
        if (resourceCache.TryGetValue(url, out object cachedResource))
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

        requestQueue.Enqueue(new ResourceRequest(url, typeof(T), priority));
    }

    public async UniTask ProcessQueue()
    {
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
            stopwatch.Restart();
            await UniTask.WhenAll(downloadTasks); // Wait for the current batch to complete
            downloadTasks.Clear(); // Clear the list for the next batch
            stopwatch.Stop();
            Debug.LogError($"Resource Loader Time = {stopwatch.ElapsedMilliseconds}");
        }

        InvokeAllCallbacks(pendingCallbacks.Keys.ToList());
    }

    public void InvokeAllCallbacks(List<string> callbackIds)
    {
        foreach (var id in callbackIds)
        {
            if (resourceCache.TryGetValue(id, out object obj))
            {
                long size = 0;
                size = ((byte[]) obj).LongLength;
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
                        resourceCache[request.Url] = await response.Content.ReadAsByteArrayAsync();
                        Debug.Log($"Downloaded {request.Url}");
                        dictURLToTimeLoad[request.Url] = sw.ElapsedMilliseconds; // Record download time
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

    private void UpdateDownloadSpeed(long bytes, float startTime)
    {
        float elapsedTime = Time.realtimeSinceStartup - startTime;
        downloadSpeed = bytes / (elapsedTime * 1024 * 1024); // MB per second
    }

    private void UpdateThreadCount()
    {
        if (downloadSpeed > 2f)
        {
            maxThreads = Mathf.Min(SystemInfo.processorCount * 2, 8);
        }
        else if (downloadSpeed > 1f)
        {
            maxThreads = Mathf.Max(SystemInfo.processorCount - 1, 2);
        }
        else
        {
            maxThreads = 2;
        }
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
        foreach (var resource in resourceCache.Values)
        {
            if (resource is UnityEngine.Object unityObject)
            {
                Destroy(unityObject);
            }
        }
        resourceCache.Clear();
        //resourceUsers.Clear();
    }

    public NetworkCondition GetNetworkCondition()
    {
        if (downloadSpeed >= VERY_FAST_THESH_HOLD)
        {
            return NetworkCondition.VERY_FAST;
        }
        else if (downloadSpeed >= FAST_THESH_HOLD)
        {
            return NetworkCondition.FAST;
        }
        return NetworkCondition.SLOW;
    }

    public enum NetworkCondition
    {
        DEFAULT = 0,
        VERY_FAST = 1,
        FAST = 2,
        SLOW = 3
    }

    public class ResourceRequest : IComparable<ResourceRequest>
    {
        public string Url { get; }
        public Type Type { get; }
        public int Priority { get; }

        public ResourceRequest(string url, Type type, int priority)
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
