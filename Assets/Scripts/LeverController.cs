using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Controls a two-state slot machine lever. The rest and pulled poses are two
/// separate child GameObjects (each with its own baked-in position within its
/// own canvas), toggled active/inactive as a hard cut — not a single Image with
/// a swapped sprite, and not an eased/tweened transform.
/// </summary>
[DisallowMultipleComponent]
public class LeverController : MonoBehaviour
{
    [Header("Lever Art")]
    [Tooltip("Child GameObject shown while the lever is at rest. Active by default.")]
    [SerializeField] private GameObject leverRestObject;

    [Tooltip("Child GameObject shown while the lever is pulled. Inactive by default.")]
    [SerializeField] private GameObject leverPulledObject;

    [Header("Interaction")]
    [Tooltip("CanvasGroup wrapping the whole lever, used to dim it and block/allow raycasts when disabled.")]
    [SerializeField] private CanvasGroup interactionCanvasGroup;

    [Tooltip("How long, in seconds, leverPulledObject stays active before the lever reverts to its rest pose.")]
    [SerializeField] private float pullHoldDuration = 0.3f;

    [Tooltip("The Image that catches the player's click. Expected to have a Button component on the same " +
             "GameObject — using Button here (rather than a raw IPointerClickHandler) means pointer-state " +
             "handling and interactable-driven raycast blocking come for free instead of being reimplemented.")]
    [SerializeField] private Image clickCatcher;

    /// <summary>Fired once the lever has finished its pull animation and reverted back to rest.</summary>
    public event Action OnLeverPulled;

    // Whether the lever currently accepts clicks. False while a pull is animating or while the
    // caller (e.g. UIManager) has explicitly disabled it during a spin.
    private bool isInteractable = true;

    // The Button driving clickCatcher's pointer handling.
    private Button _clickCatcherButton;

    private void Awake()
    {
        // Start in the rest pose.
        leverRestObject.SetActive(true);
        leverPulledObject.SetActive(false);

        _clickCatcherButton = clickCatcher != null ? clickCatcher.GetComponent<Button>() : null;
        if (_clickCatcherButton == null)
        {
            Debug.LogError($"{nameof(LeverController)} expects a {nameof(Button)} component on the " +
                            $"{nameof(clickCatcher)} GameObject, but none was found.", this);
            return;
        }

        _clickCatcherButton.onClick.AddListener(HandleLeverClicked);
    }

    private void OnDestroy()
    {
        if (_clickCatcherButton != null)
        {
            _clickCatcherButton.onClick.RemoveListener(HandleLeverClicked);
        }
    }

    /// <summary>
    /// Enables or disables the lever. When disabled, the CanvasGroup blocks raycasts and dims
    /// so the player gets a clear visual cue that the lever can't be used right now.
    /// </summary>
    /// <param name="value">True to allow interaction, false to lock the lever out.</param>
    public void SetInteractable(bool value)
    {
        isInteractable = value;

        interactionCanvasGroup.interactable = value;
        interactionCanvasGroup.blocksRaycasts = value;
        interactionCanvasGroup.alpha = value ? 1f : 0.5f;
    }

    /// <summary>Invoked by clickCatcherButton's onClick. Starts the pull animation if the lever is currently usable.</summary>
    private void HandleLeverClicked()
    {
        if (!isInteractable)
        {
            return;
        }

        // Lock out immediately so a burst of clicks during the pull animation can't queue up
        // multiple pulls. The caller is responsible for calling SetInteractable(true) again once
        // it's ready for another spin — the full spin takes longer than this animation alone.
        isInteractable = false;

        StartCoroutine(PullLeverRoutine());
    }

    /// <summary>
    /// Hard-cuts to the pulled pose, holds for pullHoldDuration, hard-cuts back to rest, then
    /// fires OnLeverPulled. No easing/tweening — the visual transition is already baked into
    /// the two art assets themselves.
    /// </summary>
    private IEnumerator PullLeverRoutine()
    {
        leverRestObject.SetActive(false);
        leverPulledObject.SetActive(true);

        yield return new WaitForSeconds(pullHoldDuration);

        leverPulledObject.SetActive(false);
        leverRestObject.SetActive(true);

        OnLeverPulled?.Invoke();
    }
}
