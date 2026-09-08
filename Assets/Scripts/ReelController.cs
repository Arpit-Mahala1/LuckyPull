using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Controls a single reel of a 3-reel slot machine.
///
/// Coordinate convention used throughout this script:
/// - Strip children are laid out top-to-bottom inside symbolStripContainer, each
///   symbolCellHeight apart, with child index 0 at local Y = 0 and index i at
///   local Y = -i * symbolCellHeight.
/// - "Resting" means symbolStripContainer.anchoredPosition.y is set so the target
///   symbol's position in the container's parent space equals Y = 0 (the window's
///   reference line). Place a visual marker / mask center at that same Y = 0 point
///   on the parent to define where the reel "reads" its result.
/// - A spin moves anchoredPosition.y upward (increasing), which visually scrolls
///   symbols upward through the window. Everything below is consistent with this
///   choice; flip the sign of steadyScrollSpeed if you want the opposite feel.
/// </summary>
[DisallowMultipleComponent]
public class ReelController : MonoBehaviour
{
    [Header("Strip Layout")]
    [Tooltip("RectTransform that holds the scrolling symbol Images as children. Its anchoredPosition is animated during a spin.")]
    [SerializeField] private RectTransform symbolStripContainer;

    [Tooltip("Template Image instantiated once per symbol on the strip. This object itself is never part of the strip.")]
    [SerializeField] private Image symbolImagePrefab;

    [Tooltip("Pool of symbols this reel can display, each with its own spawn weight and payout multiplier.")]
    [SerializeField] private SymbolData[] availableSymbols;

    [Tooltip("Vertical distance, in UI units, between the centers of two adjacent symbols on the strip.")]
    [SerializeField] private float symbolCellHeight = 150f;

    [Tooltip("How many full copies of availableSymbols (in randomized order) are used to build the scroll-past filler above the target symbol. Raise this (or lower steadyScrollSpeed/spin duration) if the reel runs out of strip mid-spin.")]
    [SerializeField] private int stripLengthMultiplier = 4;

    [Tooltip("Number of symbols visible in the reel window at once. Should be odd so the target symbol sits exactly in the middle. Used to build filler below the target so the window isn't empty the instant the reel lands.")]
    [SerializeField] private int visibleSymbolCount = 3;

    [Header("Spin Motion")]
    [Tooltip("Constant scroll speed, in UI units per second, while the reel is in its steady spin phase.")]
    [SerializeField] private float steadyScrollSpeed = 1800f;

    [Tooltip("Duration, in seconds, of the deceleration from steady scroll speed down to the exact resting position.")]
    [SerializeField] private float easeDuration = 0.5f;

    [Tooltip("Normalized (0-1 time in, 0-1 progress out) easing curve applied while decelerating into the resting position.")]
    [SerializeField] private AnimationCurve spinEaseCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    /// <summary>Fired once the Spin coroutine has finished and the reel is at rest.</summary>
    public event Action OnReelStopped;

    /// <summary>The symbol PrepareStrip most recently placed at the reel's resting/center window position.</summary>
    public SymbolData CurrentSymbol { get; private set; }

    // Index within the instantiated strip (top to bottom) of the symbol PrepareStrip placed at the resting position.
    private int _targetChildIndex;

    // The exact symbolStripContainer.anchoredPosition.y that lines the target symbol up on the reference line.
    private float _restingAnchoredY;

    // Instantiated strip children, top to bottom, kept so Spin() can validate state and future calls can clean up.
    private readonly List<Image> _stripSymbols = new List<Image>();

    /// <summary>
    /// Builds (or rebuilds) the strip of Images under symbolStripContainer so that once
    /// Spin() eases to a stop, targetSymbol sits exactly in the reel's visible/center window.
    /// Any previously-instantiated strip children are destroyed first. Filler symbols are
    /// picked randomly (weighted by SymbolData.weight) from availableSymbols.
    /// </summary>
    public void PrepareStrip(SymbolData targetSymbol)
    {
        if (targetSymbol == null)
        {
            Debug.LogError($"{nameof(ReelController)}.{nameof(PrepareStrip)} was called with a null target symbol.", this);
            return;
        }

        if (availableSymbols == null || availableSymbols.Length == 0)
        {
            Debug.LogError($"{nameof(ReelController)}.{nameof(PrepareStrip)} needs at least one entry in availableSymbols to build filler.", this);
            return;
        }

        ClearExistingStrip();

        // How many random filler symbols to scroll past above the target, so the spin has enough strip to run through.
        int topFillerCount = Mathf.Max(0, stripLengthMultiplier) * availableSymbols.Length;

        // How many filler symbols sit below the target so the window isn't empty the instant the reel lands.
        int bottomFillerCount = Mathf.Max(0, (visibleSymbolCount - 1) / 2);

        List<SymbolData> stripOrder = new List<SymbolData>(topFillerCount + 1 + bottomFillerCount);
        for (int i = 0; i < topFillerCount; i++)
        {
            stripOrder.Add(GetRandomWeightedSymbol());
        }

        _targetChildIndex = stripOrder.Count; // The target lands right after the top filler.
        stripOrder.Add(targetSymbol);

        for (int i = 0; i < bottomFillerCount; i++)
        {
            stripOrder.Add(GetRandomWeightedSymbol());
        }

        // Instantiate and lay out the strip, top to bottom, each symbolCellHeight apart.
        for (int i = 0; i < stripOrder.Count; i++)
        {
            Image symbolImage = Instantiate(symbolImagePrefab, symbolStripContainer);
            symbolImage.gameObject.SetActive(true);
            symbolImage.sprite = stripOrder[i].symbolSprite;
            symbolImage.name = $"Symbol_{i:D3}_{stripOrder[i].symbolName}";

            RectTransform symbolRect = symbolImage.rectTransform;
            symbolRect.anchoredPosition = new Vector2(symbolRect.anchoredPosition.x, -i * symbolCellHeight);
            symbolRect.sizeDelta = new Vector2(symbolRect.sizeDelta.x, symbolCellHeight);

            _stripSymbols.Add(symbolImage);
        }

        // The container position that puts the target symbol exactly on the reference line (parent-space Y = 0).
        _restingAnchoredY = _targetChildIndex * symbolCellHeight;

        // Start every freshly-prepared strip from a known position: its topmost filler symbol on the reference line.
        symbolStripContainer.anchoredPosition = new Vector2(symbolStripContainer.anchoredPosition.x, 0f);

        CurrentSymbol = targetSymbol;
    }

    /// <summary>
    /// Scrolls the strip at a steady speed for spinDuration, keeps scrolling for an extra
    /// stopDelay seconds (so callers can stagger when each reel begins decelerating), then
    /// eases smoothly onto the symbol PrepareStrip() was last called with. Fires
    /// OnReelStopped once the reel is exactly at rest.
    /// </summary>
    public IEnumerator Spin(float spinDuration, float stopDelay)
    {
        if (_stripSymbols.Count == 0)
        {
            Debug.LogError($"{nameof(ReelController)}.{nameof(Spin)} was called before {nameof(PrepareStrip)}; there is nothing to scroll.", this);
            yield break;
        }

        // --- Phase 1: steady-speed scroll, held for spinDuration + stopDelay. Reels are staggered
        // purely by giving each one a different stopDelay while spinDuration stays shared. ---
        float steadyPhaseDuration = spinDuration + stopDelay;
        float steadyPhaseElapsed = 0f;
        while (steadyPhaseElapsed < steadyPhaseDuration)
        {
            float deltaY = steadyScrollSpeed * Time.deltaTime;
            symbolStripContainer.anchoredPosition += new Vector2(0f, deltaY);

            steadyPhaseElapsed += Time.deltaTime;
            yield return null;
        }

        float steadyPhaseDistance = steadyScrollSpeed * steadyPhaseDuration;
        if (steadyPhaseDistance >= _restingAnchoredY)
        {
            // The steady phase already scrolled at or past the target's resting position, so the
            // ease-out below has to move backward to land correctly. The reel will still stop on
            // the right symbol, but the motion may visibly reverse. Fix by raising
            // stripLengthMultiplier / symbolCellHeight or lowering steadyScrollSpeed / spinDuration.
            Debug.LogWarning($"{nameof(ReelController)}: strip may be too short for the configured spin speed/duration; " +
                              "the reel will still land correctly but may visibly reverse while easing to a stop.", this);
        }

        // --- Phase 2: ease from wherever the steady scroll left off to the exact resting position. ---
        float easeStartY = symbolStripContainer.anchoredPosition.y;
        float easeElapsed = 0f;
        while (easeElapsed < easeDuration)
        {
            easeElapsed += Time.deltaTime;
            float normalizedTime = Mathf.Clamp01(easeElapsed / easeDuration);
            float curveValue = spinEaseCurve.Evaluate(normalizedTime);
            float newY = Mathf.LerpUnclamped(easeStartY, _restingAnchoredY, curveValue);
            symbolStripContainer.anchoredPosition = new Vector2(symbolStripContainer.anchoredPosition.x, newY);
            yield return null;
        }

        // Snap exactly onto the resting position so there is no leftover floating-point misalignment.
        symbolStripContainer.anchoredPosition = new Vector2(symbolStripContainer.anchoredPosition.x, _restingAnchoredY);

        OnReelStopped?.Invoke();
    }

    /// <summary>Destroys every currently-instantiated strip child and clears the tracking list.</summary>
    private void ClearExistingStrip()
    {
        for (int i = symbolStripContainer.childCount - 1; i >= 0; i--)
        {
            Destroy(symbolStripContainer.GetChild(i).gameObject);
        }
        _stripSymbols.Clear();
    }

    /// <summary>Picks a random symbol from availableSymbols, weighted by each entry's weight field.</summary>
    private SymbolData GetRandomWeightedSymbol()
    {
        int totalWeight = 0;
        for (int i = 0; i < availableSymbols.Length; i++)
        {
            totalWeight += Mathf.Max(0, availableSymbols[i].weight);
        }

        if (totalWeight <= 0)
        {
            // No usable weights configured: fall back to a plain uniform pick.
            return availableSymbols[UnityEngine.Random.Range(0, availableSymbols.Length)];
        }

        int roll = UnityEngine.Random.Range(0, totalWeight);
        int cumulativeWeight = 0;
        for (int i = 0; i < availableSymbols.Length; i++)
        {
            cumulativeWeight += Mathf.Max(0, availableSymbols[i].weight);
            if (roll < cumulativeWeight)
            {
                return availableSymbols[i];
            }
        }

        // Unreachable in practice, but guarantees a value if a rounding edge case ever slips through.
        return availableSymbols[availableSymbols.Length - 1];
    }
}
