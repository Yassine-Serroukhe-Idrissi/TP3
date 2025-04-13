using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
public struct CircleState : INetworkSerializeByMemcpy
{
    public Vector2 position;
    public Vector2 velocity;
    public CircleState(Vector2 pos, Vector2 vel)
    {
        position = pos;
        velocity = vel;
    }

    public CircleState(tickState ts)
    {
        position = ts.position;
        velocity = ts.velocity;
    }
}

public struct tickState : INetworkSerializeByMemcpy
{
    public int tick;
    public Vector2 position;
    public Vector2 velocity;

    public tickState(int t, Vector2 pos, Vector2 vel)
    {
        tick = t;
        position = pos;
        velocity = vel;
    }

    public tickState(int t, CircleState cs)
    {
        tick = t;
        position = cs.position;
        velocity = cs.velocity;
    }
}


public class MovingCircle : NetworkBehaviour
{
    [SerializeField]
    private float m_Radius = 1;
    private int m_LastTickSimulated = 0;
    public uint tps;

    private tickState m_LocalState = new();
    private NetworkVariable<tickState> m_LastKnownState = new();
    public Vector2 Position => (IsClient) ? m_LocalState.position : m_LastKnownState.Value.position;

    public Vector2 Velocity => (IsClient) ? m_LocalState.velocity : m_LastKnownState.Value.velocity;

    public Vector2 InitialPosition, InitialVelocity;
    private Queue<tickState> m_History = new();

    private GameState m_GameState;

    private void Awake()
    {
        m_GameState = FindObjectOfType<GameState>();
    }

    public override void OnNetworkSpawn()
    {

        if (IsServer)
        {
            m_LastKnownState.Value = new tickState(
                NetworkUtility.GetLocalTick(),
                InitialPosition, InitialVelocity);
        }

        if (IsClient)
        {
            m_LastKnownState.OnValueChanged += OnLastServerChangeReceived;

            m_LocalState = m_LastKnownState.Value;
        }
    }

    public override void OnNetworkDespawn()
    {

        if (IsClient)
            m_LastKnownState.OnValueChanged -= OnLastServerChangeReceived;
    }


    private void FixedUpdate()
    {

        if (m_GameState.IsStunned) return;

        if (IsClient)
        {

            tickState serverState = m_LastKnownState.Value;
            int lastServTick = m_LastKnownState.Value.tick;
            int localTick = m_LocalState.tick;


            float rtt = NetworkUtility.GetCurrentRtt(OwnerClientId) / 1000.0f;
            uint tps = NetworkUtility.GetLocalTickRate();
            int servLag = Mathf.FloorToInt(rtt * tps);

            tickState latest;
            int ticksToCatchUp;

            if (lastServTick + servLag > localTick + 1)
            {
                latest = serverState;
                ticksToCatchUp = servLag;
            }
            else 
            {
                latest = m_LocalState;
                ticksToCatchUp = 1;
            }

            tickState simu = latest;
            while (ticksToCatchUp-- > 0)
            {
                simu = tickSimulate(new CircleState(simu));
                m_LastTickSimulated = simu.tick;
            }
            m_LocalState = simu;
        }

        if (IsServer)
        {
            tickState newState = tickSimulate(new CircleState(m_LastKnownState.Value));
            m_LastKnownState.Value = newState;
        }
    }

    private tickState tickSimulate(CircleState state)
    {
        Vector2 pos = state.position;
        Vector2 vel = state.velocity;
        pos += vel * Time.deltaTime;

        var size = m_GameState.GameSize;

        if (pos.x - m_Radius < -size.x)
        {
            pos.x = -(size.x - m_Radius);
            vel.x *= -1;
        }
        else if (pos.x + m_Radius > size.x)
        {
            pos.x = size.x - m_Radius;
            vel.x *= -1;
        }

        if (pos.y + m_Radius > size.y)
        {
            pos.y = size.y - m_Radius;
            vel.y *= -1;
        }
        else if (pos.y - m_Radius < -size.y)
        {
            pos.y = -(size.y - m_Radius);
            vel.y *= -1;
        }

        tickState ret = new tickState(NetworkUtility.GetLocalTick(), pos, vel);


        if (IsClient)
            m_History.Enqueue(ret);

        return ret;
    }


    private void OnLastServerChangeReceived(tickState previous, tickState current)
    {

        while (m_History.Count > 0 && m_History.Peek().tick < current.tick)
            m_History.Dequeue();

        if (m_History.Count > 0 && m_History.Peek().tick == current.tick)
        {
            tickState clientSimu = m_History.Dequeue();
            if (IsOwner)
                /* Reconciliate if the states differ */
                if (clientSimu.position != current.position ||
                    clientSimu.velocity != current.velocity)
                {
                    Reconciliate(current);
                }
        }
    }


    private void Reconciliate(tickState serverSimulation)
    {
        Vector2 servPos = serverSimulation.position;
        Vector2 servVel = serverSimulation.velocity;
        Queue<tickState> correctedHistory = new();
        while (m_History.Count > 0)
        {
            tickState clientSimu = m_History.Dequeue();
            tickState correctSimu = tickSimulate(new CircleState(servPos, servVel));
            servPos = correctSimu.position;
            servVel = correctSimu.velocity;
            correctedHistory.Enqueue(correctSimu);
        }
        
        m_LocalState.position = servPos;
        m_LocalState.velocity = servVel;
        m_History = correctedHistory;
    }
}