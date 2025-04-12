using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class CircleSpawner : NetworkBehaviour
{
    [SerializeField]
    private int m_NbCircles = 10;

    [SerializeField]
    private float m_MaxInitialDistance = 100;

    [SerializeField]
    private float m_MaxInitialVelocity = 20;

    [SerializeField]
    private GameObject m_CirclePrefab;

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            for (int i = 0; i < m_NbCircles; i++)
            {
                var circleGameObject = Instantiate(m_CirclePrefab);
                var movingCircle = circleGameObject.GetComponent<MovingCircle>();
                movingCircle.InitialPosition = Random.insideUnitCircle * m_MaxInitialDistance;
                movingCircle.InitialVelocity = Random.insideUnitCircle * m_MaxInitialVelocity;
                var circleNetworkObject = circleGameObject.GetComponent<NetworkObject>();
                circleNetworkObject.Spawn();
            }
        }
    } 
}
