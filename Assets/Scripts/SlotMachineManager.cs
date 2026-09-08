using System;
using UnityEngine;

/// <summary>
/// Orchestrates a 3-reel slot machine: rolls an independent weighted-random result
/// for each reel, kicks off their spin animations with a staggered stop, waits for
/// all three to land, then evaluates the outcome and reports it back to the caller.
///
/// Depends on ReelController (drives a single reel's spin animation and exposes the
/// symbol it lands on) and SymbolData (the weighted symbol pool shared by all reels).
/// </summary>
[DisallowMultipleComponent]
public class SlotMachineManager : MonoBehaviour
{
    // Exactly 3 reels are expected; anything else is a misconfiguration.
    private const int RequiredReelCount = 3;

    // How many reels need to land on Cherries (short of all 3 matching) to trigger the consolation payout.
    private const int MinimumMatchingCherriesForConsolation = 2;

    // The exact symbolName used to identify the Cherries symbol for the consolation-win check.
    private const string CherrySymbolName = "Cherries";

    [Header("Reels")]
    [Tooltip("The 3 reels that make up this slot machine, left to right. Must contain exactly 3 entries.")]
    [SerializeField] private ReelController[] reels;

    [Header("Symbol Pool")]
    [Tooltip("The full weighted pool of symbols any reel can land on. Each reel rolls its result independently from this pool.")]
    [SerializeField] private SymbolData[] allSymbols;

    [Header("Spin Timing")]
    [Tooltip("How long, in seconds, every reel spins at steady speed before any of them start easing to a stop.")]
    [SerializeField] private float baseSpinDuration = 2f;

    [Tooltip("Extra stop delay, in seconds, added per subsequent reel (reel index * this value) so reels stop one after another instead of all at once.")]
    [SerializeField] private float perReelStopStagger = 0.3f;

    [Header("Payout Rules")]
    [Tooltip("Payout multiplier applied to the bet when 2 (but not all 3) reels land on Cherries. Applied instead of a full win.")]
    [SerializeField] private float cherryConsolationMultiplier = 0.5f;

    /// <summary>Fired the moment StartSpin begins a spin, before any reel starts moving. Use to disable spin input.</summary>
    public event Action OnSpinStarted;

    /// <summary>Fired once every reel has stopped and the result has been fully evaluated and reported. Use to re-enable spin input.</summary>
    public event Action OnSpinFullyResolved;

    // True while a spin is in progress; guards against a second spin being started before the first resolves.
    private bool _isSpinning;

    // How many reels have reported OnReelStopped for the spin currently in progress.
    private int _reelsStoppedCount;

    /// <summary>
    /// Starts a full spin: rolls an independent weighted-random symbol for each reel, plays
    /// their spin animations with a staggered stop, then evaluates the outcome once every reel
    /// is at rest and reports it via onSpinComplete(isWin, payoutAmount).
    /// </summary>
    /// <param name="betAmount">The amount wagered; used to scale the payout.</param>
    /// <param name="onSpinComplete">Invoked once with (isWin, payoutAmount) after all reels have landed.</param>
    public void StartSpin(int betAmount, Action<bool, float> onSpinComplete)
    {
        if (reels == null || reels.Length != RequiredReelCount)
        {
            Debug.LogError($"{nameof(SlotMachineManager)}.{nameof(StartSpin)} requires exactly {RequiredReelCount} reels " +
                            $"in {nameof(reels)}, but found {(reels == null ? 0 : reels.Length)}.", this);
            return;
        }

        if (allSymbols == null || allSymbols.Length == 0)
        {
            Debug.LogError($"{nameof(SlotMachineManager)}.{nameof(StartSpin)} needs at least one entry in {nameof(allSymbols)} to roll results.", this);
            return;
        }

        if (_isSpinning)
        {
            Debug.LogWarning($"{nameof(SlotMachineManager)}.{nameof(StartSpin)} was called while a spin was already in progress; ignoring.", this);
            return;
        }

        _isSpinning = true;
        _reelsStoppedCount = 0;
        OnSpinStarted?.Invoke();

        // Roll an independent result for every reel up front, then hand each reel its target symbol
        // so PrepareStrip can build a strip that lands on it.
        for (int i = 0; i < reels.Length; i++)
        {
            SymbolData rolledSymbol = RollWeightedRandomSymbol();
            reels[i].PrepareStrip(rolledSymbol);
        }

        // Subscribe a per-reel handler so we know exactly when every reel has come to rest, then
        // start each reel's spin with an increasing stop delay so they land one after another.
        for (int i = 0; i < reels.Length; i++)
        {
            ReelController reel = reels[i];
            float stopDelay = i * perReelStopStagger;

            Action onThisReelStopped = null;
            onThisReelStopped = () =>
            {
                reel.OnReelStopped -= onThisReelStopped;
                HandleReelStopped(betAmount, onSpinComplete);
            };
            reel.OnReelStopped += onThisReelStopped;

            reel.StartCoroutine(reel.Spin(baseSpinDuration, stopDelay));
        }
    }

    /// <summary>Called each time a reel reports it has come to rest. Once every reel has stopped, evaluates the outcome.</summary>
    private void HandleReelStopped(int betAmount, Action<bool, float> onSpinComplete)
    {
        _reelsStoppedCount++;
        if (_reelsStoppedCount < reels.Length)
        {
            return;
        }

        EvaluateOutcomeAndReport(betAmount, onSpinComplete);
    }

    /// <summary>
    /// Determines whether the landed symbols form a full win or a Cherries consolation win,
    /// computes the payout, invokes onSpinComplete, then fires OnSpinFullyResolved.
    /// </summary>
    private void EvaluateOutcomeAndReport(int betAmount, Action<bool, float> onSpinComplete)
    {
        SymbolData firstSymbol = reels[0].CurrentSymbol;
        bool isFullWin = true;
        int cherryLandingCount = 0;

        for (int i = 0; i < reels.Length; i++)
        {
            SymbolData landedSymbol = reels[i].CurrentSymbol;

            if (landedSymbol.symbolName != firstSymbol.symbolName)
            {
                isFullWin = false;
            }

            if (landedSymbol.symbolName == CherrySymbolName)
            {
                cherryLandingCount++;
            }
        }

        bool isWin;
        float payoutAmount;

        if (isFullWin)
        {
            isWin = true;
            payoutAmount = betAmount * firstSymbol.payoutMultiplier;
        }
        else if (cherryLandingCount >= MinimumMatchingCherriesForConsolation)
        {
            isWin = true;
            payoutAmount = betAmount * cherryConsolationMultiplier;
        }
        else
        {
            isWin = false;
            payoutAmount = 0f;
        }

        _isSpinning = false;
        onSpinComplete?.Invoke(isWin, payoutAmount);
        OnSpinFullyResolved?.Invoke();
    }

    /// <summary>
    /// Picks one symbol from allSymbols at random, weighted by each entry's weight field.
    /// Mirrors ReelController's own weighted-pick logic so reel results are rolled the same way.
    /// </summary>
    private SymbolData RollWeightedRandomSymbol()
    {
        int totalWeight = 0;
        for (int i = 0; i < allSymbols.Length; i++)
        {
            totalWeight += Mathf.Max(0, allSymbols[i].weight);
        }

        if (totalWeight <= 0)
        {
            // No usable weights configured: fall back to a plain uniform pick.
            return allSymbols[UnityEngine.Random.Range(0, allSymbols.Length)];
        }

        // Random.value is inclusive on both ends [0,1], so scale it into a [0, totalWeight) roll.
        float roll = UnityEngine.Random.value * totalWeight;
        float cumulativeWeight = 0f;

        for (int i = 0; i < allSymbols.Length; i++)
        {
            cumulativeWeight += Mathf.Max(0, allSymbols[i].weight);
            if (roll < cumulativeWeight)
            {
                return allSymbols[i];
            }
        }

        // Unreachable in practice, but guarantees a value if a rounding edge case ever slips through.
        return allSymbols[allSymbols.Length - 1];
    }
}
