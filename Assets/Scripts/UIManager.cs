using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Wires the slot machine's UI to CreditsManager, SlotMachineManager, and
/// LeverController. Owns no gameplay state itself — it just reacts to events
/// from the three systems above, updates on-screen text/buttons, drives the
/// spin flow when the lever is pulled, and shows the win / out-of-credits
/// confirmation popups.
/// </summary>
[DisallowMultipleComponent]
public class UIManager : MonoBehaviour
{
    [Header("System References")]
    [Tooltip("Tracks the player's balance and selected bet tier.")]
    [SerializeField] private CreditsManager creditsManager;

    [Tooltip("Rolls and plays out the 3-reel spin.")]
    [SerializeField] private SlotMachineManager slotMachineManager;

    [Tooltip("The physical/animated lever the player pulls to spin.")]
    [SerializeField] private LeverController leverController;

    [Header("HUD Text")]
    [Tooltip("Displays the player's current balance.")]
    [SerializeField] private TextMeshProUGUI balanceText;

    [Tooltip("Displays the currently selected bet amount.")]
    [SerializeField] private TextMeshProUGUI betAmountText;

    [Tooltip("Inline win text. Hidden by default; the win popup is the primary win feedback.")]
    [SerializeField] private TextMeshProUGUI winText;

    [Header("Bet Buttons")]
    [Tooltip("Raises the bet by one tier.")]
    [SerializeField] private Button betPlusButton;

    [Tooltip("Lowers the bet by one tier.")]
    [SerializeField] private Button betMinusButton;

    [Header("Popup")]
    [Tooltip("Root GameObject of the shared popup. Activated/deactivated to show/hide it.")]
    [SerializeField] private GameObject popupRoot;

    [SerializeField] private TextMeshProUGUI popupTitleText;
    [SerializeField] private TextMeshProUGUI popupMessageText;

    [Tooltip("Shown only for the out-of-credits confirmation popup.")]
    [SerializeField] private Button popupYesButton;

    [Tooltip("Shown only for the out-of-credits confirmation popup.")]
    [SerializeField] private Button popupNoButton;

    [Tooltip("Shown only for the win popup.")]
    [SerializeField] private Button popupCloseButton;

    // The most recent bet tier index reported by OnBetChanged. Used to decide whether
    // betMinusButton should be interactable (index 0 is always the lowest tier), since
    // CreditsManager doesn't expose the total tier count for the equivalent "at max" check.
    private int _lastKnownBetIndex;

    // True while the out-of-credits popup is up, so repeated OnBalanceChanged calls while
    // the balance stays below the current bet don't try to show it again on top of itself.
    private bool _isShowingOutOfCreditsPopup;

    private void Start()
    {
        // System event subscriptions.
        creditsManager.OnBalanceChanged += HandleBalanceChanged;
        creditsManager.OnBetChanged += HandleBetChanged;
        leverController.OnLeverPulled += HandleLeverPulled;
        slotMachineManager.OnSpinStarted += HandleSpinStarted;
        slotMachineManager.OnSpinFullyResolved += HandleSpinFullyResolved;

        // Button click wiring.
        betPlusButton.onClick.AddListener(HandleBetPlusClicked);
        betMinusButton.onClick.AddListener(HandleBetMinusClicked);
        popupYesButton.onClick.AddListener(HandleConfirmResetYesClicked);
        popupNoButton.onClick.AddListener(HandleConfirmResetNoClicked);
        popupCloseButton.onClick.AddListener(HandlePopupCloseClicked);

        // Initialize the HUD from current values directly, rather than relying on the
        // Start()-time events CreditsManager fires itself — script execution order between
        // different components' Start() calls isn't guaranteed, so this is the safe path.
        HandleBalanceChanged(creditsManager.CurrentBalance);
        HandleBetChanged(_lastKnownBetIndex, creditsManager.CurrentBet);

        winText.gameObject.SetActive(false);
        popupRoot.SetActive(false);
    }

    private void OnDestroy()
    {
        creditsManager.OnBalanceChanged -= HandleBalanceChanged;
        creditsManager.OnBetChanged -= HandleBetChanged;
        leverController.OnLeverPulled -= HandleLeverPulled;
        slotMachineManager.OnSpinStarted -= HandleSpinStarted;
        slotMachineManager.OnSpinFullyResolved -= HandleSpinFullyResolved;

        betPlusButton.onClick.RemoveListener(HandleBetPlusClicked);
        betMinusButton.onClick.RemoveListener(HandleBetMinusClicked);
        popupYesButton.onClick.RemoveListener(HandleConfirmResetYesClicked);
        popupNoButton.onClick.RemoveListener(HandleConfirmResetNoClicked);
        popupCloseButton.onClick.RemoveListener(HandlePopupCloseClicked);
    }

    // ---------------------------------------------------------------------
    // CreditsManager event handlers
    // ---------------------------------------------------------------------

    /// <summary>Updates the balance display, and prompts to reset if the player can no longer afford the current bet.</summary>
    private void HandleBalanceChanged(int newBalance)
    {
        balanceText.text = $"BALANCE: {newBalance}";

        if (newBalance < creditsManager.CurrentBet && !_isShowingOutOfCreditsPopup)
        {
            leverController.SetInteractable(false);
            ShowConfirmResetPopup();
        }
    }

    /// <summary>Updates the bet display and the bet buttons' availability.</summary>
    private void HandleBetChanged(int newBetIndex, int newBetAmount)
    {
        _lastKnownBetIndex = newBetIndex;
        betAmountText.text = $"BET:{newBetAmount}";

        // Index 0 is always the lowest tier, so this bound is known for certain. The upper
        // bound isn't known here (CreditsManager doesn't expose the tier count), so plusButton
        // is optimistically re-enabled and self-corrects in HandleBetPlusClicked if it turns
        // out we're already at the top tier.
        betMinusButton.interactable = newBetIndex > 0;
        betPlusButton.interactable = true;
    }

    // ---------------------------------------------------------------------
    // Bet button handlers
    // ---------------------------------------------------------------------

    private void HandleBetPlusClicked()
    {
        bool changed = creditsManager.IncreaseBet();
        if (!changed)
        {
            // Already at the top tier; disable until the bet moves back down.
            betPlusButton.interactable = false;
        }
    }

    private void HandleBetMinusClicked()
    {
        bool changed = creditsManager.DecreaseBet();
        if (!changed)
        {
            betMinusButton.interactable = false;
        }
    }

    // ---------------------------------------------------------------------
    // Lever / spin flow
    // ---------------------------------------------------------------------

    /// <summary>Validates affordability, deducts the bet, and starts a spin.</summary>
    private void HandleLeverPulled()
    {
        if (!creditsManager.CanAffordCurrentBet)
        {
            ShowConfirmResetPopup();
            return;
        }

        // Capture the bet amount before deducting, since TryDeductBet only changes the
        // balance, not the selected tier — but this keeps the value's meaning explicit at
        // the point it's passed on to the spin.
        int betAmount = creditsManager.CurrentBet;

        if (!creditsManager.TryDeductBet())
        {
            // Shouldn't happen given the affordability check above, but guard against a
            // race between the check and the deduction.
            return;
        }

        slotMachineManager.StartSpin(betAmount, HandleSpinComplete);
    }

    /// <summary>Callback passed to StartSpin; awards the payout and shows the win popup on a win.</summary>
    private void HandleSpinComplete(bool isWin, float payoutAmount)
    {
        if (!isWin)
        {
            winText.gameObject.SetActive(false);
            return;
        }

        creditsManager.AwardPayout(payoutAmount);
        winText.text = $"WIN: {Mathf.RoundToInt(payoutAmount)}";
        winText.gameObject.SetActive(true);
        ShowWinPopup(payoutAmount);
    }

    /// <summary>Locks out the lever and bet buttons while the reels are spinning.</summary>
    private void HandleSpinStarted()
    {
        leverController.SetInteractable(false);
        betPlusButton.interactable = false;
        betMinusButton.interactable = false;
        winText.gameObject.SetActive(false);
    }

    /// <summary>Re-enables the lever and bet buttons once the spin has fully resolved.</summary>
    private void HandleSpinFullyResolved()
    {
        leverController.SetInteractable(creditsManager.CanAffordCurrentBet);
        betPlusButton.interactable = true;
        betMinusButton.interactable = _lastKnownBetIndex > 0;
    }

    // ---------------------------------------------------------------------
    // Popup
    // ---------------------------------------------------------------------

    /// <summary>Configures and shows the popup in its "you won" state, with only the close button visible.</summary>
    private void ShowWinPopup(float payoutAmount)
    {
        popupTitleText.text = "You Win!";
        popupMessageText.text = $"You won {Mathf.RoundToInt(payoutAmount)} credits!";

        popupYesButton.gameObject.SetActive(false);
        popupNoButton.gameObject.SetActive(false);
        popupCloseButton.gameObject.SetActive(true);

        popupRoot.SetActive(true);
    }

    /// <summary>Configures and shows the popup in its "out of credits" confirmation state, with only Yes/No visible.</summary>
    private void ShowConfirmResetPopup()
    {
        popupTitleText.text = "Out of Credits";
        popupMessageText.text = "Out of credits — reset and play again?";

        popupYesButton.gameObject.SetActive(true);
        popupNoButton.gameObject.SetActive(true);
        popupCloseButton.gameObject.SetActive(false);

        popupRoot.SetActive(true);
        _isShowingOutOfCreditsPopup = true;
    }

    private void HandleConfirmResetYesClicked()
    {
        creditsManager.ResetBalance();
        HidePopup();
    }

    private void HandleConfirmResetNoClicked()
    {
        // Player declined to reset; leave the lever disabled since they still can't afford a spin.
        HidePopup();
    }

    private void HandlePopupCloseClicked()
    {
        HidePopup();
    }

    private void HidePopup()
    {
        popupRoot.SetActive(false);
        _isShowingOutOfCreditsPopup = false;
    }
}
