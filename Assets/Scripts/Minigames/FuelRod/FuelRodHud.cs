using UnityEngine;

/// <summary>
/// Quick on-screen HUD for the fuel rod minigame: role picking (keys 1/2/3), score, timer, suit,
/// what you're carrying, the interaction prompt and feedback messages. Placeholder until a proper UI exists.
/// </summary>
public class FuelRodHud : MonoBehaviour
{
    [SerializeField] FuelRodMinigame minigame;
    [SerializeField] float feedbackSeconds = 3f;

    [SerializeField] float bannerSeconds = 2.5f;

    string feedback;
    bool feedbackGood;
    float feedbackUntil;

    // Big CORRECT / WRONG banner after an insert, and a "+150" pop when the score changes
    string banner;
    bool bannerGood;
    float bannerUntil;
    int lastScore;
    int scoreDelta;
    float scorePopUntil;

    GUIStyle style, bigStyle, centerStyle, glowStyle;

    void OnEnable()
    {
        minigame.Feedback += OnFeedback;
        minigame.FuelInserted += OnFuelInserted;
    }

    void OnDisable()
    {
        minigame.Feedback -= OnFeedback;
        minigame.FuelInserted -= OnFuelInserted;
    }

    void OnFeedback(string message, bool good)
    {
        feedback = message;
        feedbackGood = good;
        feedbackUntil = Time.time + feedbackSeconds;
    }

    void OnFuelInserted(int slot, FuelType fuel, bool correct)
    {
        banner = correct ? $"CORRECT!  {fuel} installed in {minigame.SlotLabel(slot)}" : $"WRONG!  {minigame.SlotLabel(slot)} didn't need {fuel}";
        bannerGood = correct;
        bannerUntil = Time.time + bannerSeconds;
    }

    void Update()
    {
        if (!minigame.IsSpawned)
            return;

        int score = minigame.Score.Value;
        if (score != lastScore)
        {
            scoreDelta = score - lastScore;
            scorePopUntil = Time.time + bannerSeconds;
            lastScore = score;
        }

        // Roles are agreed between players, then each one presses their key
        bool canPick = minigame.State.Value == Minigame.MinigameState.Waiting || minigame.LocalRole == FuelRodMinigame.Role.None;
        if (!canPick)
            return;
        if (Input.GetKeyDown(KeyCode.Alpha1)) minigame.RequestClaimRole(FuelRodMinigame.Role.Observer);
        if (Input.GetKeyDown(KeyCode.Alpha2)) minigame.RequestClaimRole(FuelRodMinigame.Role.Handler);
        if (Input.GetKeyDown(KeyCode.Alpha3)) minigame.RequestClaimRole(FuelRodMinigame.Role.Decon);
    }

    void OnGUI()
    {
        if (minigame == null || !minigame.IsSpawned)
            return;

        if (style == null)
        {
            style = new GUIStyle(GUI.skin.label) { fontSize = 20, richText = true };
            style.normal.textColor = Color.white;
            bigStyle = new GUIStyle(style) { fontSize = 40, alignment = TextAnchor.MiddleCenter };
            centerStyle = new GUIStyle(style) { fontSize = 24, alignment = TextAnchor.MiddleCenter };
            glowStyle = new GUIStyle(style) { fontSize = 34, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, richText = false };
        }

        var state = minigame.State.Value;
        var role = minigame.LocalRole;

        GUILayout.BeginArea(new Rect(10, 40, 600, 400));
        GUILayout.Label($"Role: <b>{RoleName(role)}</b>    Score: <b>{minigame.Score.Value}</b>    Time: <b>{minigame.TimeRemaining:0}s</b>", style);

        if (state == Minigame.MinigameState.Waiting || role == FuelRodMinigame.Role.None)
        {
            GUILayout.Label("Pick your role:  [1] Observer   [2] Handler   [3] Purifier", style);
            GUILayout.Label($"Observer: {Taken(FuelRodMinigame.Role.Observer)}   Handler: {Taken(FuelRodMinigame.Role.Handler)}   Purifier: {Taken(FuelRodMinigame.Role.Decon)}", style);
        }

        if (state == Minigame.MinigameState.Playing)
        {
            GUILayout.Label($"Rods: {minigame.RodsChanged.Value}/{minigame.RodsToComplete}    Suit: <b>{minigame.SuitIntegrity.Value:0}%</b>", style);

            if (role == FuelRodMinigame.Role.Handler)
                GUILayout.Label($"Carrying: {minigame.CarriedFuel.Value}    Contamination: {minigame.Contamination.Value}", style);

            if (role == FuelRodMinigame.Role.Decon)
                GUILayout.Label($"Handler contamination: {minigame.Contamination.Value}    In chamber: {(minigame.HandlerInDecon.Value ? "yes" : "no")}", style);
        }
        GUILayout.EndArea();

        // Glowing "YOU HAVE ..." across the top while the Handler carries fuel
        if (state == Minigame.MinigameState.Playing && role == FuelRodMinigame.Role.Handler)
        {
            var top = new Rect(0, 20, Screen.width, 50);
            if (minigame.CarriedFuel.Value != FuelType.None)
                GlowLabel(top, $"YOU HAVE {minigame.CarriedFuel.Value.ToString().ToUpper()}", FuelRodGrid.FuelColor(minigame.CarriedFuel.Value));
            else if (minigame.Contamination.Value != FuelType.None)
                GlowLabel(top, minigame.HandlerInDecon.Value ? "IN CHAMBER - WAIT FOR THE PURIFIER" : "CONTAMINATED - GO TO PURIFICATION",
                    new Color(1f, 0.3f, 0.2f));        }

        if (Time.time < bannerUntil)
            GlowLabel(new Rect(0, Screen.height * 0.32f, Screen.width, 60), banner, bannerGood ? new Color(0.45f, 1f, 0.3f) : new Color(1f, 0.3f, 0.25f));

        if (Time.time < scorePopUntil && scoreDelta != 0)
        {
            // Floats upwards as it fades
            float t = 1f - (scorePopUntil - Time.time) / bannerSeconds;
            GlowLabel(new Rect(0, Screen.height * 0.4f - t * 40f, Screen.width, 60), (scoreDelta > 0 ? "+" : "") + scoreDelta,
                scoreDelta > 0 ? new Color(1f, 0.85f, 0.2f) : new Color(1f, 0.3f, 0.25f), 1f - t);
        }

        if (!string.IsNullOrEmpty(PlayerInteractor.Prompt))
            GUI.Label(new Rect(0, Screen.height * 0.6f, Screen.width, 40), PlayerInteractor.Prompt, centerStyle);

        if (Time.time < feedbackUntil)
            GUI.Label(new Rect(0, Screen.height * 0.25f, Screen.width, 40),
                $"<color={(feedbackGood ? "#7CFC00" : "#FF5050")}>{feedback}</color>", centerStyle);

        if (state == Minigame.MinigameState.Completed)
            GUI.Label(new Rect(0, Screen.height * 0.4f, Screen.width, 60), $"<color=#7CFC00>REACTOR STABLE - {minigame.Score.Value} pts</color>", bigStyle);
        else if (state == Minigame.MinigameState.Failed)
            GUI.Label(new Rect(0, Screen.height * 0.4f, Screen.width, 60), $"<color=#FF5050>FAILED: {minigame.EndReason.Value}</color>", bigStyle);
    }

    // Text with a soft pulsing halo in its own colour
    void GlowLabel(Rect rect, string text, Color color, float alpha = 1f)
    {
        float pulse = 0.75f + 0.25f * Mathf.Sin(Time.time * 4f);
        var halo = color;
        halo.a = 0.25f * pulse * alpha;
        glowStyle.normal.textColor = halo;
        for (int x = -3; x <= 3; x += 3)
            for (int y = -3; y <= 3; y += 3)
                if (x != 0 || y != 0)
                    GUI.Label(new Rect(rect.x + x, rect.y + y, rect.width, rect.height), text, glowStyle);

        color.a = alpha;
        glowStyle.normal.textColor = color;
        GUI.Label(rect, text, glowStyle);
    }

    string Taken(FuelRodMinigame.Role r) => minigame.IsRoleTaken(r) ? "<color=#7CFC00>taken</color>" : "<color=#FFD700>open</color>";

    static string RoleName(FuelRodMinigame.Role r) => r switch
    {
        FuelRodMinigame.Role.Decon => "Purifier",
        FuelRodMinigame.Role.None => "-",
        _ => r.ToString(),
    };
}
