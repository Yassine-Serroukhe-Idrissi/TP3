using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public struct TickInput : INetworkSerializeByMemcpy
{
    public int tick;
    public Vector2 input;
}

public struct SimulationResult : INetworkSerializeByMemcpy
{
    public TickInput tickInput;
    public Vector2 position;
}

public class Player : NetworkBehaviour
{
    [SerializeField]
    private float m_Velocity;

    [SerializeField]
    private float m_Size = 1;

    private GameState m_GameState;

    // GameState peut etre nul si l'entite joueur est instanciee avant de charger MainScene
    private GameState GameState
    {
        get
        {
            if (m_GameState == null)
            {
                m_GameState = FindObjectOfType<GameState>();
            }
            return m_GameState;
        }
    }

    private NetworkVariable<SimulationResult> m_LastServerSimulationResult = new();
    private Vector2 m_LocalPosition = new();
    public Vector2 Position => (IsClient) ? m_LocalPosition : m_LastServerSimulationResult.Value.position;

    private Queue<TickInput> m_TickInputQueue = new();
    private Queue<SimulationResult> m_History = new();

    public override void OnNetworkSpawn()
    {
        if (IsClient)
        {
            m_LastServerSimulationResult.OnValueChanged += OnLastServerSimulationResultChanged;
        }
    }

    public override void OnNetworkDespawn()
    {
        if (IsClient)
        {
            m_LastServerSimulationResult.OnValueChanged -= OnLastServerSimulationResultChanged;
        }
    }

    private void Awake()
    {
        m_GameState = FindObjectOfType<GameState>();
    }

    private void FixedUpdate()
    {
        // Si le stun est active, rien n'est mis a jour.
        if (GameState == null || GameState.IsStunned)
        {
            return;
        }

        // Seul le serveur met à jour la position de l'entite.
        if (IsServer)
        {
            var serverSimulationResult = Simulate(m_LastServerSimulationResult.Value.position, m_TickInputQueue);
            if (!serverSimulationResult.Equals(default))
            {
                m_LastServerSimulationResult.Value = serverSimulationResult;
            }
        }

        // Seul le client qui possede cette entite peut envoyer ses inputs.
        if (IsClient && IsOwner)
        {
            var tickInput = new TickInput { tick = NetworkUtility.GetLocalTick(), input = GetInputClient() };
            m_TickInputQueue.Enqueue(tickInput);

            var clientSimulationResult = Simulate(m_LocalPosition, m_TickInputQueue);
            m_LocalPosition = clientSimulationResult.position;
            m_History.Enqueue(clientSimulationResult);

            SendTickInputServerRpc(tickInput);
        }

        // Seul le client qui ne possèdent pas cette entite peut estimer sa position.

        if (IsClient && !IsOwner)
        {
            var tickInput = m_LastServerSimulationResult.Value.tickInput;

            var clientSimulationResult = SimulateGhost(m_LocalPosition, tickInput);
            m_LocalPosition = clientSimulationResult.position;
            m_History.Enqueue(clientSimulationResult);

        }

    }
    private SimulationResult SimulateGhost(Vector2 position, TickInput lastServerTick)
    {
        //On estime l'input du joueur fantôme par rapport au dernier input reçu
        var estimatedtickInput = new TickInput { tick = NetworkUtility.GetLocalTick(), input = lastServerTick.input };
        position += estimatedtickInput.input * m_Velocity * Time.fixedDeltaTime;

        // Gestion des collisions avec l'exterieur de la zone de simulation
        var size = GameState.GameSize;
        if (position.x - m_Size < -size.x)
        {
            position = new Vector2(-size.x + m_Size, position.y);
        }
        else if (position.x + m_Size > size.x)
        {
            position = new Vector2(size.x - m_Size, position.y);
        }

        if (position.y + m_Size > size.y)
        {
            position = new Vector2(position.x, size.y - m_Size);
        }
        else if (position.y - m_Size < -size.y)
        {
            position = new Vector2(position.x, -size.y + m_Size);
        }
        return new SimulationResult { tickInput = estimatedtickInput, position = position };
    }
    private SimulationResult Simulate(Vector2 position, Queue<TickInput> tickInputQueue)
    {
        // Mise a jour de la position selon dernier input reçu, puis consommation de l'input
        if (tickInputQueue.Count > 0)
        {
            var tickInput = tickInputQueue.Dequeue();
            position += tickInput.input * m_Velocity * Time.fixedDeltaTime;

            // Gestion des collisions avec l'exterieur de la zone de simulation
            var size = GameState.GameSize;
            if (position.x - m_Size < -size.x)
            {
                position = new Vector2(-size.x + m_Size, position.y);
            }
            else if (position.x + m_Size > size.x)
            {
                position = new Vector2(size.x - m_Size, position.y);
            }

            if (position.y + m_Size > size.y)
            {
                position = new Vector2(position.x, size.y - m_Size);
            }
            else if (position.y - m_Size < -size.y)
            {
                position = new Vector2(position.x, -size.y + m_Size);
            }

            return new SimulationResult { tickInput = tickInput, position = position };
        }

        return default;
    }

    private Vector2 GetInputClient()
    {
        Vector2 inputDirection = new Vector2(0, 0);
        if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow))
        {
            inputDirection += Vector2.up;
        }
        if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow))
        {
            inputDirection += Vector2.left;
        }
        if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow))
        {
            inputDirection += Vector2.down;
        }
        if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow))
        {
            inputDirection += Vector2.right;
        }
        return inputDirection.normalized;
    }

    [ServerRpc]
    private void SendTickInputServerRpc(TickInput tickInput)
    {
        // On utilise une file pour les inputs pour les cas ou on en recoit plusieurs en meme temps.
        m_TickInputQueue.Enqueue(tickInput);
    }

    private void OnLastServerSimulationResultChanged(SimulationResult previous, SimulationResult current)
    {
        while (m_History.Count > 0 && m_History.Peek().tickInput.tick < current.tickInput.tick)
        {
            m_History.Dequeue();
        }
        if (m_History.Count > 0 && m_History.Peek().tickInput.tick == current.tickInput.tick)
        {
            var clientSimulationResult = m_History.Dequeue();
            if (IsOwner)
            {
                if (clientSimulationResult.position != current.position)
                {
                    Reconciliate(current);

                }
            }
            else
            {
                //Réconciliation de la prédiction des ghosts par le client
                if (clientSimulationResult.position != current.position
                || clientSimulationResult.tickInput.input != current.tickInput.input)
                {
                    ReconciliateGhst(current);

                }
            }
        }
    }

    private void Reconciliate(SimulationResult serverSimulationResult)
    {
        var tempPosition = serverSimulationResult.position;
        Queue<TickInput> tempTickInputQueue = new();
        Queue<SimulationResult> correctedHistory = new();
        while (m_History.Count > 0)
        {
            var clientSimulationResult = m_History.Dequeue();
            tempTickInputQueue.Enqueue(clientSimulationResult.tickInput);
            var correctedSimulationResult = Simulate(tempPosition, tempTickInputQueue);
            tempPosition = correctedSimulationResult.position;
            correctedHistory.Enqueue(correctedSimulationResult);
        }
        //Debug.Log($"Reconciliate: {this.GetInstanceID()} {IsOwner} {m_LocalPosition} -> {tempPosition}");
        m_LocalPosition = tempPosition;
        m_History = correctedHistory;
    }

    /*
    Méthode qui gère la réconcialiation de la prédiction de l'état de ghosts pas un client avec celle du serveur
    */
    private void ReconciliateGhst(SimulationResult serverSimulationResult)
    {
        var tempPosition = serverSimulationResult.position;
        var tempTick = serverSimulationResult.tickInput;

        Queue<SimulationResult> correctedHistory = new();
        if (m_History.Count > 0)
        {
            var clientSimulationResult = m_History.Dequeue();
            var correctedSimulationResult = SimulateGhost(tempPosition, tempTick);
            tempPosition = correctedSimulationResult.position;
            correctedHistory.Enqueue(correctedSimulationResult);
        }
        m_LocalPosition = tempPosition;
        m_History = correctedHistory;
    }
}