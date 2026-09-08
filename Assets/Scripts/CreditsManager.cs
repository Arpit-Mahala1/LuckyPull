using System;
using UnityEngine;

/// <summary>
/// Standalone credits/balance system for the slot machine: tracks the player's
/// current balance and selected bet tier, and exposes methods to change either.
/// Has no dependencies on any other custom script — other systems (UI, the spin
/// orchestrator, etc.) are expected to subscribe to its events and react to them.
/// </summary>
[DisallowMultipleComponent]
public class CreditsManager : MonoBehaviour
{
    [Header("Balance")]
    [Tooltip("The balance the player starts with, and the value ResetBalance() restores.")]
    [SerializeField] private int startingBalance = 1000;

    [Header("Bet Tiers")]
    [Tooltip("The selectable bet amounts, in ascending order. The current bet is betTiers[betIndex].")]
    [SerializeField] private int[] betTiers = { 10, 50, 100 };

    // The player's current balance.
    private int balance;

    // Index into betTiers for the currently selected bet amount.
    private int betIndex;

    /// <summary>The player's current balance.</summary>
    public int CurrentBalance => balance;

    /// <summary>The currently selected bet amount (betTiers[betIndex]).</summary>
    public int CurrentBet => betTiers[betIndex];

    /// <summary>Whether the current balance is enough to cover the current bet.</summary>
    public bool CanAffordCurrentBet => balance >= CurrentBet;

    /// <summary>Fired whenever the balance changes, passing the new balance.</summary>
    public event Action<int> OnBalanceChanged;

    /// <summary>Fired whenever the selected bet tier changes, passing the new bet tier index and the new bet amount.</summary>
    public event Action<int, int> OnBetChanged;

    private void Awake()
    {
        balance = startingBalance;
        betIndex = 0;
    }

    private void Start()
    {
        // Fire both events once on scene load so any UI that subscribed in its own Awake/Start
        // has a chance to initialize with the correct starting values, rather than showing blanks
        // until the next balance or bet change.
        OnBalanceChanged?.Invoke(balance);
        OnBetChanged?.Invoke(betIndex, CurrentBet);
    }

    /// <summary>
    /// Moves the selected bet up one tier, if not already at the highest tier.
    /// </summary>
    /// <returns>True if the bet tier changed, false if already at the last tier.</returns>
    public bool IncreaseBet()
    {
        if (betTiers == null || betIndex >= betTiers.Length - 1)
        {
            return false;
        }

        betIndex++;
        OnBetChanged?.Invoke(betIndex, CurrentBet);
        return true;
    }

    /// <summary>
    /// Moves the selected bet down one tier, if not already at the lowest tier.
    /// </summary>
    /// <returns>True if the bet tier changed, false if already at tier 0.</returns>
    public bool DecreaseBet()
    {
        if (betIndex <= 0)
        {
            return false;
        }

        betIndex--;
        OnBetChanged?.Invoke(betIndex, CurrentBet);
        return true;
    }

    /// <summary>
    /// Attempts to deduct the current bet from the balance. Does nothing if the
    /// player can't afford it.
    /// </summary>
    /// <returns>True if the bet was deducted, false if the balance was insufficient.</returns>
    public bool TryDeductBet()
    {
        if (!CanAffordCurrentBet)
        {
            return false;
        }

        balance -= CurrentBet;
        OnBalanceChanged?.Invoke(balance);
        return true;
    }

    /// <summary>
    /// Adds a payout to the balance, rounding to the nearest whole credit.
    /// </summary>
    /// <param name="payoutAmount">The payout to award, as computed by the spin result (e.g. bet * multiplier).</param>
    public void AwardPayout(float payoutAmount)
    {
        balance += Mathf.RoundToInt(payoutAmount);
        OnBalanceChanged?.Invoke(balance);
    }

    /// <summary>Resets the balance back to startingBalance.</summary>
    public void ResetBalance()
    {
        balance = startingBalance;
        OnBalanceChanged?.Invoke(balance);
    }
}
