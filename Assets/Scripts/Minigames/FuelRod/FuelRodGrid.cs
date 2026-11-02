using System.Collections;
using TMPro;
using UnityEngine;

/// <summary>
/// Builds the fuel rod floor grid at runtime (identical on every client): a circular pad per cell with a fuel
/// chamber (tube) hidden underneath. The Handler raises a chamber, inserts fuel, and it sinks back down.
/// Visual only - FuelRodMinigame owns the rules.
///
/// Place this object at the centre of the fuel rod area. Columns run along its X axis (1, 2, 3...),
/// rows along its Z axis (A at the +Z edge).
/// </summary>
public class FuelRodGrid : MonoBehaviour
{
    public static FuelRodGrid Instance { get; private set; }

    [SerializeField] FuelRodMinigame minigame;

    [Header("Layout")]
    [SerializeField] float cellSize = 1.6f;
    [SerializeField] float padDiameter = 1f;
    [SerializeField] float tubeDiameter = 0.6f;
    [SerializeField] float tubeHeight = 1.4f;

    [Header("Tube animation")]
    [SerializeField] float raiseSeconds = 0.8f;
    [SerializeField] float holdSeconds = 1.2f;

    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    static readonly Color PadColor = new Color(0.15f, 0.55f, 0.6f);
    static readonly Color WrongColor = new Color(0.9f, 0.15f, 0.1f);
    static readonly Color EmptyTubeColor = new Color(0.75f, 0.78f, 0.8f);

    int shownRaised = -1;
    Renderer[] pads;
    Renderer[] tubes;
    Color[] padColors;
    Coroutine[] running;
    MaterialPropertyBlock block;

    public int Rows => minigame.Rows;
    public int Columns => minigame.Columns;

    public static Color FuelColor(FuelType fuel) => fuel switch
    {
        FuelType.Uranium => new Color(0.35f, 0.95f, 0.2f),
        FuelType.Plutonium => new Color(0.65f, 0.3f, 0.95f),
        FuelType.Tritium => new Color(0.2f, 0.7f, 1f),
        _ => Color.grey,
    };

    void Awake()
    {
        Instance = this;
        block = new MaterialPropertyBlock();
        Build();
    }

    void OnEnable() => minigame.FuelInserted += OnFuelInserted;
    void OnDisable() => minigame.FuelInserted -= OnFuelInserted;

    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    public Vector3 LocalSlotPosition(int slot)
    {
        int row = slot / Columns, col = slot % Columns;
        return new Vector3((col - (Columns - 1) / 2f) * cellSize, 0f, ((Rows - 1) / 2f - row) * cellSize);
    }

    public Vector3 SlotPosition(int slot) => transform.TransformPoint(LocalSlotPosition(slot));

    // ------------------------------------------------------------------ building

    void Build()
    {
        int count = Rows * Columns;
        pads = new Renderer[count];
        tubes = new Renderer[count];
        padColors = new Color[count];
        running = new Coroutine[count];

        for (int i = 0; i < count; i++)
        {
            var local = LocalSlotPosition(i);

            // Pad: flat disc with a tall trigger so it's easy to look at and walk through
            var pad = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            pad.name = "Pad " + minigame.SlotLabel(i);
            pad.transform.SetParent(transform, false);
            pad.transform.localPosition = local + Vector3.up * 0.01f;
            pad.transform.localScale = new Vector3(padDiameter, 0.01f, padDiameter);
            Destroy(pad.GetComponent<Collider>());
            var trigger = pad.AddComponent<BoxCollider>();
            trigger.isTrigger = true;
            trigger.center = new Vector3(0f, 25f, 0f);  // local y is scaled by 0.01 -> 0.5m tall
            trigger.size = new Vector3(1f, 50f, 1f);
            pad.AddComponent<FuelRodPad>().Slot = i;
            pads[i] = pad.GetComponent<Renderer>();
            padColors[i] = PadColor;
            SetColor(pads[i], PadColor);

            // Tube: hidden below the floor until fuel goes in
            var tube = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            tube.name = "Tube " + minigame.SlotLabel(i);
            tube.transform.SetParent(transform, false);
            tube.transform.localScale = new Vector3(tubeDiameter, tubeHeight / 2f, tubeDiameter);
            tube.transform.localPosition = TubeLowered(i);
            tube.GetComponent<Collider>().isTrigger = true; // lets the Handler aim at a raised chamber
            tube.AddComponent<FuelRodPad>().Slot = i;
            tubes[i] = tube.GetComponent<Renderer>();
            tubes[i].enabled = false;
        }

        // Battleship-style labels: letters down one side, numbers along the top
        float halfW = Columns * cellSize / 2f, halfD = Rows * cellSize / 2f;
        for (int c = 0; c < Columns; c++)
            AddLabel((c + 1).ToString(), new Vector3(LocalSlotPosition(c).x, 0.02f, halfD + cellSize * 0.4f));
        for (int r = 0; r < Rows; r++)
            AddLabel(((char)('A' + r)).ToString(), new Vector3(-halfW - cellSize * 0.4f, 0.02f, LocalSlotPosition(r * Columns).z));
    }

    void AddLabel(string text, Vector3 localPos)
    {
        var go = new GameObject("Label " + text, typeof(RectTransform));
        go.transform.SetParent(transform, false);
        go.transform.localPosition = localPos;
        go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f); // lying flat, readable from above
        var tmp = go.AddComponent<TextMeshPro>();
        tmp.text = text;
        tmp.fontSize = 6f;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = Color.white;
        ((RectTransform)go.transform).sizeDelta = new Vector2(1.5f, 1.5f);
    }

    Vector3 TubeLowered(int slot) => LocalSlotPosition(slot) + Vector3.down * (tubeHeight / 2f + 0.05f);
    Vector3 TubeRaised(int slot) => LocalSlotPosition(slot) + Vector3.up * (tubeHeight / 2f);

    void SetColor(Renderer r, Color c)
    {
        r.GetPropertyBlock(block);
        block.SetColor(BaseColorId, c);
        r.SetPropertyBlock(block);
    }

    // ------------------------------------------------------------------ chamber animation

    // Follows FuelRodMinigame.RaisedSlot (polled, so late joiners and dropped events still end up right)
    void Update()
    {
        int target = minigame.IsSpawned ? minigame.RaisedSlot.Value : -1;
        if (target == shownRaised)
            return;

        // Cleared to -1 means fuel just went in: hold it up a moment so everyone sees it, then sink
        if (shownRaised >= 0)
            Animate(shownRaised, raise: false, delay: target < 0 ? holdSeconds : 0f);
        if (target >= 0)
        {
            SetColor(tubes[target], EmptyTubeColor);
            Animate(target, raise: true, delay: 0f);
        }
        shownRaised = target;
    }

    void OnFuelInserted(int slot, FuelType fuel, bool correct)
    {
        if (slot < 0 || slot >= tubes.Length)
            return;

        SetColor(tubes[slot], FuelColor(fuel)); // the fuel is now in the chamber
        if (correct)
        {
            padColors[slot] = FuelColor(fuel);   // installed rods leave their pad lit in the fuel's colour
            SetColor(pads[slot], padColors[slot]);
        }
        else
        {
            StartCoroutine(FlashWrong(slot));
        }
    }

    IEnumerator FlashWrong(int slot)
    {
        SetColor(pads[slot], WrongColor);
        yield return new WaitForSeconds(holdSeconds + raiseSeconds);
        SetColor(pads[slot], padColors[slot]);
    }

    void Animate(int slot, bool raise, float delay)
    {
        if (running[slot] != null)
            StopCoroutine(running[slot]);
        running[slot] = StartCoroutine(Slide(slot, raise, delay));
    }

    IEnumerator Slide(int slot, bool raise, float delay)
    {
        if (delay > 0f)
            yield return new WaitForSeconds(delay);

        var tube = tubes[slot];
        tube.enabled = true;
        Vector3 from = tube.transform.localPosition;
        Vector3 to = raise ? TubeRaised(slot) : TubeLowered(slot);
        for (float time = 0f; time < raiseSeconds; time += Time.deltaTime)
        {
            tube.transform.localPosition = Vector3.Lerp(from, to, Mathf.SmoothStep(0f, 1f, time / raiseSeconds));
            yield return null;
        }
        tube.transform.localPosition = to;
        tube.enabled = raise;
        running[slot] = null;
    }
}
