using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using WebP;
using static WebP.Texture2DExt;

public class WebpLoader
{
    public Texture2D LoadResource(byte[] data, bool lMipmaps, bool lLinear, out Error lError, ScalingFunction scalingFunction = null, bool makeNoLongerReadable = true)
    {
        var tex = WebP.Texture2DExt.CreateTexture2DFromWebP(data, lMipmaps, lLinear, out lError, scalingFunction, makeNoLongerReadable);
        if (lError == WebP.Error.Success)
        {
            return tex;
        }
        return null;
    }

    public Task<object> LoadResourceAsync(byte[] data)
    {
        throw new System.NotImplementedException();
    }
}
