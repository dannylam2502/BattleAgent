using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class AssetProviderConfig
{
    // True by default
    public bool IsOptimized = true;

    // It is called before optimized
    public Action<Texture2D> OnProcessTexture;

    public bool DownloadWithAlpha = true;

    public int HorizontalImages;
    public int VerticalImages;
    public int HorizontalOffset;
    public int VerticalOffset;
}
