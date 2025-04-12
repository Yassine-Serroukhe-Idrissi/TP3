using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

public class NetworkManagerHUD : MonoBehaviour
{
    public NetworkManager manager;

    private void Start()
    {
        manager.OnServerStarted += OnServerStarted;
    }

    private void OnDestroy()
    {
        manager.OnServerStarted -= OnServerStarted;
    }

    public void StartClient()
    {
        manager.StartClient();
    }

    public void StartServer()
    {
        manager.StartServer();
    }

    private void OnServerStarted()
    {
        manager.SceneManager.LoadScene("MainScene", LoadSceneMode.Single);
    }
}

