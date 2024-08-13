using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using WebP;

public class FastDownloadAndDecode : MonoBehaviour
{
    public string filePath = "AssetLog"; // Path within Resources (exclude the .txt extension)
    private static readonly HttpClient httpClient = new HttpClient();
    private SemaphoreSlim downloadSemaphore;
    private SemaphoreSlim decodeSemaphore;
    public UIScript uiScript;
    public RawImage image;

    private int totalSize = 0;
    private string currentLog;

    public int maxDownloadWorkers = 16;  // Adjust based on your needs
    public int maxDecodeWorkers = 16;    // Adjust based on your needs

    private ConcurrentQueue<(string url, byte[] data)> downloadedDataQueue = new ConcurrentQueue<(string url, byte[] data)>();
    private List<Task> downloadTasks = new List<Task>();
    private List<Task> decodeTasks = new List<Task>();

    private float downloadStartTime;
    private float downloadEndTime;
    private float decodeStartTime;
    private float decodeEndTime;

    void Start()
    {
        downloadSemaphore = new SemaphoreSlim(maxDownloadWorkers);
        decodeSemaphore = new SemaphoreSlim(maxDecodeWorkers);
    }

    public async void OnClickButtonFastDownloadAndDecode()
    {
        totalSize = 0;
        currentLog = string.Empty;

        string[] urls = ReadFileLines(); // Use synchronous file reading from Resources

        if (urls == null || urls.Length == 0)
        {
            UnityEngine.Debug.LogError("Failed to load URLs from Resources.");
            currentLog += "Failed to load URLs from Resources.\n";
            UpdateLog();
            return;
        }

        Stopwatch stopwatch = new Stopwatch();
        stopwatch.Start();
        downloadStartTime = 0;
        downloadEndTime = 0;
        decodeStartTime = 0;
        decodeEndTime = 0;

        foreach (string url in urls)
        {
            if (string.IsNullOrWhiteSpace(url))
                continue;

            downloadTasks.Add(DownloadAndQueueAsync(url));
        }

        // Wait for all downloads to complete
        await Task.WhenAll(downloadTasks);

        // Wait for all decodes to complete
        await Task.WhenAll(decodeTasks);
        stopwatch.Stop();

        // Calculate and log the total time for downloading and decoding
        float totalDownloadTime = downloadEndTime - downloadStartTime;
        float totalDecodeTime = decodeEndTime - decodeStartTime;

        UnityEngine.Debug.Log($"Total Time taken for downloads = {totalDownloadTime} seconds");
        UnityEngine.Debug.Log($"Total Time taken for decodes = {totalDecodeTime} seconds");

        currentLog += $"Total Time taken for downloads = {totalDownloadTime} seconds\n";
        currentLog += $"Total Time taken for decodes = {totalDecodeTime} seconds\n";
        UpdateLog();

        // Update the UI with the total time and size
        uiScript.txtTotalTime.text = $"Downloaded {urls.Length} assets, total {totalSize / 1000}KB, time = {stopwatch.ElapsedMilliseconds}";
    }

    async Task DownloadAndQueueAsync(string url)
    {
        await downloadSemaphore.WaitAsync();  // Control the number of concurrent downloads

        if (downloadStartTime == 0)
        {
            downloadStartTime = Time.time;  // Record the start time of the first download
        }

        try
        {
            for (int i = 0; i < 3; i++)
            {
                try
                {
                    HttpResponseMessage response = await httpClient.GetAsync(url);
                    if (response.IsSuccessStatusCode)
                    {
                        byte[] data = await response.Content.ReadAsByteArrayAsync();
                        downloadedDataQueue.Enqueue((url, data));
                        totalSize += data.Length;
                        currentLog += $"Downloaded {Path.GetFileName(url)}, size: {data.Length / 1000}KB\n";
                        UpdateLog();

                        decodeTasks.Add(DecodeAsync(url, data));  // Start decoding as soon as downloading is done
                        break;
                    }
                    else
                    {
                        UnityEngine.Debug.LogError($"Error downloading {Path.GetFileName(url)}: {response.ReasonPhrase}");
                    }
                }
                catch (HttpRequestException e)
                {
                    UnityEngine.Debug.LogWarning($"Retrying {Path.GetFileName(url)} due to network error: {e.Message}");
                    await Task.Delay(1000);  // Wait 1 second before retrying
                }
            }
        }
        finally
        {
            downloadSemaphore.Release();

            // Record the end time of the download process
            if (downloadTasks.TrueForAll(t => t.IsCompleted))
            {
                downloadEndTime = Time.time;
            }
        }
    }

    async Task DecodeAsync(string url, byte[] data)
    {
        await decodeSemaphore.WaitAsync();  // Control the number of concurrent decodes

        if (decodeStartTime == 0)
        {
            decodeStartTime = Time.time;  // Record the start time of the first decode
        }

        try
        {
            if (url.Contains(".webp"))
            {
                LoadWebp(image, data);
            }

            currentLog += $"Decoded {Path.GetFileName(url)}\n";
            UpdateLog();
        }
        finally
        {
            decodeSemaphore.Release();

            // Record the end time of the decode process
            if (decodeTasks.TrueForAll(t => t.IsCompleted))
            {
                decodeEndTime = Time.time;
            }
        }
    }

    void LoadWebp(RawImage image, byte[] webpBytes)
    {
        Texture2D texture = Texture2DExt.CreateTexture2DFromWebP(webpBytes, lMipmaps: true, lLinear: false, lError: out Error lError);

        if (lError == Error.Success)
        {
            image.texture = texture;
        }
        else
        {
            UnityEngine.Debug.LogError("Webp Load Error: " + lError.ToString());
        }
    }

    void UpdateLog()
    {
        uiScript.log.text = currentLog;
    }

    string[] ReadFileLines()
    {
        TextAsset textAsset = Resources.Load<TextAsset>(filePath);
        if (textAsset == null)
        {
            UnityEngine.Debug.LogError($"Failed to load {filePath} from Resources.");
            return null;
        }

        return textAsset.text.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
    }

    private void OnDestroy()
    {
        httpClient.Dispose();
        downloadSemaphore?.Dispose();
        decodeSemaphore?.Dispose();
    }
}
