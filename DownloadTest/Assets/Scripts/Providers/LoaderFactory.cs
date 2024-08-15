using System.Collections;
using System.Collections.Generic;
using System.IO.Compression;
using System.IO;
using UnityEngine;
using WebP;
using static WebP.Texture2DExt;
using System.Text;
using Cysharp.Threading.Tasks;

public class LoaderFactory
{
    public Texture2D LoadResourceWebp(byte[] data, bool lMipmaps, bool lLinear, ScalingFunction scalingFunction = null, bool makeNoLongerReadable = true)
    {
        var tex = WebP.Texture2DExt.CreateTexture2DFromWebP(data, lMipmaps, lLinear, out Error lError, scalingFunction, makeNoLongerReadable);
        if (lError == WebP.Error.Success)
        {
            return tex;
        }
        return null;
    }

    public async UniTask<Texture2D> LoadResourceWebpAsync(byte[] data, bool lMipmaps, bool lLinear, ScalingFunction scalingFunction = null, bool makeNoLongerReadable = true)
    {
        await UniTask.SwitchToMainThread();
        var tex = WebP.Texture2DExt.CreateTexture2DFromWebP(data, lMipmaps, lLinear, out Error lError, scalingFunction, makeNoLongerReadable);
        if (lError == WebP.Error.Success)
        {
            return tex;
        }
        return null;
    }

    // come with zip
    public string LoadResourceJson(byte[] bytes)
    {
        using var data = new MemoryStream(bytes);
        using var zip = new ZipArchive(data);
        foreach (ZipArchiveEntry entry in zip.Entries)
        {
            if (UrlUtils.GetExtension(entry.FullName) != "json") continue;

            using var str = entry.Open();
            // str.Position = 0;
            using var reader = new StreamReader(str, Encoding.UTF8);
            return reader.ReadToEnd();
        }
        return null;
    }

    public async UniTask<string> LoadResourceJsonAsync(byte[] bytes)
    {
        using var data = new MemoryStream(bytes);
        using var zip = new ZipArchive(data);
        foreach (ZipArchiveEntry entry in zip.Entries)
        {
            if (UrlUtils.GetExtension(entry.FullName) != "json") continue;

            using var str = entry.Open();
            // str.Position = 0;
            using var reader = new StreamReader(str, Encoding.UTF8);
            return await reader.ReadToEndAsync();
        }
        return null;
    }
}
