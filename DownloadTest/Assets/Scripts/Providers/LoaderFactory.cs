using System.IO.Compression;
using System.IO;
using UnityEngine;
using WebP;
using static WebP.Texture2DExt;
using System.Text;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;

namespace Astro.Engine
{
    public class SpriteWithColliderPath
    {
        public Sprite Sprite;
        public List<List<Vector2>> Paths;
    }
    public class LoaderFactory
    {
        public float timeCompress = 0.0f;
        public async UniTask<Texture2D> LoadAssetWebpAsync(byte[] data, AssetProviderConfig config, bool lMipmaps, bool lLinear, ScalingFunction scalingFunction = null, bool makeNoLongerReadable = true, bool isZipped = false)
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
        public Sprite GetSpriteFromTexture(Texture2D texture, AssetProviderConfig config)
        {
            // We use height, since many of our calculations involve settings things so that their height is a certain
            // value relative to the player.
            float pixelsPerUnitFinal = texture.height;
            Sprite NewSprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height),
                new Vector2(0.5f, 0.5f), pixelsPerUnitFinal);
            return NewSprite;
        }

        private List<List<Vector2>> GetColliderPathForTexture(Texture2D texture)
        {
            List<List<Vector2>> paths = new List<List<Vector2>>();
            if (texture == null)
                return null;

            //_getColliderPathForTextureBenchmark.Begin(texture.name);

            // Find a reasonable width and height for the texture copy we'll used to generate a collider path.
            // It's okay if we lose some detail because the collider path will be simplified by the end anyway.
            int targetWidth = texture.width;
            int targetHeight = texture.height;
            while (targetWidth >= 256 && targetHeight >= 256)
            {
                targetWidth /= 2;
                targetHeight /= 2;
            }

            // https://support.unity.com/hc/en-us/articles/206486626-How-can-I-get-pixels-from-unreadable-textures
            RenderTexture tmp = RenderTexture.GetTemporary(
                                targetWidth,
                                targetHeight,
                                0,
                                RenderTextureFormat.Default,
                                RenderTextureReadWrite.Linear);
            // Blit the pixels on texture to the RenderTexture
            Graphics.Blit(texture, tmp);
            // Backup the currently set RenderTexture
            RenderTexture previous = RenderTexture.active;
            // Set the current RenderTexture to the temporary one we created
            RenderTexture.active = tmp;
            // Create a new readable Texture2D to copy the pixels to it
            Texture2D myTexture2D = new Texture2D(targetWidth, targetHeight);
            // Copy the pixels from the RenderTexture to the new Texture
            myTexture2D.ReadPixels(new Rect(0, 0, tmp.width, tmp.height), 0, 0);
            myTexture2D.Apply();
            // Reset the active RenderTexture
            RenderTexture.active = previous;
            // Release the temporary RenderTexture
            RenderTexture.ReleaseTemporary(tmp);

            // Let Unity generate a physics shape
            Sprite sprite = Sprite.Create(myTexture2D, new Rect(0, 0, myTexture2D.width, myTexture2D.height), new Vector2(0.5f, 0.5f), myTexture2D.height, 0, SpriteMeshType.Tight, Vector4.zero, true);

            // Grab the polygons for the generated physics shapes, and optimize them
            for (int i = 0; i < sprite.GetPhysicsShapeCount(); i++)
            {
                if (sprite.GetPhysicsShapePointCount(i) <= 2)
                    continue;
                List<Vector2> physicsVertsForPath = new List<Vector2>();
                sprite.GetPhysicsShape(i, physicsVertsForPath);

                List<Vector2> optimizedPoints = ShapeOptimizationHelper.DouglasPeuckerReduction(physicsVertsForPath, 0.07f);
                if (optimizedPoints.Count <= 2)
                    continue;

                paths.Add(optimizedPoints);
            }

            //_getColliderPathForTextureBenchmark.LogTime();

            // Return optimized polygon paths
            return paths;
        }

        public SpriteWithColliderPath GetSpriteWithColliderPath(Texture2D texture, AssetProviderConfig config)
        {
            return new SpriteWithColliderPath()
            {
                Sprite = GetSpriteFromTexture(texture, config),
                Paths = GetColliderPathForTexture(texture)
            };
        }

        public Sprite GetSpriteGridFrame(Texture2D tex, AssetProviderConfig config)
        {
            float width = tex.width / (float)config.HorizontalImages;
            float height = tex.height / (float)config.VerticalImages;
            float x = (float)config.HorizontalOffset * width;
            float y = (float)config.VerticalOffset * height;

            return Sprite.Create(tex, new(x, y, width, height), Vector2.one * 0.5f, width / 2);
        }
    }
}
