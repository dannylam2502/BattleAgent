using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
using WebP;

public class TestDecode : MonoBehaviour
{
    public TestDownload testDownload;
    public RawImage image; // Assign your RawImage component in the inspector
    public UIScript uiScript;
    public Dictionary<string, byte[]> dictPNGData = new Dictionary<string, byte[]>();

    public void OnClickBtnStartDecode()
    {
        if (testDownload != null)
        {
            var data = testDownload.dictURLtoByte;
            if (data != null && data.Count > 0)
            {
                StartCoroutine(TestDecodeWebp(data));
            }
        }
    }

    public IEnumerator TestDecodeWebp(Dictionary<string, byte[]> webpFiles)
    {
        long totalTimeUsingPool = 0;
        long totalTimeWithoutPool = 0;

        foreach (var webpFile in webpFiles)
        {
            string url = webpFile.Key;
            byte[] data = webpFile.Value;

            if (!url.Contains(".webp"))
            {
                continue; // Skip files that do not have a .webp extension
            }

            UnityEngine.Debug.Log($"Testing URL: {url}");

            // Test LoadWebpUsingPool
            Stopwatch stopwatch = new Stopwatch();
            //stopwatch.Start();
            //LoadWebpUsingPool(image, data);
            //stopwatch.Stop();
            //UnityEngine.Debug.Log($"LoadWebpUsingPool Time for {url}: {stopwatch.ElapsedMilliseconds} ms");
            //totalTimeUsingPool += stopwatch.ElapsedMilliseconds;

            //yield return new WaitForEndOfFrame();

            // Test LoadWebp
            stopwatch.Reset();
            stopwatch.Start();
            LoadWebp(image, data);
            stopwatch.Stop();
            UnityEngine.Debug.Log($"LoadWebp Time for {url}: {stopwatch.ElapsedMilliseconds} ms");
            totalTimeWithoutPool += stopwatch.ElapsedMilliseconds;

            yield return new WaitForEndOfFrame();
        }

        var str = "";
        // Log total time for each method
        //UnityEngine.Debug.Log($"Total Time for LoadWebpUsingPool: {totalTimeUsingPool} ms");
        //str += $"Total Time for LoadWebpUsingPool: {totalTimeUsingPool} ms \n";
        UnityEngine.Debug.Log($"Total Time for LoadWebp: {totalTimeWithoutPool} ms");
        str += $"Total Time for LoadWebp: {totalTimeWithoutPool} ms";
        uiScript.decodeLog.text = str;
    }

    void LoadWebpUsingPool(RawImage image, byte[] webpBytes)
    {
        byte[] bytePool = new byte[4096 * 2048 * 10];

        Texture2DExt.GetWebPDimensions(webpBytes, out int width, out int height);

        Texture2D texture = Texture2DExt.CreateWebpTexture2D(width, height, isUseMipmap: true, isLinear: false);
        image.texture = texture;

        int numBytesRequired = Texture2DExt.GetRequireByteSize(width, height, isUseMipmap: true);

        if (bytePool.Length < numBytesRequired)
        {
            //UnityEngine.Debug.Assert(bytePool.Length >= numBytesRequired);
            UnityEngine.Debug.LogError($"BYTEPOOL < numBytesRequired url = {bytePool}");
        }

        Texture2DExt.LoadTexture2DFromWebP(webpBytes, texture, lMipmaps: true, lLinear: true, bytePool, numBytesRequired);
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
            UnityEngine.Debug.LogError("Webp Load Error : " + lError.ToString());
        }
    }

    public void OnClickSavePNG()
    {
        SaveWebPAsPng(testDownload.dictURLtoByte);
    }
    private void SaveWebPAsPng(Dictionary<string, byte[]> webpFiles)
    {
        // Ensure the save directory exists
        string fullPath = Path.Combine(Application.persistentDataPath, "PNGFiles");
        if (!Directory.Exists(fullPath))
        {
            Directory.CreateDirectory(fullPath);
        }

        int count = 0;
        foreach (var webpFile in webpFiles)
        {
            string url = webpFile.Key;
            byte[] data = webpFile.Value;

            if (!url.Contains(".webp"))
            {
                continue; // Skip files that do not have a .webp extension
            }
            count++;

            // Decode the WebP file to a Texture2D
            Texture2D texture = Texture2DExt.CreateTexture2DFromWebP(data, lMipmaps: false, lLinear: true, lError: out Error lError, makeNoLongerReadable: false);

            if (lError == Error.Success)
            {
                // Convert the texture to a PNG byte array
                byte[] pngData = texture.EncodeToPNG();

                // Generate a valid file name from the URL
                string fileName = $"{count}.png";

                // Save the PNG to the specified directory
                //string filePath = Path.Combine(fullPath, fileName);
                //File.WriteAllBytes(filePath, pngData);

                //UnityEngine.Debug.Log($"Saved {fileName} as PNG at {filePath}");
                dictPNGData.Add($"{count}", pngData);
            }
            else
            {
                UnityEngine.Debug.LogError($"Failed to decode WebP from {url}. Error: {lError}");
            }
        }
    }
}
