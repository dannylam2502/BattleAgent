using System.Collections;
using System.Collections.Generic;
using System.IO.Compression;
using System.IO;
using UnityEngine;
using WebP;
using static WebP.Texture2DExt;
using System.Text;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json.Linq;
using System.Diagnostics;
using UnityEngine.Diagnostics;

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
    public UniTask<string> LoadResourceJson(byte[] bytes)
    {
        return UniTask.RunOnThreadPool(() => Encoding.UTF8.GetString(bytes));
    }

    public UniTask<string> LoadResourceJsonAsync(byte[] bytes)
    {
        return UniTask.RunOnThreadPool(() => Encoding.UTF8.GetString(bytes));
    }


    // come with zip
    public UniTask<JObject> LoadResourceJObject(byte[] bytes)
    {
        return UniTask.RunOnThreadPool(() =>
        {
            using (var data = new MemoryStream(bytes, false))
            using (var zip = new ZipArchive(data, ZipArchiveMode.Read, false))
            {
                foreach (ZipArchiveEntry entry in zip.Entries)
                {
                    if (UrlUtils.GetExtension(entry.FullName) != "json") continue;

                    using (var str = entry.Open())
                    {
                        // Optimize by using a larger buffer for reading
                        using (var reader = new StreamReader(str, Encoding.UTF8, false, 4096, false))
                        {
                            var json = reader.ReadToEnd();
                            return JObject.Parse(json);
                        }
                    }
                }
            }

            return null; // Return null if no JSON file was found
        });
    }


    public UniTask<JObject> LoadResourceJObjectAsync(byte[] bytes)
    {
        return UniTask.RunOnThreadPool(() =>
        {
            using (var data = new MemoryStream(bytes, false))
            using (var zip = new ZipArchive(data, ZipArchiveMode.Read, false))
            {
                foreach (ZipArchiveEntry entry in zip.Entries)
                {
                    if (UrlUtils.GetExtension(entry.FullName) != "json") continue;

                    using (var str = entry.Open())
                    {
                        // Optimize by using a larger buffer for reading
                        using (var reader = new StreamReader(str, Encoding.UTF8, false, 4096, false))
                        {
                            var json = reader.ReadToEnd();
                            return JObject.Parse(json);
                        }
                    }
                }
            }

            return null; // Return null if no JSON file was found
        });
    }

}
