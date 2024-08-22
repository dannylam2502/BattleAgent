using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using UnityEngine.SceneManagement;
using Astro.Engine;

public class UIScript : MonoBehaviour
{
    public TMP_InputField infNumConcurrent;
    public TMP_InputField infNumLimit;
    public TMP_Text txtTotalTime;
    public TMP_Text log;
    public TMP_Text decodeLog;
    public TMP_Text PNGLog;
    public TMP_Text txtSwitchMode;

    // Start is called before the first frame update
    void Start()
    {
        txtSwitchMode.text = $"{ResourceLoaderManager.Instance.CurLoaderState}";
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    public void LoadFastScene()
    {
        SceneManager.LoadScene(0);
    }

    public void LoadDownloadScene()
    {
        SceneManager.LoadScene(1);
    }

    public void OnClickSwitchMode()
    {
        ResourceLoaderManager.Instance.CurLoaderState = ResourceLoaderManager.LoaderState.Balance;
        txtSwitchMode.text = $"{ResourceLoaderManager.Instance.CurLoaderState}";
    }
}
