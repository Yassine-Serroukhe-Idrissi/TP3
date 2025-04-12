using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class PlayerGhost : NetworkBehaviour
{
    [SerializeField] 
    private Player m_Player;
    [SerializeField] 
    private SpriteRenderer m_SpriteRenderer;

    public override void OnNetworkSpawn()
    {
        // L'entite qui appartient au client est recoloriee en rouge
        if (IsOwner)
        {
            m_SpriteRenderer.color = Color.red;
        }
    }

    private void Update()
    {
        if (IsOwner)
        {
            // Affichage de la position prédite pour une réactivité maximale
            transform.position = m_Player.PredictedPosition;
        }
        else
        {
            // Pour les autres, utiliser la position autoritaire synchronisée
            transform.position = m_Player.Position;
        }
    }
}
