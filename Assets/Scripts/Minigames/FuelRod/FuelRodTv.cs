using TMPro;
using UnityEngine;

/// <summary>
/// The viewing room TV for the fuel rod minigame. Shows which slot needs a new rod and which fuel goes in it,
/// so the Observer can direct the Handler. Reads FuelRodMinigame's synced state, so it's the same on every client.
///
/// Put this on a world-space TextMeshPro sitting on the TV screen and assign the minigame.
/// </summary>
public class FuelRodTv : MonoBehaviour
{
    [SerializeField] FuelRodMinigame minigame;
    [SerializeField] TMP_Text screen;

    static readonly Color Dim = new Color(0.55f, 0.75f, 0.72f);
    static readonly Color Good = new Color(0.45f, 1f, 0.3f);
    static readonly Color Bad = new Color(1f, 0.3f, 0.25f);

    // Only rebuild the text when something it shows changes
    string shownKey;

    void Update()
    {
        if (minigame == null || !minigame.IsSpawned)
        {
            Show("off", Header("REACTOR CONTROL") + Line("NO SIGNAL", Dim));
            return;
        }

        var state = minigame.State.Value;
        int slot = minigame.TargetSlot.Value;
        var fuel = minigame.TargetFuel.Value;
        int rods = minigame.RodsChanged.Value;
        string key = $"{state}|{slot}|{fuel}|{rods}";
        if (key == shownKey)
            return;

        string text = state switch
        {
            Minigame.MinigameState.Waiting => Header("REACTOR CONTROL") + Line("Awaiting crew...", Dim),
            Minigame.MinigameState.Completed => Header("REACTOR CONTROL") + Big("REACTOR STABLE", Good),
            Minigame.MinigameState.Failed => Header("REACTOR CONTROL") + Big("REACTOR FAILURE", Bad),
            _ when slot < 0 => Header($"RODS {rods}/{minigame.RodsToComplete}") + Big("ALL RODS REPLACED", Good) + Line("Decontaminate the Handler", Dim),
            _ => Header($"ROD {rods + 1} OF {minigame.RodsToComplete}")
                 + Line("REPLACE ROD", Dim)
                 + Big(minigame.SlotLabel(slot), Color.white)
                 + Line("WITH", Dim)
                 + Big(fuel.ToString().ToUpper(), FuelRodGrid.FuelColor(fuel)),
        };
        Show(key, text);
    }

    void Show(string key, string text)
    {
        if (key == shownKey)
            return;
        shownKey = key;
        screen.text = text;
    }

    static string Header(string text) => $"<size=60%><color=#{ColorUtility.ToHtmlStringRGB(Dim)}>{text}</color></size>\n";
    static string Line(string text, Color color) => $"<size=70%><color=#{ColorUtility.ToHtmlStringRGB(color)}>{text}</color></size>\n";
    static string Big(string text, Color color) => $"<b><color=#{ColorUtility.ToHtmlStringRGB(color)}>{text}</color></b>\n";
}
