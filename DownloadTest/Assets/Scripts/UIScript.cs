using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using UnityEngine.SceneManagement;

public class UIScript : MonoBehaviour
{
    public TMP_InputField infNumConcurrent;
    public TMP_Text txtTotalTime;
    public TMP_Text log;
    public TMP_Text decodeLog;
    public TMP_Text PNGLog;
    // Start is called before the first frame update
    void Start()
    {
        
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
}
