using System;
using Steamworks;
using UnityEngine;

/// <summary>One persistent Steamworks callback pump for the App ID 480 development build.</summary>
[DefaultExecutionOrder(-1000)]
[DisallowMultipleComponent]
public sealed class SteamManager : MonoBehaviour
{
    public const uint TestAppId = 480;
    public static SteamManager Instance { get; private set; }
    public bool Initialized { get; private set; }
    private bool _ownsInitialization;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    public bool Initialize(out string error)
    {
        error = null;
        try
        {
            if (!Initialized)
            {
                bool available;
                try { InteropHelp.TestIfAvailableClient(); available = true; }
                catch (InvalidOperationException) { available = false; }
                if (!available)
                {
                    if (!SteamAPI.Init())
                    { error = "Start Steam and sign in, then try again. This test build requires steam_appid.txt = 480."; return false; }
                    _ownsInitialization = true;
                }
                Initialized = true;
            }
            if (SteamUtils.GetAppID().m_AppId != TestAppId)
            { error = "This multiplayer test build requires Steam App ID 480."; return false; }
            if (!SteamUser.BLoggedOn())
            { error = "Steam is offline. Sign in to Steam before creating or joining a room."; return false; }
            return true;
        }
        catch (Exception exception) when (exception is DllNotFoundException || exception is EntryPointNotFoundException || exception is InvalidOperationException)
        { error = "Steam could not initialize: " + exception.Message; return false; }
    }

    private void Update()
    {
        if (Initialized) SteamAPI.RunCallbacks();
    }

    private void OnDestroy()
    {
        if (Instance != this) return;
        if (Initialized && _ownsInitialization) SteamAPI.Shutdown();
        Initialized = false;
        Instance = null;
    }
}
