using System;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using System.Threading.Tasks;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Multiplayer;
#if UNITY_EDITOR
using Unity.Multiplayer.PlayMode;
#endif

/// <summary>
/// Automatically starts the NetworkManager as Host, Client or Server so nobody has to click buttons while testing.
///
/// In the Editor (Multiplayer Play Mode):
///   - Main Editor            -> Host
///   - Virtual Player clones  -> Client
///   - A clone can be overridden by giving it a Play Mode tag containing "Server", "Host" or "Client"
///     (Window > Multiplayer > Multiplayer Play Mode).
///
/// In a build, pass a command line argument:  -mode host | client | server
/// (e.g. "Game.exe -mode client"). With no argument a build does nothing, so a menu can handle it instead.
/// </summary>
[RequireComponent(typeof(NetworkManager))]
public class NetworkCommandLine : MonoBehaviour
{
    enum Mode { None, Host, Client, Server }

    [Tooltip("Turn off once a real connect menu exists and you want to start connections manually.")]
    [SerializeField] bool autoStartInEditor = true;

    NetworkManager networkManager;
    public static string JoinCode { get; private set; }

async void Start()
{
    networkManager = GetComponent<NetworkManager>();

    // Already started by something else - leave it alone.
    if (networkManager.IsListening)
        return;

    // Came from the menu -> play online through Relay
    if (MenuManager.StartMode == "host") { await HostOnline(); return; }
    if (MenuManager.StartMode == "client") { await JoinOnline(MenuManager.JoinCode); return; }

    // Opened the scene directly -> old local behaviour
    var mode = GetCommandLineMode();
#if UNITY_EDITOR
    if (mode == Mode.None && autoStartInEditor)
        mode = GetPlayModeMode();
#endif

    switch (mode)
    {
        case Mode.Host: networkManager.StartHost(); break;
        case Mode.Client: networkManager.StartClient(); break;
        case Mode.Server: networkManager.StartServer(); break;
        default: return;
    }

    Debug.Log($"[NetworkCommandLine] Started as {mode}");
}

static async Task SignIn()
{
    if (UnityServices.State == ServicesInitializationState.Uninitialized)
    {
        // Random profile so several players on one PC (Multiplayer Play Mode) don't share an account
        var options = new InitializationOptions().SetProfile("p" + Guid.NewGuid().ToString("N").Substring(0, 20));
        await UnityServices.InitializeAsync(options);
    }

    if (!AuthenticationService.Instance.IsSignedIn)
        await AuthenticationService.Instance.SignInAnonymouslyAsync();
}

async Task HostOnline()
{
    try
    {
        await SignIn();
        // Creates the Relay allocation and starts NetworkManager as host for us
        var session = await MultiplayerService.Instance.CreateSessionAsync(new SessionOptions { MaxPlayers = 4 }.WithRelayNetwork());
        JoinCode = session.Code;
        Debug.Log($"[NetworkCommandLine] Hosting online. Join code: {JoinCode}");
    }
    catch (Exception e) { Debug.LogException(e); }
}

async Task JoinOnline(string code)
{
    try
    {
        await SignIn();
        // Finds the session and starts NetworkManager as client for us
        await MultiplayerService.Instance.JoinSessionByCodeAsync(code.Trim().ToUpperInvariant());
        Debug.Log($"[NetworkCommandLine] Joined session {code}");
    }
    catch (Exception e)
    {
        // Wrong/expired/empty code: go back to the menu instead of sitting in an empty scene
        Debug.LogWarning($"[NetworkCommandLine] Couldn't join session '{code}': {e.Message}");
        MenuManager.StartMode = null;
        MenuManager.ErrorMessage = "Couldn't join - check the code and try again.";

        // NetworkManager survives scene loads, so remove it or the next attempt would have two
        networkManager.Shutdown();
        Destroy(networkManager.gameObject);
        SceneManager.LoadScene("MainMenu");
    }
}

// Quick on-screen join code for the host until there's proper UI
void OnGUI()
{
    if (!string.IsNullOrEmpty(JoinCode))
        GUI.Label(new Rect(10, 10, 300, 30), "Join code: " + JoinCode);
}

    static Mode GetCommandLineMode()
    {
        var args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i].Equals("-mode", StringComparison.OrdinalIgnoreCase))
                return ParseMode(args[i + 1]);
        }
        return Mode.None;
    }

#if UNITY_EDITOR
    static Mode GetPlayModeMode()
    {
        var tagMode = ParseMode(string.Join(",", CurrentPlayer.Tags));
        if (tagMode != Mode.None)
            return tagMode;

        return CurrentPlayer.IsMainEditor ? Mode.Host : Mode.Client;
    }
#endif

    static Mode ParseMode(string value)
    {
        if (string.IsNullOrEmpty(value)) return Mode.None;
        value = value.ToLowerInvariant();
        if (value.Contains("server")) return Mode.Server;
        if (value.Contains("host")) return Mode.Host;
        if (value.Contains("client")) return Mode.Client;
        return Mode.None;
    }
}
