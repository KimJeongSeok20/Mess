using System.Collections;
using PurrNet;
using PurrNet.Logging;
using PurrNet.Transports;
using UnityEngine;
using PurrNet.Steam;
using Steamworks;

namespace PurrLobby
{
    public class MyConnectionStarter : MonoBehaviour
    {
        private SteamTransport _steamtransport;
        private UDPTransport _udpTransport;
        [SerializeField] private LocalTransport localTransport;
        private NetworkManager _networkManager;
        private LobbyDataHolder _lobbyDataHolder;

        private bool _isFromLobby = true;
        private bool _soloStartRequested;
        private bool _steamStartRequested;
        private const float HostClientStartDelay = 0.1f;
        private const float CloneClientStartDelay = 5.0f;
        private const float LobbyClientStartDelay = 1.0f;
        private const int CloneClientRetryCount = 5;
        private const float CloneClientRetryInterval = 2.0f;
        private const float CloneClientConnectTimeout = 8.0f;
        private const float CloneDisconnectWaitTimeout = 3.0f;

        private void Awake()
        {
            if (!TryGetComponent(out _networkManager))
            {
                PurrLogger.LogError($"Failed to get {nameof(NetworkManager)} component.", this);
            }
            
            if (!TryGetComponent(out _steamtransport))
            {
                PurrLogger.LogError($"Failed to get {nameof(SteamTransport)} component.", this);
            }

            if (!TryGetComponent(out _udpTransport))
            {
                PurrLogger.LogError($"Failed to get {nameof(UDPTransport)} component.", this);
            }

            _lobbyDataHolder = FindFirstObjectByType<LobbyDataHolder>();
            Debug.Log($"[MyConnectionStarter]{_lobbyDataHolder}");
            _soloStartRequested = RunSaveService.IsSoloStartRequested;
            _steamStartRequested = !_soloStartRequested && SteamRoomService.Instance != null && SteamRoomService.Instance.IsGameSession;
            _isFromLobby = !_soloStartRequested && _lobbyDataHolder != null && _lobbyDataHolder.CurrentLobby.IsValid;

            if (_networkManager != null)
            {
                _networkManager.transport = _soloStartRequested ? localTransport
                    : _isFromLobby ? _steamtransport : _udpTransport;
                Debug.Log($"[MyConnectionStarter] Awake transport bind: {(_networkManager.transport != null ? _networkManager.transport.GetType().Name : "null")}");
            }
        }

        private void Start()
        {
            if (_steamStartRequested && (!_isFromLobby || SteamRoomService.Instance == null || SteamRoomService.Instance.State == SteamRoomState.Error))
            {
                PurrLogger.LogError("The Steam room closed while loading. Return to the title and join again.", this);
                return;
            }
            if (!_networkManager)
            {
                PurrLogger.LogError($"Failed to start connection. {nameof(NetworkManager)} is null!", this);
                return;
            }

            if (_isFromLobby)
            {
                Debug.Log("Run by Lobby Mode");
                StartFromLobby();
            }
            else
            {
                Debug.Log("Run by Normal Mode");
                StartNormal();
            }

        }
        private void StartNormal()
        {
            // A title-menu solo request takes precedence over a stale lobby or client test flag.
            if (_soloStartRequested)
            {
                if (localTransport == null)
                {
                    PurrLogger.LogError("Local play transport is missing on NetworkManager.", this);
                    return;
                }
                Debug.Log("[MyConnectionStarter] Local solo session (no network socket).");
                _networkManager.StartServer();
                _networkManager.StartClient();
                return;
            }

            _networkManager.transport = _udpTransport;

            bool isClone = false;
            #if UNITY_EDITOR
            isClone = ParrelSync.ClonesManager.IsClone();
            #endif

            // Unity 6 Multiplayer Play Mode: virtual players tagged "Client" join the main editor's host.
            if (!isClone && IsMultiplayerPlayModeClient())
                isClone = true;

            // Standalone build launched with "-client": join the editor host on localhost
            // (the low-memory way to test two players on one machine).
            if (!isClone && HasCommandLineFlag("-client"))
                isClone = true;

            // Test hook: lets the editor act as the client while a standalone build hosts.
            // (PlayerPrefs survives the domain reload that happens when entering play mode.)
            if (!isClone && (ForceClientForTesting || PlayerPrefs.GetInt(ForceClientPrefKey, 0) == 1))
                isClone = true;

            Debug.Log($"[MyConnectionStarter] Normal mode start. isClone={isClone}, transport={_networkManager.transport.GetType().Name}");

            if (!isClone)
            {
                _networkManager.StartServer();
                StartCoroutine(StartClientAfterDelay(HostClientStartDelay));
                return;
            }

            StartCoroutine(StartCloneClientWithRetry());
        }
        /// <summary>Set from an editor script/eval before entering play mode to make this editor a client.</summary>
        public static bool ForceClientForTesting;
        public const string ForceClientPrefKey = "StillWorking.ForceClient";

        private static bool HasCommandLineFlag(string flag)
        {
            try
            {
                string[] args = System.Environment.GetCommandLineArgs();
                for (int i = 0; i < args.Length; i++)
                {
                    if (string.Equals(args[i], flag, System.StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }
            catch (System.Exception)
            {
                // Some platforms restrict command line access; treat as absent.
            }

            return false;
        }

        /// <summary>
        /// True inside a Multiplayer Play Mode virtual player whose tags contain "Client" (or any
        /// virtual player other than the main editor when no tags are set). Reflection keeps the
        /// package optional.
        /// </summary>
        private static bool IsMultiplayerPlayModeClient()
        {
            try
            {
                // Unity 6.3+: built into UnityEngine.MultiplayerModule as Unity.Multiplayer.PlayMode.CurrentPlayer.
                System.Type type = null;
                foreach (var assembly in System.AppDomain.CurrentDomain.GetAssemblies())
                {
                    type = assembly.GetType("Unity.Multiplayer.PlayMode.CurrentPlayer", false)
                           ?? assembly.GetType("Unity.Multiplayer.Playmode.CurrentPlayer", false);
                    if (type != null)
                        break;
                }

                if (type == null)
                    return false;

                var tagsMethod = type.GetMethod("ReadOnlyTags", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                if (tagsMethod != null && tagsMethod.Invoke(null, null) is string[] tags)
                {
                    for (int i = 0; i < tags.Length; i++)
                    {
                        if (string.Equals(tags[i], "Client", System.StringComparison.OrdinalIgnoreCase))
                            return true;
                        if (string.Equals(tags[i], "Host", System.StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(tags[i], "Server", System.StringComparison.OrdinalIgnoreCase))
                            return false;
                    }
                }

                // Untagged virtual player: anything that is not the main editor becomes a client.
                // Only meaningful inside the editor; a standalone player is never the "main editor".
                if (Application.isEditor)
                {
                    var isMainProp = type.GetProperty("IsMainEditor", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                    if (isMainProp != null && isMainProp.GetValue(null) is bool isMain)
                        return !isMain;
                }
            }
            catch (System.Exception exception)
            {
                Debug.LogWarning($"[MyConnectionStarter] MPPM detection failed: {exception.Message}");
            }

            return false;
        }

        private void StartFromLobby()
        {
            _networkManager.transport = _steamtransport;
            if (!_lobbyDataHolder)
            {
                PurrLogger.LogError($"Failed to start connection. {nameof(LobbyDataHolder)} is null!", this);
                return;
            }

            if (!_lobbyDataHolder.CurrentLobby.IsValid)
            {
                PurrLogger.LogError($"Failed to start connection. Lobby is invalid!", this);
                return;
            }

            if(!ulong.TryParse(_lobbyDataHolder.CurrentLobby.LobbyId,out ulong ulongId))
            {
                Debug.LogError($"Failed to parse lobbyid into Ulong!", this);
                return;
            }
            var lobbyOwner = SteamMatchmaking.GetLobbyOwner(new CSteamID(ulongId));
            if (!lobbyOwner.IsValid()) {
                Debug.LogError($"Failed to get lobby owner from parsed lobby ID", this);
                return;
            }

            _steamtransport.address = lobbyOwner.ToString();
            _steamtransport.peerToPeer = true;
            _steamtransport.dedicatedServer = false;

            if (_lobbyDataHolder.CurrentLobby.IsOwner)
                _networkManager.StartServer();
            StartCoroutine(StartCloneClientWithRetry(LobbyClientStartDelay));
        }

        private IEnumerator StartClientAfterDelay(float delay)
        {
            if (delay > 0f)
                yield return new WaitForSeconds(delay);

            Debug.Log($"[MyConnectionStarter] Starting client after {delay:0.00}s delay");
            _networkManager.StartClient();
        }

        private IEnumerator StartCloneClientWithRetry(float initialDelay = CloneClientStartDelay)
        {
            // Steam peers may finish loading before their host; retry the same selected transport.
            yield return new WaitForSecondsRealtime(initialDelay);

            for (int attempt = 1; attempt <= CloneClientRetryCount; attempt++)
            {
                if (_steamStartRequested && (SteamRoomService.Instance == null || SteamRoomService.Instance.State == SteamRoomState.Error))
                    yield break;
                if (_networkManager.clientState == ConnectionState.Connected)
                {
                    Debug.Log($"[MyConnectionStarter] Clone client already connected before attempt {attempt}");
                    yield break;
                }

                if (_networkManager.clientState == ConnectionState.Disconnecting)
                {
                    Debug.Log($"[MyConnectionStarter] Clone client disconnecting before attempt {attempt}, waiting for cleanup...");
                    yield return WaitForClientState(ConnectionState.Disconnected, CloneDisconnectWaitTimeout);
                }

                if (_networkManager.clientState != ConnectionState.Disconnected)
                {
                    Debug.Log($"[MyConnectionStarter] Clone client forcing stop before attempt {attempt}, state={_networkManager.clientState}");
                    _networkManager.StopClient();
                    yield return WaitForClientState(ConnectionState.Disconnected, CloneDisconnectWaitTimeout);
                }

                Debug.Log($"[MyConnectionStarter] Clone client start attempt {attempt}/{CloneClientRetryCount}");
                _networkManager.StartClient();

                // Steam starts connecting asynchronously, so Disconnected can persist after StartClient.
                // Keep the full connection window instead of retrying before the transport begins.
                float elapsed = 0f;
                while (elapsed < CloneClientConnectTimeout)
                {
                    if (_networkManager.clientState == ConnectionState.Connected)
                    {
                        Debug.Log($"[MyConnectionStarter] Clone client connected on attempt {attempt}");
                        yield break;
                    }

                    elapsed += Time.unscaledDeltaTime;
                    yield return null;
                }

                if (_networkManager.clientState == ConnectionState.Connected)
                {
                    Debug.Log($"[MyConnectionStarter] Clone client connected on attempt {attempt}");
                    yield break;
                }

                Debug.Log($"[MyConnectionStarter] Clone client state after attempt {attempt}: {_networkManager.clientState}");

                if (_networkManager.clientState != ConnectionState.Disconnected)
                {
                    // Force a clean retry instead of burning all attempts while the client is stuck in Connecting.
                    _networkManager.StopClient();
                    yield return WaitForClientState(ConnectionState.Disconnected, CloneDisconnectWaitTimeout);
                }

                yield return new WaitForSeconds(CloneClientRetryInterval);
            }

            Debug.LogWarning("[MyConnectionStarter] Clone client failed to connect after retries");
        }

        private IEnumerator WaitForClientState(ConnectionState targetState, float timeout)
        {
            float elapsed = 0f;
            while (_networkManager != null && _networkManager.clientState != targetState && elapsed < timeout)
            {
                elapsed += Time.deltaTime;
                yield return null;
            }
        }
    }
}

