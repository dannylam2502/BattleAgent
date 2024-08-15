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
}