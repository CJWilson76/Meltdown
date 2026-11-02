using System.Linq;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Local player's "look at it and press E" interaction: fuel stations (pick up), grid pads (insert) and decon buttons.
/// Only runs on your own player. The HUD shows <see cref="Prompt"/>.
/// </summary>
public class PlayerInteractor : NetworkBehaviour
{
    [SerializeField] float reach = 3.5f;
    [SerializeField] KeyCode interactKey = KeyCode.E;

    public static string Prompt { get; private set; } = "";

    Camera cam;

    public override void OnNetworkSpawn()
    {
        if (!IsOwner)
            enabled = false;
    }

    public override void OnNetworkDespawn()
    {
        if (IsOwner)
            Prompt = "";
    }

    void Start() => cam = GetComponentInChildren<Camera>();

    void Update()
    {
        Prompt = "";
        var game = FuelRodMinigame.Current;
        if (game == null || cam == null || game.State.Value != Minigame.MinigameState.Playing)
            return;

        var hits = Physics.RaycastAll(cam.transform.position, cam.transform.forward, reach, ~0, QueryTriggerInteraction.Collide)
            .OrderBy(h => h.distance);

        foreach (var hit in hits)
        {
            if (hit.transform.IsChildOf(transform))
                continue; // our own body

            if (hit.collider.TryGetComponent(out FuelStation station))
            {
                LookAtStation(game, station);
                return;
            }
            if (hit.collider.TryGetComponent(out FuelRodPad pad))
            {
                LookAtPad(game, pad);
                return;
            }
            if (hit.collider.GetComponentInParent<DeconButton>() is { } button)
            {
                LookAtDeconButton(game, button);
                return;
            }
            if (!hit.collider.isTrigger)
                return; // a wall or floor is in the way
        }
    }

    void LookAtStation(FuelRodMinigame game, FuelStation station)
    {
        if (game.LocalRole != FuelRodMinigame.Role.Handler)
        {
            Prompt = $"{station.Fuel} - only the Handler can take fuel";
            return;
        }
        if (game.CarriedFuel.Value != FuelType.None)
        {
            Prompt = $"Already carrying {game.CarriedFuel.Value}";
            return;
        }

        Prompt = $"[{interactKey}] Pick up {station.Fuel}";
        if (Input.GetKeyDown(interactKey))
            game.RequestPickUpFuel(station.Fuel);
    }

    void LookAtPad(FuelRodMinigame game, FuelRodPad pad)
    {
        string label = game.SlotLabel(pad.Slot);
        if (game.LocalRole != FuelRodMinigame.Role.Handler || game.CarriedFuel.Value == FuelType.None)
        {
            Prompt = label;
            return;
        }

        // Step 1: raise this pad's fuel chamber. Step 2: put the fuel in.
        bool raised = game.RaisedSlot.Value == pad.Slot;
        Prompt = raised
            ? $"[{interactKey}] Insert {game.CarriedFuel.Value} into {label}"
            : $"[{interactKey}] Raise fuel chamber {label}";

        if (!Input.GetKeyDown(interactKey))
            return;
        if (raised)
            game.RequestInsertFuel(pad.Slot);
        else
            game.RequestRaiseChamber(pad.Slot);
    }

    void LookAtDeconButton(FuelRodMinigame game, DeconButton button)
    {
        if (!game.HasRole(NetworkManager.LocalClientId, FuelRodMinigame.Role.Decon))
        {
            Prompt = "Decon button - only the Purifier can use this";
            return;
        }
        if (!game.HandlerInDecon.Value)
        {
            Prompt = "Decon button - waiting for the Handler to enter the chamber";
            return;
        }
        if (game.Contamination.Value == FuelType.None)
        {
            Prompt = "Decon button - the Handler is clean";
            return;
        }

        // One number key per chemical: [1] first in the list, [2] second...
        var chemicals = game.Chemicals;
        var options = new string[chemicals.Count];
        for (int i = 0; i < chemicals.Count; i++)
            options[i] = $"[{i + 1}] {chemicals[i].name} (-{chemicals[i].cost})";
        Prompt = string.Join("    ", options);

        for (int i = 0; i < chemicals.Count && i < 9; i++)
        {
            if (!Input.GetKeyDown(KeyCode.Alpha1 + i))
                continue;
            button.Press();
            game.RequestApplyChemical(i);
            return;
        }
    }
}
