using System.Threading.Tasks;
using UnityEngine;

public class PngProvider : IResourceProvider
{
    public async Task<object> ProcessResource(byte[] data, string url)
    {
        Texture2D texture = new Texture2D(2, 2);
        await Task.Run(() => texture.LoadImage(data));
        return texture;
    }
}