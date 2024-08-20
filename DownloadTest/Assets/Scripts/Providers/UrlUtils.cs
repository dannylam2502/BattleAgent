using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public static class UrlUtils
{
    public static string GetExtension(string url)
    {
        int lastQuestionMarkIndex = url.LastIndexOf('?');
        if (lastQuestionMarkIndex == -1)
        {
            return url.Substring(url.LastIndexOf('.') + 1).ToLower();
        }
        else
        {
            return url.Substring(url.LastIndexOf('.') + 1, lastQuestionMarkIndex - url.LastIndexOf('.') - 1).ToLower();
        }
    }
    public static (string extension, bool isZipped) GetFileExtensionAndZipStatus(string url)
    {
        bool isZipped = url.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);

        int questionMarkIndex = url.IndexOf('?');
        int endIndex = (questionMarkIndex == -1) ? url.Length : questionMarkIndex;

        int lastDotIndex = url.LastIndexOf('.', endIndex - 1);
        if (lastDotIndex == -1)
        {
            return (string.Empty, isZipped);
        }

        // Check if the file is zipped and the previous extension
        if (isZipped)
        {
            int secondLastDotIndex = url.LastIndexOf('.', lastDotIndex - 1);
            if (secondLastDotIndex != -1)
            {
                return (url.Substring(secondLastDotIndex + 1, lastDotIndex - secondLastDotIndex - 1).ToLower(), isZipped);
            }
        }

        return (url.Substring(lastDotIndex + 1, endIndex - lastDotIndex - 1).ToLower(), isZipped);
    }
}