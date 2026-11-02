using System;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Base class for every minigame. Handles the countdown, score and win/lose state, all server-authoritative.
/// Put one subclass (e.g. FuelRodMinigame) on a NetworkObject in each minigame scene.
///
/// Subclasses call StartMinigame() when ready, AddScore() as things happen, and Complete()/Fail() to finish.
/// Running out of time fails the minigame automatically.
/// </summary>
public abstract class Minigame : NetworkBehaviour
{
    public enum MinigameState { Waiting, Playing, Completed, Failed }

    public static Minigame Current { get; private set; }

    [Header("Timer")]
    [Tooltip("Seconds the team has to finish this minigame.")]
    [SerializeField] protected float timeLimit = 180f;

    public readonly NetworkVariable<MinigameState> State = new NetworkVariable<MinigameState>(MinigameState.Waiting);
    public readonly NetworkVariable<int> Score = new NetworkVariable<int>();
    public readonly NetworkVariable<FixedString128Bytes> EndReason = new NetworkVariable<FixedString128Bytes>();

    // Server time the minigame ends at. Clients work out the countdown from this, so nothing is sent every frame.
    readonly NetworkVariable<double> endTime = new NetworkVariable<double>();

    /// <summary>Fires on every client/host when the minigame ends. (success, reason)</summary>
    public event Action<bool, string> Ended;

    public float TimeRemaining =>
        State.Value == MinigameState.Playing
            ? Mathf.Max(0f, (float)(endTime.Value - NetworkManager.ServerTime.Time))
            : State.Value == MinigameState.Waiting ? timeLimit : 0f;

    public override void OnNetworkSpawn()
    {
        Current = this;
        State.OnValueChanged += OnStateChanged;
    }

    public override void OnNetworkDespawn()
    {
        State.OnValueChanged -= OnStateChanged;
        if (Current == this)
            Current = null;
    }

    protected virtual void Update()
    {
        if (!IsServer || State.Value != MinigameState.Playing)
            return;

        if (NetworkManager.ServerTime.Time >= endTime.Value)
            Fail("Ran out of time");
    }

    protected void StartMinigame()
    {
        if (!IsServer || State.Value != MinigameState.Waiting)
            return;

        endTime.Value = NetworkManager.ServerTime.Time + timeLimit;
        State.Value = MinigameState.Playing;
        OnMinigameStarted();
    }

    /// <summary>Called on the server when the timer starts.</summary>
    protected virtual void OnMinigameStarted() { }

    protected void AddScore(int amount)
    {
        if (IsServer)
            Score.Value += amount;
    }

    protected void Complete(string reason = "Complete") => End(true, reason);

    protected void Fail(string reason) => End(false, reason);

    void End(bool success, string reason)
    {
        if (!IsServer || State.Value != MinigameState.Playing)
            return;

        EndReason.Value = reason;
        State.Value = success ? MinigameState.Completed : MinigameState.Failed;
    }

    void OnStateChanged(MinigameState previous, MinigameState current)
    {
        if (current == MinigameState.Completed || current == MinigameState.Failed)
            Ended?.Invoke(current == MinigameState.Completed, EndReason.Value.ToString());
    }
}
