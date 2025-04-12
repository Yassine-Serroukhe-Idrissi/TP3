using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class GameHUD : NetworkBehaviour
{
    [SerializeField]
    private Text m_RoleText;

    public override void OnNetworkSpawn()
    {
        if (IsHost)
        {
            m_RoleText.text = "Host";
        }
        else if (IsServer)
        {
            m_RoleText.text = "Server";
        }
        else if (IsClient)
        {
            m_RoleText.text = "Client";
        }
    }

    public void ReturnToMenu()
    {
        NetworkManager.Shutdown();
        SceneManager.LoadScene("StartupScene");
    }
}
