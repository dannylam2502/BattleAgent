using PimDeWitte.UnityMainThreadDispatcher;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using WebP;

public class TestDownAndLoad : MonoBehaviour
{
    public string filePath = "AssetLog"; // Path within Resources (exclude the .txt extension)
    private static readonly HttpClient httpClient = new HttpClient();
    private SemaphoreSlim semaphore;
    public UIScript uiScript;
    public int totalSize = 0;

    public string currentLog;

    public Dictionary<string, byte[]> dictURLtoByte = new Dictionary<string, byte[]>();

    public RawImage image;

    public async void OnClickButtonTestDownAndLoad()
    {
        if (!Texture.allowThreadedTextureCreation)
        {
            Texture.allowThreadedTextureCreation = true;
        }
        totalSize = 0;
        currentLog = string.Empty;
        int maxDownload;

        if (semaphore == null)
        {
            if (int.TryParse(uiScript.infNumConcurrent.text, out maxDownload))
            {
                semaphore = new SemaphoreSlim(maxDownload);
            }
            else
            {
                maxDownload = CalculateMaxConcurrentDownloads();
                semaphore = new SemaphoreSlim(maxDownload);
            }
        }

        await DownloadFilesAsync();
    }

    void UpdateLog()
    {
        uiScript.log.text = currentLog;
    }

    async Task DownloadFilesAsync()
    {
        string[] urls = ReadFileLines(); // Use synchronous file reading from Resources

        if (urls == null || urls.Length == 0)
        {
            Debug.LogError("Failed to load URLs from Resources.");
            currentLog += "Failed to load URLs from Resources.\n";
            UpdateLog();
            return;
        }

        List<Task> downloadTasks = new List<Task>();
        float totalTime = 0.0f;

        foreach (string url in urls)
        {
            if (string.IsNullOrWhiteSpace(url))
                continue;

            downloadTasks.Add(DownloadFileAsync(url));
        }

        // Wait for all downloads to complete
        float startTime = Time.time;
        await Task.WhenAll(downloadTasks);
        float endTime = Time.time;

        totalTime = endTime - startTime;
        Debug.Log($"Total Time taken for all downloads = {totalTime} seconds");
        currentLog += $"Total Time taken for all downloads = {totalTime} seconds\n";
        UpdateLog();

        uiScript.txtTotalTime.text = $"{totalTime} for {urls.Length} assets, total {totalSize / 1000}KB";
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

    async Task DownloadFileAsync(string url)
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
                        //dictURLtoByte.Add(url, data);
                        if (url.Contains(".webp"))
                        {
                            //LoadWebp(image, data);
                            UnityMainThreadDispatcher.Instance().Enqueue(LoadWebp(image, data));
                        }
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
                    await Task.Delay(1000); // Wait 1 second before retrying
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
        // A common approach might be to use double the number of processors for I/O bounds operation
        int maxConcurrentDownloads = Mathf.Max(1, processorCount * 2);

        // Optionally, you can add further adjustments based on other factors
        // For example, you could lower the number if running on older devices
        return maxConcurrentDownloads;
    }

    IEnumerator LoadWebp(RawImage image, byte[] webpBytes)
    {
        Texture2D texture = Texture2DExt.CreateTexture2DFromWebP(webpBytes, lMipmaps: true, lLinear: false, lError: out Error lError);

        if (lError == Error.Success)
        {
            image.texture = texture;
        }
        else
        {
            UnityEngine.Debug.LogError("Webp Load Error : " + lError.ToString());
        }
        yield return null;
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
