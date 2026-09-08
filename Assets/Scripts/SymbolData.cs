using UnityEngine;

/// <summary>
/// Data container describing a single slot machine symbol: its display name,
/// artwork, how often it should appear, and how much it pays out on a win.
/// This is a pure data asset with no logic — create one instance per symbol
/// via the Assets menu and reference it from your reel/spin logic.
/// </summary>
[CreateAssetMenu(fileName = "NewSymbolData", menuName = "Slot Machine/Symbol Data")]
public class SymbolData : ScriptableObject
{
    /// <summary>
    /// The display/reference name of this symbol (e.g. "Cherry", "Bar", "Seven").
    /// </summary>
    [Tooltip("The name of this symbol, e.g. \"Cherry\" or \"Lucky Seven\". Used to identify it in lists and code.")]
    public string symbolName;

    /// <summary>
    /// The artwork shown on the reel for this symbol.
    /// </summary>
    [Tooltip("The picture that appears on the reel for this symbol.")]
    public Sprite symbolSprite;

    /// <summary>
    /// Relative weight used when randomly picking a symbol. Higher values make
    /// the symbol land more often; weights are relative to each other, not
    /// percentages.
    /// </summary>
    [Tooltip("How likely this symbol is to show up, compared to the other symbols. Bigger number = appears more often. For example, a symbol with weight 10 will land about twice as often as one with weight 5.")]
    public int weight;

    /// <summary>
    /// The factor applied to the player's bet when this symbol produces a win
    /// (e.g. a value of 5 pays out 5x the bet).
    /// </summary>
    [Tooltip("How much the player's bet is multiplied by when they win with this symbol. For example, 5 means a win pays out 5 times what the player bet.")]
    public float payoutMultiplier;
}
