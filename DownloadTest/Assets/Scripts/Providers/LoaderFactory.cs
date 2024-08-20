using System.IO.Compression;
using System.IO;
using UnityEngine;
using WebP;
using static WebP.Texture2DExt;
using System.Text;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json.Linq;
using System.Data;

namespace Astro.Engine
{

    public class LoaderFactory
    {
        public float timeCompress = 0.0f;
        public async UniTask<Texture2D> LoadAssetWebpAsync(byte[] data, TextureProviderConfig config, bool lMipmaps, bool lLinear, ScalingFunction scalingFunction = null, bool makeNoLongerReadable = true, bool isZipped = false)
        {
            Error lError;
            Texture2DExt.GetWebPDimensions(data, out var lWidth, out var lHeight);
            var lRawData = Texture2DExt.LoadRGBAFromWebP(data, ref lWidth, ref lHeight, false, out lError, null);
            Texture2D loadedTex = null;
            if (lError == WebP.Error.Success)
            {
                await UniTask.SwitchToMainThread();
                if (!config.DownloadWithAlpha)
                {
                    loadedTex = new Texture2D(lWidth, lHeight, TextureFormat.RGB24, lMipmaps, lLinear);
                }
                else
                {
                    loadedTex = Texture2DExt.CreateWebpTexture2D(lWidth, lHeight, lMipmaps, lLinear);

                }
                loadedTex.LoadRawTextureData(lRawData);
                config.OnProcessTexture?.Invoke(loadedTex);
                if (config.IsOptimized)
                {
                    System.Diagnostics.Stopwatch sw = new();
                    sw.Start();
                    OptimizeTexture(loadedTex);
                    sw.Stop();
                    timeCompress += sw.ElapsedMilliseconds;
                }
                else
                {
                    loadedTex.Apply(false, false);
                }
                return loadedTex;
            }
            return null;
        }

        // come with zip
        public string LoadAssetJson(byte[] bytes, bool isZipped = false)
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

        public async UniTask<string> LoadAssetJsonAsync(byte[] bytes, AssetProviderConfig config, bool isZipped = false)
        {
            if (isZipped)
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
            }
            else
            {
                var json = Encoding.UTF8.GetString(bytes);
                if (json != null)
                {
                    return json;
                }
            }
            
            return null;
        }


        // come with json
        public UniTask<JObject> LoadAssetJObject(byte[] bytes, AssetProviderConfig config, bool isZipped = false)
        {
            return UniTask.RunOnThreadPool(() =>
            {
                JObject result = null;
                if (isZipped)
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
                                    if (json != null)
                                    {
                                        result = JObject.Parse(json);
                                        return result;
                                    }
                                }
                            }
                        }
                    }
                }
                else
                {
                    var json = Encoding.UTF8.GetString(bytes);
                    if (json != null)
                    {
                        result = JObject.Parse(json);
                        return result;
                    }
                }

                return null; // Return null if no JSON file was found
            });
        }

        public async UniTask<JObject> LoadAssetJObjectAsync(byte[] bytes, AssetProviderConfig config, bool isZipped = false)
        {
            await UniTask.SwitchToThreadPool();
            JObject result = null;
            if (isZipped)
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
                                if (json != null)
                                {
                                    result = JObject.Parse(json);
                                    return result;
                                }
                            }
                        }
                    }
                }
            }
            else
            {
                var json = Encoding.UTF8.GetString(bytes);
                if (json != null)
                {
                    result = JObject.Parse(json);
                    return result;
                }
            }

            return null; // Return null if no JSON file was found
        }
        private static void OptimizeTexture(Texture2D texture)
        {
            if (texture.width % 4 == 0 && texture.height % 4 == 0)
            {
                texture.Compress(false);
            }

            texture.Apply(false, true);
        }
    }
}
