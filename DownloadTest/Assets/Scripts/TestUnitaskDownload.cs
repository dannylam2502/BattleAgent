using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using Cysharp.Threading.Tasks;
using Unity.VisualScripting;
using UnityEngine;

public class TestUniTaskDownload : MonoBehaviour
{
    public string filePath = "AssetLog"; // Path within Resources (exclude the .txt extension)
    private static readonly HttpClient httpClient = new HttpClient();
    private SemaphoreSlim semaphore;
    public UIScript uiScript;
    public int totalSize = 0;

    public string currentLog;

    public Dictionary<string, byte[]> dictURLtoByte = new Dictionary<string, byte[]>();

    public async void OnClickButtonTestDownload()
    {
        totalSize = 0;
        currentLog = string.Empty;
        int maxDownload;

        if (int.TryParse(uiScript.infNumConcurrent.text, out maxDownload))
        {
            semaphore = new SemaphoreSlim(maxDownload);
        }
        else
        {
            maxDownload = CalculateMaxConcurrentDownloads();
            semaphore = new SemaphoreSlim(maxDownload);
        }

        await DownloadFilesAsync();
    }

    void UpdateLog()
    {
        uiScript.log.text = currentLog;
    }

    async UniTask DownloadFilesAsync()
    {
        string[] urls = ReadFileLines(); // Use synchronous file reading from Resources

        if (urls == null || urls.Length == 0)
        {
            Debug.LogError("Failed to load URLs from Resources.");
            currentLog += "Failed to load URLs from Resources.\n";
            UpdateLog();
            return;
        }

        List<UniTask> downloadTasks = new List<UniTask>();
        float totalTime = 0.0f;

        foreach (string url in urls)
        {
            if (string.IsNullOrWhiteSpace(url))
                continue;

            downloadTasks.Add(DownloadFileAsync(url));
        }

        // Wait for all downloads to complete
        float startTime = Time.time;
        await UniTask.WhenAll(downloadTasks);
        float endTime = Time.time;

        totalTime = endTime - startTime;
        Debug.Log($"Total Time taken for all downloads = {totalTime} seconds");
        currentLog += $"Total Time taken for all downloads = {totalTime} seconds\n";
        UpdateLog();

        uiScript.txtTotalTime.text = $"Max Thread = {CalculateMaxConcurrentDownloads()}, {totalTime} for {urls.Length} assets, total {totalSize / 1000}KB";
    }

    string[] ReadFileLines()
    {
        TextAsset textAsset = Resources.Load<TextAsset>(filePath);
        if (textAsset == null)
        {
            Debug.LogError($"Failed to load {filePath} from Resources.");
            return null;
        }

        return textAsset.text.Split(new[] { '\n' }, System.StringSplitOptions.RemoveEmptyEntries);
    }

    async UniTask DownloadFileAsync(string url)
    {
        Uri uri;
        string fileName;

        try
        {
            uri = new Uri(url);
            fileName = Path.GetFileName(uri.LocalPath);
        }
        catch (UriFormatException e)
        {
            Debug.LogError($"Invalid URL format: {url}, Error: {e.Message}");
            return;
        }
        catch (ArgumentException e)
        {
            Debug.LogError($"Invalid file path characters in URL: {url}, Error: {e.Message}");
            return;
        }

        Debug.Log("Downloading: " + fileName);

        await semaphore.WaitAsync(); // Wait until it's safe to proceed

        try
        {
            // Retry mechanism for unstable mobile networks
            for (int i = 0; i < 3; i++)
            {
                try
                {
                    HttpResponseMessage response = await httpClient.GetAsync(url);
                    if (response.IsSuccessStatusCode)
                    {
                        byte[] data = await response.Content.ReadAsByteArrayAsync();
                        // Save the file locally for mobile
                        //string savePath = Path.Combine(Application.persistentDataPath, fileName);
                        //File.WriteAllBytes(savePath, data);
                        dictURLtoByte[url] = data;
                        Debug.Log($"Downloaded {fileName} to {Application.persistentDataPath}");
                        totalSize += data.Length;
                        currentLog += $"Downloaded {fileName}, size: {data.Length / 1000}KB\n";
                        UpdateLog();
                        break; // Exit the retry loop if successful
                    }
                    else
                    {
                        Debug.LogError($"Error downloading {fileName}: {response.ReasonPhrase}");
                        currentLog += $"Error downloading {fileName}: {response.ReasonPhrase}\n";
                        UpdateLog();
                    }
                }
                catch (HttpRequestException e)
                {
                    Debug.LogWarning($"Retrying {fileName} due to network error: {e.Message}");
                    currentLog += $"Retrying {fileName} due to network error: {e.Message}\n";
                    UpdateLog();
                    await UniTask.Delay(1000); // Wait 1 second before retrying
                }
            }
        }
        finally
        {
            semaphore.Release(); // Release the semaphore slot for the next task
        }
    }

    private int CalculateMaxConcurrentDownloads()
    {
        // Simple example based on the number of processors
        int processorCount = SystemInfo.processorCount;

        // Adjust the number of concurrent downloads
        // A common approach might be to use half the number of processors
        int maxConcurrentDownloads = Mathf.Max(1, processorCount * 2);

        // Optionally, you can add further adjustments based on other factors
        // For example, you could lower the number if running on older devices
        return maxConcurrentDownloads;
    }

    private void OnDestroy()
    {
        httpClient.Dispose();
        if (semaphore != null)
        {
            semaphore.Dispose();
        }
    }
}
