using Cysharp.Threading.Tasks;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Net.Http;
using UnityEngine;
using UnityEngine.Android;
using UnityEngine.Networking;

public class DownloadHandler
{
    private HttpClient httpClient;

    public DownloadHandler()
    {
        httpClient = new HttpClient();
    }

    public void Dispose()
    {
        httpClient?.Dispose();
    }

    public async UniTask<HttpResponseMessage> DownloadByteAsync(string url)
    {
        return await httpClient.GetAsync(url);
    }


    public async UniTask<AudioClip> DownloadAudioClip(string url)
    {
        var inferredAudioType = GetAudioTypeFromUrl(url);
        if (inferredAudioType == AudioType.UNKNOWN)
        {
            Debug.LogError("Audio resource loader: Unknown audio type for url: " + url);
            return null;
        }

        var dh = new DownloadHandlerAudioClip(url, inferredAudioType)
        {
            compressed = true // Greatly reduces audio memory usage
        };

        using UnityWebRequest wr = new UnityWebRequest(url, "GET", dh, null);
        try
        {
            await wr.SendWebRequest();
            return dh.audioClip;
        }
        catch (UnityWebRequestException)
        {
            Debug.LogError("Error while trying to download audio clip: " + wr.error);
        }
        return null;
    }

    private AudioType GetAudioTypeFromUrl(string url)
    {
        int index = url.IndexOf("?", StringComparison.InvariantCulture);
        string s3Path = (index > 0) ? url.Substring(0, index) : url;

        // Get the last characters of url up to .
        index = url.LastIndexOf(".", StringComparison.InvariantCulture);
        string extension = (index > 0) ? s3Path.Substring(index) : s3Path;

        AudioType audioType = AudioType.UNKNOWN;

        if (extension.ToLower() == ".mp3")
        {
            audioType = AudioType.MPEG;
        }
        else if (extension.ToLower() == ".wav")
        {
            audioType = AudioType.WAV;
        }

        return audioType;
    }
}
