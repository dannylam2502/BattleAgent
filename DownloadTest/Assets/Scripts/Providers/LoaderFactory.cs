using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using WebP;
using static WebP.Texture2DExt;

public class LoaderFactory
{
    public Texture2D LoadResource(byte[] data, bool lMipmaps, bool lLinear, ScalingFunction scalingFunction = null, bool makeNoLongerReadable = true)
    {
        var tex = WebP.Texture2DExt.CreateTexture2DFromWebP(data, lMipmaps, lLinear, out Error lError, scalingFunction, makeNoLongerReadable);
        if (lError == WebP.Error.Success)
        {
            return tex;
        }
        return null;
    }
}
