
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Fusion;
using Fusion.Sockets;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class MultiplayerManager : MonoBehaviour, INetworkRunnerCallbacks
{
    [Header("Networking")]
    [SerializeField] private NetworkRunner runner;

    [Header("Panels")]
    [SerializeField] private GameObject mainMenuPanel;
    [SerializeField] private GameObject lobbyPanel;
    [SerializeField] private GameObject roomPanel;

    [Header("Lobby UI")]
    [SerializeField] private TMP_InputField lobbyNameInput;
    [SerializeField] private Button joinLobbyButton;
    [SerializeField] private Button leaveLobbyButton;
    [SerializeField] private TMP_Text lobbyStatusText;

    [Header("Room Creation UI")]
    [SerializeField] private TMP_InputField sessionNameInput;
    [SerializeField] private TMP_InputField maxPlayersInput;
    [SerializeField] private Button createRoomButton;
    [SerializeField] private TMP_Text lobbyRoomStatusText;

    [Header("Room List")]
    [SerializeField] private Button sessionEntryPrefab;
    [SerializeField] private Transform sessionListContent;
    [SerializeField] private CanvasGroup sessionListGroup;

    [Header("Inside Room UI")]
    [SerializeField] private TMP_Text playerCountText;
    [SerializeField] private TMP_Text playerEntryPrefab;
    [SerializeField] private Transform playerListContent;
    [SerializeField] private Button leaveRoomButton;

    private bool busy = false;
    private string currentLobbyName;

    private void Awake()
    {
        ShowMainMenu("");
    }

    // Join a lobby chosen by the player
    public async void JoinLobby()
    {
        if (busy)
            return;

        string lobbyName = lobbyNameInput.text.Trim();

        if (lobbyName.Length == 0)
        {
            lobbyStatusText.text = "Please enter a lobby name.";
            return;
        }

        SetBusy(true);
        lobbyStatusText.text = "Connecting...";

        // Create a new runner if the previous one was shut down.
        if (runner == null)
        {
            GameObject runnerObject = new GameObject("NetworkRunner");
            runner = runnerObject.AddComponent<NetworkRunner>();
        }

        runner.AddCallbacks(this);

        try
        {
            var result = await runner.JoinSessionLobby(SessionLobby.Custom,lobbyName);

            if (result.Ok && runner != null)
            {
                currentLobbyName = lobbyName;

                mainMenuPanel.SetActive(false);
                lobbyPanel.SetActive(true);

                lobbyRoomStatusText.text = "Lobby: " + lobbyName;

                Debug.Log("Joined lobby: " + lobbyName);
            }
            else
            {
                await ReturnToMenu("Failed to join lobby: " + result.ShutdownReason);
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning(ex.Message);
            await ReturnToMenu("Could not connect to lobby.");
        }

        SetBusy(false);
    }

    // Update the room list whenever Photon reports a change
    public void OnSessionListUpdated(NetworkRunner source, List<SessionInfo> sessions)
    {
        if (source != runner || roomPanel.activeSelf)
            return;

        ClearRoomList();

        foreach (SessionInfo session in sessions)
        {
            Button button = Instantiate(sessionEntryPrefab, sessionListContent);

            // Find the text on this button copy to display its room.
            TMP_Text label = button.GetComponentInChildren<TMP_Text>();

            label.text = $"{session.Name} ({session.PlayerCount}/{session.MaxPlayers})";

            button.interactable = session.IsOpen && session.PlayerCount < session.MaxPlayers;

            string roomName = session.Name;

            button.onClick.AddListener(() => JoinRoom(roomName));
        }
    }

    // Create a room with the selected name and player limit
    public void CreateRoom()
    {
        if (busy || !lobbyPanel.activeSelf)
            return;

        string roomName = sessionNameInput.text.Trim();

        if (roomName.Length == 0)
        {
            lobbyRoomStatusText.text = "Enter a room name.";
            return;
        }

        if (!int.TryParse(maxPlayersInput.text, out int maxPlayers) || maxPlayers < 1)
        {
            lobbyRoomStatusText.text = "Invalid player count.";
            return;
        }

        StartRoom(roomName, true, maxPlayers);
    }

    // Join an existing room from the session list
    private void JoinRoom(string roomName)
    {
        StartRoom(roomName, false, 0);
    }

    // Create or join a Shared Mode session
    private async void StartRoom(string roomName, bool createRoom, int maxPlayers)
    {
        if (busy || runner == null || !lobbyPanel.activeSelf)
            return;

        SetBusy(true);
        lobbyRoomStatusText.text = "Connecting to room...";

        StartGameArgs args = new StartGameArgs
        {
            GameMode = GameMode.Shared,
            SessionName = roomName,
            CustomLobbyName = currentLobbyName
        };

        if (createRoom) args.PlayerCount = maxPlayers;
        else
            args.EnableClientSessionCreation = false;

        try
        {
            var result = await runner.StartGame(args);

            if (result.Ok && runner != null)
            {
                lobbyPanel.SetActive(false);
                roomPanel.SetActive(true);

                RefreshPlayerList(runner);

                Debug.Log("Entered room: " + roomName);
            }
            else
            {
                await ReturnToMenu("Failed to enter room: " + result.ShutdownReason);
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning(ex.Message);
            await ReturnToMenu("Could not enter room.");
        }

        SetBusy(false);
    }

    // Photon calls this when a player joins
    public void OnPlayerJoined(NetworkRunner source, PlayerRef player)
    {
        RefreshPlayerList(source);
    }

    // Photon calls this when a player leaves
    public void OnPlayerLeft(NetworkRunner source, PlayerRef player)
    {
        RefreshPlayerList(source);
    }

    // Display all connected player IDs and the player count
    private void RefreshPlayerList(NetworkRunner source)
    {
        if (source != runner || !roomPanel.activeSelf)
            return;

        ClearPlayerList();

        foreach (PlayerRef player in source.ActivePlayers)
        {
            TMP_Text entry = Instantiate(playerEntryPrefab, playerListContent);

            entry.text = "Player " + player.PlayerId;

            if (player == source.LocalPlayer)
            {
                entry.text += " (You)";
            }
        }

        playerCountText.text = "Players: " + source.SessionInfo.PlayerCount;

        
    }

    // Leave the current lobby or room
    
    public async void Leave()
    {
        if (busy) return;

        SetBusy(true);

        try
        {
            await ReturnToMenu("");
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
        }
        finally
        {
            SetBusy(false);
        }
    }


    // Disconnect and return to the main menu
    private async Task ReturnToMenu(string message)
    {
        if (runner != null)
        {
            try
            {
                await runner.Shutdown();
            }
            catch (Exception ex)
            {
                Debug.LogWarning(ex.Message);
            }
        }

        runner = null;
        ShowMainMenu(message);
    }

    // Handle runner shutdown
    public void OnShutdown( NetworkRunner source, ShutdownReason reason)
    {
        if (source != runner)
            return;

        runner = null;

        if (!busy)
        {
            ShowMainMenu("Disconnected: " + reason);
        }
    }

    // Disable buttons while a network operation is running
    private void SetBusy(bool value)
    {
        busy = value;

        joinLobbyButton.interactable = !value;
        leaveLobbyButton.interactable = !value;
        createRoomButton.interactable = !value;
        leaveRoomButton.interactable = !value;
        sessionListGroup.interactable = !value;
    }

    // Display only the main menu
    private void ShowMainMenu(string message)
    {
        mainMenuPanel.SetActive(true);
        lobbyPanel.SetActive(false);
        roomPanel.SetActive(false);

        lobbyStatusText.text = message;
        playerCountText.text = "Players: 0";

        ClearRoomList();
        ClearPlayerList();

        SetBusy(false);
    }

    // Remove the previous room buttons
    private void ClearRoomList()
    {
        foreach (Transform entry in sessionListContent)
        {
            entry.gameObject.SetActive(false);
            Destroy(entry.gameObject);
        }
    }

    // Remove the previous player entries
    private void ClearPlayerList()
    {
        foreach (Transform entry in playerListContent)
        {
            entry.gameObject.SetActive(false);
            Destroy(entry.gameObject);
        }
    }

    // Remaining callbacks required by Fusion
    public void OnConnectedToServer(NetworkRunner source) { }

    public void OnDisconnectedFromServer(NetworkRunner source,NetDisconnectReason reason) { }

    public void OnConnectRequest(NetworkRunner source,NetworkRunnerCallbackArgs.ConnectRequest request,byte[] token) { }

    public void OnConnectFailed(NetworkRunner source,NetAddress address,NetConnectFailedReason reason) { }

    public void OnInput(NetworkRunner source,NetworkInput input) { }

    public void OnInputMissing(NetworkRunner source,PlayerRef player,NetworkInput input) { }

    [Obsolete]
    public void OnUserSimulationMessage(NetworkRunner source,SimulationMessagePtr message) { }

    public void OnCustomAuthenticationResponse(NetworkRunner source,Dictionary<string, object> data) { }

    public void OnHostMigration(NetworkRunner source,HostMigrationToken token) { }

    public void OnSceneLoadStart(NetworkRunner source) { }

    public void OnSceneLoadDone(NetworkRunner source) { }

    public void OnObjectEnterAOI(NetworkRunner source,NetworkObject obj,PlayerRef player) { }

    public void OnObjectExitAOI(NetworkRunner source,NetworkObject obj,PlayerRef player) { }

    public void OnReliableDataReceived(NetworkRunner source,PlayerRef player,ReliableKey key,ReadOnlySpan<byte> data) { }

    public void OnReliableDataProgress(NetworkRunner source, PlayerRef player, ReliableKey key, float progress) { }
}
