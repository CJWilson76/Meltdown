using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using Random = UnityEngine.Random;

/// <summary>
/// Minigame 1: fuel rod change. Three roles:
///   Observer - sees the highlighted grid slot (and the fuel it needs) and directs the Handler.
///   Handler  - picks up fuel, inserts it in the right slot, then MUST be decontaminated before the next rod.
///   Decon    - applies purification chemicals (they cost points). The wrong one leaves the Handler contaminated
///              and their suit keeps decaying; at 0 the Handler dies and the minigame fails.
///
/// Put this on a NetworkObject in the minigame scene. Interaction code calls the public Request* methods;
/// all rules are checked on the server.
/// </summary>
public class FuelRodMinigame : Minigame
{
    public enum Role { None, Observer, Handler, Decon }

    [Serializable]
    public class DeconChemical
    {
        public string name;
        public FuelType neutralises;
        public int cost;
    }

    public static new FuelRodMinigame Current => Minigame.Current as FuelRodMinigame;

    const ulong NoClient = ulong.MaxValue;

    [Header("Grid")]
    [SerializeField] int rows = 8;      // A-H
    [SerializeField] int columns = 8;   // 1-8

    [Header("Goal")]
    [SerializeField] int rodsToComplete = 5;

    [Header("Scoring")]
    [SerializeField] int pointsPerRod = 100;
    [Tooltip("Extra points if the rod goes in within this many seconds of the slot being highlighted.")]
    [SerializeField] float bonusWindow = 30f;
    [SerializeField] int bonusPoints = 50;
    [SerializeField] int wrongInsertPenalty = 25;

    [Header("Handler suit")]
    [Tooltip("Suit integrity lost per second while the Handler is contaminated. Walls don't help - it's on the suit.")]
    [SerializeField] float suitDecayPerSecond = 2f;
    [Tooltip("Suit integrity lost per second while a clean Handler has line of sight to the fuel rod grid. Walls, floors and closed doors block it.")]
    [SerializeField] float gridExposurePerSecond = 1f;
    [Tooltip("How far the fuel rod grid's radiation reaches.")]
    [SerializeField] float gridExposureRange = 15f;
    [Tooltip("Instant suit damage when the wrong chemical is used.")]
    [SerializeField] float wrongChemicalDamage = 10f;
    [SerializeField] bool restoreSuitOnDecon = true;

    [Header("Decon chemicals")]
    [SerializeField] List<DeconChemical> chemicals = new List<DeconChemical>
    {
        new DeconChemical { name = "Sodium Bicarbonate", neutralises = FuelType.Uranium, cost = 20 },
        new DeconChemical { name = "DTPA Solution", neutralises = FuelType.Plutonium, cost = 30 },
        new DeconChemical { name = "Water Flush", neutralises = FuelType.Tritium, cost = 10 },
    };

    [Header("Testing")]
    [Tooltip("Start the timer as soon as the scene loads instead of waiting for all three roles to be claimed.")]
    [SerializeField] bool startWithoutAllRoles = false;
    [Tooltip("Let one player hold several roles (solo testing).")]
    [SerializeField] bool allowMultipleRolesPerPlayer = false;

    // Roles (client ids)
    public readonly NetworkVariable<ulong> ObserverClient = new NetworkVariable<ulong>(NoClient);
    public readonly NetworkVariable<ulong> HandlerClient = new NetworkVariable<ulong>(NoClient);
    public readonly NetworkVariable<ulong> DeconClient = new NetworkVariable<ulong>(NoClient);

    // Current job
    public readonly NetworkVariable<int> TargetSlot = new NetworkVariable<int>(-1);
    public readonly NetworkVariable<FuelType> TargetFuel = new NetworkVariable<FuelType>();
    public readonly NetworkVariable<int> RodsChanged = new NetworkVariable<int>();
    readonly NetworkVariable<double> targetShownAt = new NetworkVariable<double>();

    // Handler state
    public readonly NetworkVariable<FuelType> CarriedFuel = new NetworkVariable<FuelType>();
    public readonly NetworkVariable<FuelType> Contamination = new NetworkVariable<FuelType>();
    public readonly NetworkVariable<float> SuitIntegrity = new NetworkVariable<float>(100f);
    public readonly NetworkVariable<bool> HandlerInDecon = new NetworkVariable<bool>();

    /// <summary>True while a clean Handler can see the fuel rod grid and their suit is decaying from it.</summary>
    public readonly NetworkVariable<bool> HandlerExposed = new NetworkVariable<bool>();

    /// <summary>The slot whose fuel chamber the Handler has raised (-1 = none). Fuel can only go into a raised chamber.</summary>
    public readonly NetworkVariable<int> RaisedSlot = new NetworkVariable<int>(-1);

    /// <summary>Fires on all clients for feedback (sounds, UI pop-ups). (message, isGood)</summary>
    public event Action<string, bool> Feedback;

    /// <summary>Fires on all clients when the Handler puts fuel into a slot (drives the tube animation). (slot, fuel, correct)</summary>
    public event Action<int, FuelType, bool> FuelInserted;

    public int Rows => rows;
    public int Columns => columns;
    public int RodsToComplete => rodsToComplete;
    public IReadOnlyList<DeconChemical> Chemicals => chemicals;

    public static string SlotLabel(int index, int columns) =>
        index < 0 ? "-" : $"{(char)('A' + index / columns)}{index % columns + 1}";

    public string SlotLabel(int index) => SlotLabel(index, columns);

    public Role GetRole(ulong clientId)
    {
        if (ObserverClient.Value == clientId) return Role.Observer;
        if (HandlerClient.Value == clientId) return Role.Handler;
        if (DeconClient.Value == clientId) return Role.Decon;
        return Role.None;
    }

    public bool HasRole(ulong clientId, Role role) => RoleVar(role) is { } v && v.Value == clientId;

    public bool IsRoleTaken(Role role) => RoleVar(role) is { } v && v.Value != NoClient;

    public Role LocalRole => GetRole(NetworkManager.LocalClientId);

    // ------------------------------------------------------------------ lifecycle

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        if (IsServer)
        {
            NetworkManager.OnClientDisconnectCallback += OnClientDisconnected;
            if (startWithoutAllRoles)
                StartMinigame();
        }
    }

    public override void OnNetworkDespawn()
    {
        if (IsServer && NetworkManager != null)
            NetworkManager.OnClientDisconnectCallback -= OnClientDisconnected;
        base.OnNetworkDespawn();
    }

    protected override void OnMinigameStarted()
    {
        RodsChanged.Value = 0;
        SuitIntegrity.Value = 100f;
        CarriedFuel.Value = FuelType.None;
        Contamination.Value = FuelType.None;
        RaisedSlot.Value = -1;
        PickNewTarget();
    }

    protected override void Update()
    {
        base.Update();
        if (!IsServer || State.Value != MinigameState.Playing)
            return;

        bool contaminated = Contamination.Value != FuelType.None || CarriedFuel.Value != FuelType.None;
        bool exposed = !contaminated && HandlerCanSeeGrid();
        if (HandlerExposed.Value != exposed)
            HandlerExposed.Value = exposed;

        if (contaminated)
            DamageSuit(suitDecayPerSecond * Time.deltaTime);
        else if (exposed)
            DamageSuit(gridExposurePerSecond * Time.deltaTime);
    }

    // ------------------------------------------------------------------ grid radiation

    static readonly RaycastHit[] exposureHits = new RaycastHit[16];

    // Server only. Checks a few points on the grid (its centre, corners and the slot nearest the Handler);
    // the Handler is exposed if any of them is in range with nothing solid in between.
    bool HandlerCanSeeGrid()
    {
        var grid = FuelRodGrid.Instance;
        if (grid == null || !NetworkManager.ConnectedClients.TryGetValue(HandlerClient.Value, out var client) || client.PlayerObject == null)
            return false;

        var player = client.PlayerObject.transform;
        var chest = player.position + Vector3.up * 1.2f;

        int count = rows * columns, nearest = 0;
        float nearestDist = float.MaxValue;
        for (int i = 0; i < count; i++)
        {
            float d = (grid.SlotPosition(i) - chest).sqrMagnitude;
            if (d < nearestDist) { nearestDist = d; nearest = i; }
        }

        return Visible(chest, grid.SlotPosition(nearest), player)
            || Visible(chest, grid.transform.position, player)
            || Visible(chest, grid.SlotPosition(0), player)
            || Visible(chest, grid.SlotPosition(columns - 1), player)
            || Visible(chest, grid.SlotPosition(count - columns), player)
            || Visible(chest, grid.SlotPosition(count - 1), player);
    }

    bool Visible(Vector3 from, Vector3 gridPoint, Transform player)
    {
        var to = gridPoint + Vector3.up * 0.3f; // just above the floor so the floor itself doesn't count
        var delta = to - from;
        if (delta.sqrMagnitude > gridExposureRange * gridExposureRange)
            return false;

        int n = Physics.RaycastNonAlloc(from, delta.normalized, exposureHits, delta.magnitude, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++)
        {
            var col = exposureHits[i].collider;
            // People don't shield radiation - ignore the Handler and any other player in the way
            if (col.transform.IsChildOf(player) || (col.attachedRigidbody != null && col.attachedRigidbody.GetComponent<Movement>() != null))
                continue;
            return false;
        }
        return true;
    }

    // ------------------------------------------------------------------ public API for scene objects
    // Call these from the local player's interaction code. They are safe to call from any client.

    public void RequestClaimRole(Role role) => ClaimRoleRpc(role);
    public void RequestReleaseRole() => ReleaseRoleRpc();
    public void RequestPickUpFuel(FuelType fuel) => PickUpFuelRpc(fuel);
    public void RequestRaiseChamber(int slotIndex) => RaiseChamberRpc(slotIndex);
    public void RequestInsertFuel(int slotIndex) => InsertFuelRpc(slotIndex);
    public void RequestApplyChemical(int chemicalIndex) => ApplyChemicalRpc(chemicalIndex);

    /// <summary>Server only. Call when the Handler walks in/out of the purification chamber (e.g. from a trigger).</summary>
    public void SetHandlerInDecon(bool inside)
    {
        if (IsServer)
            HandlerInDecon.Value = inside;
    }

    // ------------------------------------------------------------------ roles

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    void ClaimRoleRpc(Role role, RpcParams rpcParams = default)
    {
        ulong sender = rpcParams.Receive.SenderClientId;
        var roleVar = RoleVar(role);
        if (roleVar == null || roleVar.Value == sender)
            return;

        if (roleVar.Value != NoClient)
        {
            SendFeedback(sender, $"{role} is already taken", false);
            return;
        }

        if (!allowMultipleRolesPerPlayer)
            ClearRolesOf(sender);

        roleVar.Value = sender;
        BroadcastFeedbackRpc($"Player {sender} is the {role}", true);

        if (State.Value == MinigameState.Waiting && AllRolesFilled())
            StartMinigame();
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    void ReleaseRoleRpc(RpcParams rpcParams = default)
    {
        ClearRolesOf(rpcParams.Receive.SenderClientId);
    }

    void OnClientDisconnected(ulong clientId) => ClearRolesOf(clientId);

    void ClearRolesOf(ulong clientId)
    {
        foreach (var v in new[] { ObserverClient, HandlerClient, DeconClient })
            if (v.Value == clientId)
                v.Value = NoClient;
    }

    bool AllRolesFilled() =>
        ObserverClient.Value != NoClient && HandlerClient.Value != NoClient && DeconClient.Value != NoClient;

    NetworkVariable<ulong> RoleVar(Role role) => role switch
    {
        Role.Observer => ObserverClient,
        Role.Handler => HandlerClient,
        Role.Decon => DeconClient,
        _ => null,
    };

    // ------------------------------------------------------------------ handler actions

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    void PickUpFuelRpc(FuelType fuel, RpcParams rpcParams = default)
    {
        ulong sender = rpcParams.Receive.SenderClientId;
        if (!CanAct(sender, Role.Handler) || fuel == FuelType.None)
            return;

        if (Contamination.Value != FuelType.None)
        {
            SendFeedback(sender, "You must be decontaminated first!", false);
            return;
        }
        if (CarriedFuel.Value != FuelType.None)
        {
            SendFeedback(sender, $"Already carrying {CarriedFuel.Value}", false);
            return;
        }

        CarriedFuel.Value = fuel;
        Contamination.Value = fuel; // handling the rod contaminates the suit
        SendFeedback(sender, $"Picked up {fuel}", true);
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    void RaiseChamberRpc(int slotIndex, RpcParams rpcParams = default)
    {
        ulong sender = rpcParams.Receive.SenderClientId;
        if (!CanAct(sender, Role.Handler) || slotIndex < 0 || slotIndex >= rows * columns)
            return;

        if (CarriedFuel.Value == FuelType.None)
        {
            SendFeedback(sender, "Get fuel before raising a chamber", false);
            return;
        }

        RaisedSlot.Value = slotIndex; // any previously raised chamber goes back down
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    void InsertFuelRpc(int slotIndex, RpcParams rpcParams = default)
    {
        ulong sender = rpcParams.Receive.SenderClientId;
        if (!CanAct(sender, Role.Handler))
            return;

        if (CarriedFuel.Value == FuelType.None)
        {
            SendFeedback(sender, "You're not carrying any fuel", false);
            return;
        }
        if (slotIndex != RaisedSlot.Value)
        {
            SendFeedback(sender, $"Raise the {SlotLabel(slotIndex)} fuel chamber first", false);
            return;
        }

        var carried = CarriedFuel.Value;
        CarriedFuel.Value = FuelType.None;
        RaisedSlot.Value = -1;

        bool correct = slotIndex == TargetSlot.Value && carried == TargetFuel.Value;
        FuelInsertedRpc(slotIndex, carried, correct);

        if (!correct)
        {
            AddScore(-wrongInsertPenalty);
            BroadcastFeedbackRpc($"Wrong {(slotIndex != TargetSlot.Value ? "slot" : "fuel")}! -{wrongInsertPenalty}", false);
            return;
        }

        int points = pointsPerRod;
        bool bonus = NetworkManager.ServerTime.Time - targetShownAt.Value <= bonusWindow;
        if (bonus)
            points += bonusPoints;

        AddScore(points);
        RodsChanged.Value++;
        BroadcastFeedbackRpc($"{carried} rod installed in {SlotLabel(slotIndex)}! +{points}{(bonus ? " (speed bonus)" : "")}", true);

        if (RodsChanged.Value < rodsToComplete)
            PickNewTarget();
        else
            TargetSlot.Value = -1; // all rods done - finish once the Handler is decontaminated
    }

    // ------------------------------------------------------------------ decon actions

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    void ApplyChemicalRpc(int chemicalIndex, RpcParams rpcParams = default)
    {
        ulong sender = rpcParams.Receive.SenderClientId;
        if (!CanAct(sender, Role.Decon) || chemicalIndex < 0 || chemicalIndex >= chemicals.Count)
            return;

        if (!HandlerInDecon.Value)
        {
            SendFeedback(sender, "The Handler isn't in the purification chamber", false);
            return;
        }
        if (Contamination.Value == FuelType.None)
        {
            SendFeedback(sender, "The Handler is already clean", false);
            return;
        }
        if (CarriedFuel.Value != FuelType.None)
        {
            SendFeedback(sender, "The Handler has to install their fuel first", false);
            return;
        }

        var chemical = chemicals[chemicalIndex];
        AddScore(-chemical.cost);

        if (chemical.neutralises != Contamination.Value)
        {
            DamageSuit(wrongChemicalDamage);
            BroadcastFeedbackRpc($"{chemical.name} had no effect! Suit still decaying (-{chemical.cost})", false);
            return;
        }

        Contamination.Value = FuelType.None;
        if (restoreSuitOnDecon)
            SuitIntegrity.Value = 100f;
        BroadcastFeedbackRpc($"Handler decontaminated with {chemical.name} (-{chemical.cost})", true);

        if (RodsChanged.Value >= rodsToComplete && CarriedFuel.Value == FuelType.None)
            Complete("All fuel rods changed");
    }

    // ------------------------------------------------------------------ helpers

    bool CanAct(ulong sender, Role role)
    {
        if (State.Value != MinigameState.Playing)
            return false;
        if (!HasRole(sender, role))
        {
            SendFeedback(sender, $"Only the {role} can do that", false);
            return false;
        }
        return true;
    }

    void PickNewTarget()
    {
        int slotCount = rows * columns;
        int slot;
        do slot = Random.Range(0, slotCount);
        while (slotCount > 1 && slot == TargetSlot.Value);

        TargetSlot.Value = slot;
        TargetFuel.Value = (FuelType)Random.Range(1, Enum.GetValues(typeof(FuelType)).Length);
        targetShownAt.Value = NetworkManager.ServerTime.Time;
    }

    void DamageSuit(float amount)
    {
        SuitIntegrity.Value = Mathf.Max(0f, SuitIntegrity.Value - amount);
        if (SuitIntegrity.Value <= 0f)
            Fail("The Handler's suit failed - radiation poisoning");
    }

    void SendFeedback(ulong clientId, string message, bool good) =>
        FeedbackRpc(message, good, RpcTarget.Single(clientId, RpcTargetUse.Temp));

    [Rpc(SendTo.SpecifiedInParams)]
    void FeedbackRpc(string message, bool good, RpcParams rpcParams) => Feedback?.Invoke(message, good);

    [Rpc(SendTo.ClientsAndHost)]
    void BroadcastFeedbackRpc(string message, bool good) => Feedback?.Invoke(message, good);

    [Rpc(SendTo.ClientsAndHost)]
    void FuelInsertedRpc(int slot, FuelType fuel, bool correct) => FuelInserted?.Invoke(slot, fuel, correct);
}
