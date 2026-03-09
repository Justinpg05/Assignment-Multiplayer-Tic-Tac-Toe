using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Unity.Netcode;

public class Main : NetworkBehaviour
{
    [Header("UI References")]
    public Button[] cells; // clickable buttons to play the game
    public Image[] marks; // the images that appear either O or X
    public Sprite xSprite;
    public Sprite oSprite;

    [Header("Optional UI Text")]
    public TMP_Text gameOverText;
    public TMP_Text roleText;
    public TMP_Text turnText;

    private NetworkList<int> board;

    private NetworkVariable<int> currentTurn = new( // keeps track of current turn
        1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private NetworkVariable<bool> gameOver = new( // tells everyone the game is over
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private NetworkVariable<ulong> xPlayerId = new( // keeps track of X player
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private NetworkVariable<ulong> oPlayerId = new( // keeps track of O player
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private NetworkVariable<bool> rolesAssigned = new( // when both players start the game this will be true
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private static readonly int[][] wins = new int[][] // setup the board
    {
        new[] {0,1,2}, new[] {3,4,5}, new[] {6,7,8},
        new[] {0,3,6}, new[] {1,4,7}, new[] {2,5,8},
        new[] {0,4,8}, new[] {2,4,6}
    };

    void Awake()
    {
        board = new NetworkList<int>();
    }

    public override void OnNetworkSpawn()
    {
        for (int i = 0; i < cells.Length; i++) // make it so the buttons function
        {
            int idx = i;
            cells[i].onClick.AddListener(() => RequestMove(idx));
        }

        // refresh UI when any of these events are fired
        board.OnListChanged += _ => RefreshUI();
        currentTurn.OnValueChanged += (_, __) => RefreshUI();
        gameOver.OnValueChanged += (_, __) => RefreshUI();
        xPlayerId.OnValueChanged += (_, __) => RefreshUI();
        oPlayerId.OnValueChanged += (_, __) => RefreshUI();
        rolesAssigned.OnValueChanged += (_, __) => RefreshUI();

        if (IsServer) // server is responsible for setting up the board and assigning roles
        {
            EnsureBoard();

            NetworkManager.OnClientConnectedCallback += _ => TryAssignRoles();
            NetworkManager.OnClientDisconnectCallback += OnClientDisconnected;

            TryAssignRoles();
        }

        RefreshUI();
    }

    private void OnClientDisconnected(ulong clientId)
    {
        if (clientId == xPlayerId.Value || clientId == oPlayerId.Value) // reset game if a player leaves
        {
            rolesAssigned.Value = false;
            xPlayerId.Value = 0;
            oPlayerId.Value = 0;
            ResetGameServer();
        }
    }

    private void RequestMove(int index)
    {
        if (!IsSpawned) return;
        SubmitMoveServerRpc(index); // client requests the server to make a move whenever they click a button
    }

    [ServerRpc(RequireOwnership = false)]
    private void SubmitMoveServerRpc(int index, ServerRpcParams rpcParams = default)
    {
        if (!rolesAssigned.Value) return; // server makes sure the client isnt trying to cheat or something, move is verified
        if (gameOver.Value) return;
        if (index < 0 || index > 8) return;
        if (board[index] != 0) return;

        ulong sender = rpcParams.Receive.SenderClientId;

        if (currentTurn.Value == 1 && sender != xPlayerId.Value) return;
        if (currentTurn.Value == 2 && sender != oPlayerId.Value) return;

        board[index] = currentTurn.Value;

        if (CheckWin(currentTurn.Value))
        {
            gameOver.Value = true;
            SetGameOverClientRpc(currentTurn.Value == 1 ? "X Wins!" : "O Wins!");
            return;
        }

        if (IsFull())
        {
            gameOver.Value = true;
            SetGameOverClientRpc("Tie!");
            return;
        }

        // next turn
        currentTurn.Value = (currentTurn.Value == 1) ? 2 : 1;
    }

    [ClientRpc]
    private void SetGameOverClientRpc(string msg)
    {
        if (gameOverText == null) return; // show game over msg

        gameOverText.text = msg;
        gameOverText.gameObject.SetActive(!string.IsNullOrEmpty(msg));
    }

    private void TryAssignRoles()
    {
        if (!IsServer) return;
        if (rolesAssigned.Value) return;

        if (NetworkManager.ConnectedClientsIds.Count < 2)
        {
            if (turnText != null) turnText.text = "Waiting for 2 players...";
            return;
        }

        ulong a = NetworkManager.ConnectedClientsIds[0];
        ulong b = NetworkManager.ConnectedClientsIds[1];

        bool aIsX = Random.Range(0, 2) == 0;
        xPlayerId.Value = aIsX ? a : b;
        oPlayerId.Value = aIsX ? b : a;

        rolesAssigned.Value = true;

        ResetGameServer();
    }

    private void ResetGameServer()
    {
        EnsureBoard();
        for (int i = 0; i < 9; i++) board[i] = 0;

        gameOver.Value = false;
        currentTurn.Value = 1;
        SetGameOverClientRpc("");
    }

    private void EnsureBoard()
    {
        if (board.Count == 9) return;
        board.Clear();
        for (int i = 0; i < 9; i++) board.Add(0);
    }

    private void RefreshUI() // updates visuals on the client
    {
        for (int i = 0; i < 9; i++)
        {
            int v = (board.Count == 9) ? board[i] : 0;

            if (v == 0)
            {
                marks[i].enabled = false;
                marks[i].sprite = null;
            }
            else
            {
                marks[i].enabled = true;
                marks[i].sprite = (v == 1) ? xSprite : oSprite;
            }

            cells[i].interactable = CanLocalClick(i);
        }
    }

    private bool CanLocalClick(int i)
    {
        if (!IsSpawned) return false;
        if (!rolesAssigned.Value) return false;
        if (gameOver.Value) return false;
        if (board.Count != 9) return false;
        if (board[i] != 0) return false;

        ulong me = NetworkManager.Singleton.LocalClientId;
        if (currentTurn.Value == 1) return me == xPlayerId.Value;
        return me == oPlayerId.Value;
    }

    private bool CheckWin(int p)
    {
        foreach (var w in wins)
            if (board[w[0]] == p && board[w[1]] == p && board[w[2]] == p)
                return true;
        return false;
    }

    private bool IsFull()
    {
        for (int i = 0; i < 9; i++)
            if (board[i] == 0) return false;
        return true;
    }
}