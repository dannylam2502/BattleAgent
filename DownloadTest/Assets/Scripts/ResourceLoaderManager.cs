using UnityEngine;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Net.Http;
using System.Threading;
using System.Collections.Concurrent;
using System.Linq;
using System.Collections;
using Unity.VisualScripting;

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
    private ConcurrentDictionary<string, object> resourceCache = new ConcurrentDictionary<string, object>();
    private ConcurrentDictionary<string, Action<object>> pendingCallbacks = new ConcurrentDictionary<string, Action<object>>();
    private PriorityQueue<ResourceRequest> requestQueue = new PriorityQueue<ResourceRequest>();

    private int maxThreads;
    private int currentThreads = 0;
    private float downloadSpeed = 0f;
    private const int MAX_RETRIES = 3;

    private IDownloader downloader;
    private Dictionary<Type, IResourceProvider> providers;

    private void Awake()
    {
        maxThreads = Mathf.Min(SystemInfo.processorCount * 2, 16);  // Cap at 16 threads
        StartCoroutine(ProcessQueue());
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
    private IEnumerator ProcessQueue()
    {
        while (true)
        {
            UpdateThreadCount();

            List<Task> downloadTasks = new List<Task>();

            while (currentThreads < maxThreads && !requestQueue.IsEmpty)
            {
                ResourceRequest request = requestQueue.Dequeue();
                currentThreads++;
                downloadTasks.Add(DownloadResource(request));
            }

            if (downloadSpeed > 2f && downloadTasks.Count > 0)
            {
                // For fast speeds, wait for all downloads to complete before processing
                yield return new WaitUntil(() => Task.WhenAll(downloadTasks).IsCompleted);
                foreach (var task in downloadTasks)
                {
                    ProcessDownloadedResource(task as Task<(string, object)>);
                }
            }

            yield return new WaitForSeconds(0.1f);
        }
    }
    private async Task DownloadResource(ResourceRequest request)
    {
        for (int retry = 0; retry < MAX_RETRIES; retry++)
        {
            try
            {
                var result = await DownloadAndProcessResource(request.Url, request.Type);
                return;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Download failed (Attempt {retry + 1}/{MAX_RETRIES}): {e.Message}");
                await Task.Delay(Mathf.FloorToInt(1000 * Mathf.Pow(2, retry)));
            }
        }

        Debug.LogError($"Failed to download resource after {MAX_RETRIES} attempts: {request.Url}");
        InvokeCallbacks(request.Url, null);
        currentThreads--;
    }

    private void ProcessDownloadedResource(Task<(string, object)> task)
    {
        if (task.IsCompletedSuccessfully)
        {
            var (url, resource) = task.Result;
            resourceCache[url] = resource;
            InvokeCallbacks(url, resource);
        }
        currentThreads--;
    }

    private async Task<(string, object)> DownloadAndProcessResource(string url, Type type)
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

    private void InvokeCallbacks(string url, object resource)
    {
        if (pendingCallbacks.TryRemove(url, out Action<object> callback))
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