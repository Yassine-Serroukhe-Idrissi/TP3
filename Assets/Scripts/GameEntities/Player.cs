using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Contrôle la logique du carré du joueur côté client et serveur.
/// Ce script gère la capture des inputs, la prédiction côté client,
/// l'envoi des inputs avec leur tick, et la réconciliation avec l'état autoritaire.
/// </summary>
public class Player : NetworkBehaviour
{
    [SerializeField]
    private float m_Velocity = 5f; // Vitesse du joueur

    [SerializeField]
    private float m_Size = 1f; // Utilisé pour la gestion des collisions

    private GameState m_GameState;
    private GameState GameState
    {
        get
        {
            if (m_GameState == null)
                m_GameState = FindObjectOfType<GameState>();
            return m_GameState;
        }
    }

    // Variable réseau contenant la position autoritaire (mise à jour par le serveur)
    private NetworkVariable<Vector2> m_Position = new NetworkVariable<Vector2>();
    public Vector2 Position => m_Position.Value;

    // Variable réseau pour le dernier tick traité par le serveur (pour la réconciliation)
    private NetworkVariable<int> m_ServerTick = new NetworkVariable<int>();
    public int ServerTick => m_ServerTick.Value;

    // File d'attente côté serveur pour stocker les inputs reçus avec leur tick associé.
    private Queue<InputFrame> m_InputQueue = new Queue<InputFrame>();

    // Structure pour stocker un input avec son tick et la position prédite après application.
    private struct InputFrame
    {
        public int Tick;
        public Vector2 Input;
        public Vector2 Position;
    }

    // Historique des inputs côté client (pour rejouer les inputs si correction nécessaire)
    private List<InputFrame> m_InputHistory = new List<InputFrame>();

    // Position locale prédite par le client (mise à jour immédiatement lors d'un input)
    private Vector2 m_PredictedPosition;

    // Tick local servant d'horodatage pour chaque input
    private int m_LocalTick = 0;

    public Vector2 PredictedPosition => m_PredictedPosition;

    private void Awake()
    {
        m_GameState = FindObjectOfType<GameState>();
        // Initialisation de la position prédite avec celle reçue initialement du serveur
        m_PredictedPosition = m_Position.Value;
    }

    private void FixedUpdate()
    {
        // Si le jeu est en état "stun" ou si GameState n'est pas disponible, ne rien faire
        if (GameState == null || GameState.IsStunned)
            return;

        // Logique serveur : traiter les inputs reçus
        if (IsServer)
        {
            ProcessInputQueue();
        }

        // Logique client : capture des inputs et réconciliation uniquement pour le joueur possédé
        if (IsClient && IsOwner)
        {
            TryReconcile();
            ProcessLocalInput();
            // Passage au tick suivant
            m_LocalTick++;
        }
    }

    /// <summary>
    /// Traitement des inputs en file côté serveur.
    /// À chaque input, la position autoritaire est mise à jour, avec vérification des collisions.
    /// Le dernier tick traité est stocké dans m_ServerTick.
    /// </summary>
    private void ProcessInputQueue()
    {
        if (m_InputQueue.Count > 0)
        {
            InputFrame frame = m_InputQueue.Dequeue();

            // Mise à jour de la position du joueur
            m_Position.Value += frame.Input * m_Velocity * Time.fixedDeltaTime;

            // Gestion des collisions avec les bords de la zone de jeu
            Vector2 pos = m_Position.Value;
            Vector2 size = GameState.GameSize;
            if (pos.x - m_Size < -size.x)
                pos.x = -size.x + m_Size;
            else if (pos.x + m_Size > size.x)
                pos.x = size.x - m_Size;
            if (pos.y - m_Size < -size.y)
                pos.y = -size.y + m_Size;
            else if (pos.y + m_Size > size.y)
                pos.y = size.y - m_Size;
            m_Position.Value = pos;

            // Mise à jour du tick autoritaire en fonction de l'input traité
            m_ServerTick.Value = frame.Tick;
        }
    }

    /// <summary>
    /// Capture des inputs côté client et envoi au serveur.
    /// Applique immédiatement l'input en local pour une simulation réactive.
    /// </summary>
    private void ProcessLocalInput()
    {
        Vector2 inputDirection = Vector2.zero;
        if (Input.GetKey(KeyCode.W))
            inputDirection += Vector2.up;
        if (Input.GetKey(KeyCode.A))
            inputDirection += Vector2.left;
        if (Input.GetKey(KeyCode.S))
            inputDirection += Vector2.down;
        if (Input.GetKey(KeyCode.D))
            inputDirection += Vector2.right;

        inputDirection = inputDirection.normalized;

        // Si une direction est indiquée, appliquer l'input localement
        if (inputDirection != Vector2.zero)
        {
            m_PredictedPosition += inputDirection * m_Velocity * Time.fixedDeltaTime;

            // Création d'un InputFrame enregistré pour le tick courant
            InputFrame frame = new InputFrame
            {
                Tick = m_LocalTick,
                Input = inputDirection,
                Position = m_PredictedPosition
            };
            m_InputHistory.Add(frame);

            // Limiter la taille de l'historique (pour éviter une accumulation excessive)
            if (m_InputHistory.Count > 200)
                m_InputHistory.RemoveAt(0);
        }

        // Envoyer l'input au serveur, même s'il s'agit d'un input nul, pour assurer la synchronisation des ticks
        SendInputServerRpc(inputDirection, m_LocalTick);
    }

    /// <summary>
    /// ServerRPC : le client envoie son input avec le tick associé.
    /// Le serveur crée alors un InputFrame qui est ajouté à la file d'attente.
    /// </summary>
    /// <param name="input">Direction d'input envoyée par le client</param>
    /// <param name="tick">Tick local du client lors de l'envoi</param>
    [ServerRpc]
    private void SendInputServerRpc(Vector2 input, int tick)
    {
        InputFrame frame = new InputFrame
        {
            Tick = tick,
            Input = input,
            Position = Vector2.zero // La position n'est pas nécessaire côté serveur
        };
        m_InputQueue.Enqueue(frame);
    }

    /// <summary>
    /// Réconciliation côté client.
    /// Compare l'état autoritaire (m_Position) avec la prédiction enregistrée pour le tick traité par le serveur.
    /// En cas d'erreur supérieure au seuil défini, corrige la position prédite et rejoue les inputs non confirmés.
    /// </summary>
    private void TryReconcile()
    {
        // Cherche dans l'historique l'input correspondant au dernier tick reçu du serveur
        InputFrame? matchingFrame = null;
        foreach (var frame in m_InputHistory)
        {
            if (frame.Tick == m_ServerTick.Value)
            {
                matchingFrame = frame;
                break;
            }
        }

        if (matchingFrame.HasValue)
        {
            // Comparer la position autoritaire à la position prédite enregistrée pour ce tick
            float error = Vector2.Distance(m_Position.Value, matchingFrame.Value.Position);
            if (error > 0.01f)
            {
                Debug.LogWarning($"[RECONCILE] Erreur détectée au tick {matchingFrame.Value.Tick}: {error}. Correction appliquée.");
                // Correction : la position prédite est remplacée par la position autoritaire
                m_PredictedPosition = m_Position.Value;

                // Rejouer les inputs dont le tick est supérieur à celui confirmé par le serveur
                List<InputFrame> framesToReplay = m_InputHistory.FindAll(f => f.Tick > matchingFrame.Value.Tick);
                m_InputHistory.RemoveAll(f => f.Tick > matchingFrame.Value.Tick);

                foreach (var f in framesToReplay)
                {
                    m_PredictedPosition += f.Input * m_Velocity * Time.fixedDeltaTime;
                    // Enregistrer à nouveau le frame avec la nouvelle position calculée
                    InputFrame newFrame = new InputFrame
                    {
                        Tick = f.Tick,
                        Input = f.Input,
                        Position = m_PredictedPosition
                    };
                    m_InputHistory.Add(newFrame);
                }
                Debug.Log($"[RECONCILE] {framesToReplay.Count} inputs rejoués après correction.");
            }
        }
    }
}
