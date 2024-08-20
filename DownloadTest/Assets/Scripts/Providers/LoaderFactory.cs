using System.IO.Compression;
using System.IO;
using UnityEngine;
using WebP;
using static WebP.Texture2DExt;
using System.Text;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json.Linq;
using System.Data;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Collections;

namespace Astro.Engine
{

    public class LoaderFactory
    {
        //public async UniTask<Texture2D> LoadAssetWebpAsync(byte[] data, TextureProviderConfig config, bool lMipmaps, bool lLinear, ScalingFunction scalingFunction = null, bool makeNoLongerReadable = true, bool isZipped = false)
        //{
        //    await UniTask.SwitchToMainThread();
        //    var tex = WebP.Texture2DExt.CreateTexture2DFromWebP(data, lMipmaps, lLinear, out Error lError, scalingFunction, makeNoLongerReadable);
        //    if (lError == WebP.Error.Success)
        //    {
        //        return tex;
        //    }
        //    return null;
        //}

        public async UniTask<Texture2D> LoadAssetWebpAsync(byte[] data, TextureProviderConfig config, bool lMipmaps, bool lLinear, ScalingFunction scalingFunction = null, bool makeNoLongerReadable = true, bool isZipped = false)
        {
            await UniTask.SwitchToMainThread();

            // Get WebP dimensions
            Error lError = Error.DecodingError;
            Texture2DExt.GetWebPDimensions(data, out var lWidth, out var lHeight);

            // Preallocate buffer for RGBA data
            byte[] lRawDataBuffer = new byte[lWidth * lHeight * 4];
            var lRawData = Texture2DExt.LoadRGBAFromWebP(data, ref lWidth, ref lHeight, false, out lError, scalingFunction);

            if (lError == WebP.Error.Success)
            {
                // Determine the texture format based on whether alpha is needed
                TextureFormat format = config.DownloadWithAlpha ? TextureFormat.RGBA32 : TextureFormat.RGB24;
                Texture2D loadedTex = new Texture2D(lWidth, lHeight, format, lMipmaps, lLinear);

                // Load the raw texture data
                loadedTex.LoadRawTextureData(lRawData);
                loadedTex.Apply(lMipmaps, !config.IsOptimized);

                if (!config.DownloadWithAlpha)
                {
                    // If alpha is not needed, remove the alpha channel using optimized pixel data manipulation
                    var texToDestroy = loadedTex;
                    var tmpTex = new Texture2D(lWidth, lHeight, TextureFormat.RGB24, false, true);

                    // Create a NativeArray from the pointer
                    unsafe
                    {
                        Color32* pixelsPtr = (Color32*)loadedTex.GetRawTextureData<Color32>().GetUnsafeReadOnlyPtr();
                        var pixelCount = lWidth * lHeight;
                        var pixelsArray = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<Color32>(pixelsPtr, pixelCount, Allocator.None);

                        // Set the pixel data using the NativeArray
                        tmpTex.SetPixelData(pixelsArray, 0);
                    }

                    // Destroy the old texture to free up memory
                    UnityEngine.Object.Destroy(texToDestroy);
                    loadedTex = tmpTex;
                }

                // Invoke any custom processing function
                config.OnProcessTexture?.Invoke(loadedTex);

                // Optimize the texture if needed
                if (config.IsOptimized)
                {
                    OptimizeTexture(loadedTex);
                }

                return loadedTex;
            }

            return null; // Return null if WebP decoding fails
        }

        private static void OptimizeTexture(Texture2D texture)
        {
            if (texture.width % 4 == 0 && texture.height % 4 == 0)
            {
                texture.Compress(false);
            }

            texture.Apply(false, true); // Apply changes and make the texture non-readable
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
    }
}
