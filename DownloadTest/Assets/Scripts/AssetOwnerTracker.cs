using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class AssetOwnerTracker : MonoBehaviour
{
    public event Action<GameObject> OnOwnerDestroyed;

    private void OnDestroy()
    {
        OnOwnerDestroyed?.Invoke(gameObject);
    }
}
