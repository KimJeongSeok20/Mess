using System.Collections;
using PurrNet;
using PurrNet.Transports;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(NetworkManager))]
public sealed class MonsterTestAutoHost : MonoBehaviour
{
    [SerializeField] private UDPTransport udpTransport;
    [SerializeField] private float startupDelay = 0.15f;
    [SerializeField] private bool logStartup = true;

    private NetworkManager _networkManager;
    private Coroutine _startupRoutine;

    private void Awake()
    {
        _networkManager = GetComponent<NetworkManager>();
        udpTransport ??= GetComponent<UDPTransport>();
    }

    private void Start()
    {
        if (!Application.isPlaying || _networkManager == null)
            return;

        if (_startupRoutine == null)
            _startupRoutine = StartCoroutine(StartHostRoutine());
    }

    private IEnumerator StartHostRoutine()
    {
        if (startupDelay > 0f)
            yield return new WaitForSeconds(startupDelay);
        else
            yield return null;

        _startupRoutine = null;

        if (_networkManager == null || !_networkManager.isOffline)
            yield break;

        if (udpTransport != null)
            _networkManager.transport = udpTransport;

        if (_networkManager.transport == null)
        {
            Debug.LogWarning("[MonsterTestAutoHost] Missing transport. Cannot start host.", this);
            yield break;
        }

        if (logStartup)
            Debug.Log("[MonsterTestAutoHost] Starting local host for monster test scene.", this);

        _networkManager.StartHost();
    }
}
