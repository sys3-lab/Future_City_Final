using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

/// <summary>
/// Quest 3 window editing controller.
///
/// Supported modes:
/// 1. WindowRemoval: delete the selected window when Trigger is released.
/// 2. WindowsLayoutEditing: resize the selected front window while Trigger is held.
/// </summary>
public sealed class QuestWindowEditingController : MonoBehaviour
{
    private enum ActiveMode
    {
        None,
        Removal,
        Resize
    }

    [Header("Required Reference")]
    [SerializeField]
    private QuestModeController modeController;

    [Header("Resize")]
    [Tooltip("Multiplier applied to the controller horizontal movement in Floor local space.")]
    [SerializeField, Min(0.01f)]
    private float resizeSensitivity = 1f;

    [Tooltip("Smallest allowed window width in the original model's local units.")]
    [SerializeField, Min(0.1f)]
    private float minimumWidth = 0.5f;

    [Tooltip("Minimum distance between either side of the resized window and the floor edge.")]
    [SerializeField, Min(0f)]
    private float edgePadding = 0.5f;

    [Tooltip("Minimum gap kept between the resized window and neighbouring front windows.")]
    [SerializeField, Min(0f)]
    private float minimumWindowGap = 0.2f;

    [Tooltip("Enable this if left/right dragging changes width in the opposite direction from expected.")]
    [SerializeField]
    private bool invertResizeDirection;

    [Header("Selection")]
    [SerializeField]
    private bool highlightOnSelect = true;

    [Header("Debug")]
    [SerializeField]
    private bool logEvents = true;

    private readonly Dictionary<Window, XRSimpleInteractable>
        windowInteractables = new Dictionary<Window, XRSimpleInteractable>();

    private ActiveMode activeMode = ActiveMode.None;

    private Window selectedWindow;
    private Floor selectedFloor;
    private NearFarInteractor activeInteractor;

    private float resizeStartPointerLocalZ;
    private float resizeStartWidth;

    private void Start()
    {
        if (modeController == null)
        {
            modeController = FindFirstObjectByType<QuestModeController>();
        }

        if (modeController == null)
        {
            Debug.LogError(
                "QuestWindowEditingController: QuestModeController is missing.",
                this
            );

            enabled = false;
            return;
        }

        RefreshWindowInteractables();
        UpdateModeState(true);
    }

    private void Update()
    {
        UpdateModeState(false);

        if (
            activeMode == ActiveMode.Resize
            && selectedWindow != null
            && selectedFloor != null
            && activeInteractor != null
        )
        {
            UpdateSelectedWindowWidth(activeInteractor.transform.position);
        }
    }

    private void OnDisable()
    {
        ClearSelection();
        SetWindowInteractablesEnabled(false);
        activeMode = ActiveMode.None;
    }

    private void UpdateModeState(bool force)
    {
        ActiveMode desiredMode = GetDesiredMode();

        if (!force && desiredMode == activeMode)
        {
            return;
        }

        ClearSelection();
        activeMode = desiredMode;

        bool shouldEnable = activeMode != ActiveMode.None;

        if (shouldEnable)
        {
            // Register windows that were added during P3A.
            RefreshWindowInteractables();
        }

        SetWindowInteractablesEnabled(shouldEnable);

        if (logEvents)
        {
            Debug.Log(
                $"[QuestWindowEditing] Active mode: {activeMode}",
                this
            );
        }
    }

    private ActiveMode GetDesiredMode()
    {
        switch (modeController.CurrentMode)
        {
            case QuestModeController.InteractionMode.WindowRemoval:
                return ActiveMode.Removal;

            case QuestModeController.InteractionMode.WindowsLayoutEditing:
                return ActiveMode.Resize;

            default:
                return ActiveMode.None;
        }
    }

    private void RefreshWindowInteractables()
    {
        RemoveDestroyedEntries();

        Window[] windows = FindObjectsByType<Window>(
            FindObjectsInactive.Exclude,
            FindObjectsSortMode.None
        );

        int newlyRegistered = 0;

        foreach (Window window in windows)
        {
            if (window == null || window.glass == null)
            {
                continue;
            }

            EnsureWindowWidthInitialized(window);

            Collider glassCollider = window.glass.GetComponent<Collider>();

            if (glassCollider == null)
            {
                Debug.LogWarning(
                    $"QuestWindowEditingController: {window.name} glass has no Collider.",
                    window
                );

                continue;
            }

            if (
                windowInteractables.TryGetValue(
                    window,
                    out XRSimpleInteractable existingInteractable
                )
                && existingInteractable != null
            )
            {
                existingInteractable.colliders.Clear();
                existingInteractable.colliders.Add(glassCollider);
                continue;
            }

            XRSimpleInteractable interactable =
                window.GetComponent<XRSimpleInteractable>();

            if (interactable == null)
            {
                interactable =
                    window.gameObject.AddComponent<XRSimpleInteractable>();
            }

            interactable.enabled = false;
            interactable.colliders.Clear();
            interactable.colliders.Add(glassCollider);

            Window capturedWindow = window;

            interactable.selectEntered.AddListener(
                args => HandleSelectEntered(capturedWindow, args)
            );

            interactable.selectExited.AddListener(
                _ => HandleSelectExited(capturedWindow)
            );

            windowInteractables[window] = interactable;
            newlyRegistered++;
        }

        if (logEvents)
        {
            Debug.Log(
                $"[QuestWindowEditing] Registered {newlyRegistered} new windows; "
                    + $"{windowInteractables.Count} total.",
                this
            );
        }
    }

    private void HandleSelectEntered(
        Window window,
        SelectEnterEventArgs args
    )
    {
        if (
            activeMode == ActiveMode.None
            || window == null
            || selectedWindow != null
        )
        {
            return;
        }

        Floor floor =
            window.floor != null
                ? window.floor
                : window.GetComponentInParent<Floor>();

        if (floor == null)
        {
            Debug.LogError(
                $"QuestWindowEditingController: {window.name} has no Floor.",
                window
            );

            return;
        }

        selectedWindow = window;
        selectedFloor = floor;

        if (window.floor == null)
        {
            window.floor = floor;
        }

        EnsureWindowWidthInitialized(window);
        SetSelectedVisual(window, true);

        if (activeMode == ActiveMode.Resize)
        {
            activeInteractor = args.interactorObject as NearFarInteractor;

            if (activeInteractor == null)
            {
                Debug.LogWarning(
                    "QuestWindowEditingController: selection source is not a NearFarInteractor.",
                    this
                );

                ClearSelection();
                return;
            }

            resizeStartPointerLocalZ =
                selectedFloor.transform
                    .InverseTransformPoint(activeInteractor.transform.position)
                    .z;

            resizeStartWidth = GetWindowWidth(selectedWindow);
        }

        if (logEvents)
        {
            Debug.Log(
                $"[QuestWindowEditing] Selected {window.name} in {activeMode} mode. "
                    + $"Start width: {GetWindowWidth(window):F3}",
                window
            );
        }
    }

    private void HandleSelectExited(Window window)
    {
        if (window == null || selectedWindow != window)
        {
            return;
        }

        ActiveMode modeAtRelease = activeMode;
        Floor floorAtRelease = selectedFloor;
        float finalWidth = GetWindowWidth(window);

        ClearSelection();

        if (modeAtRelease == ActiveMode.Resize)
        {
            if (logEvents)
            {
                Debug.Log(
                    $"[QuestWindowEditing] Resize finished for {window.name}. "
                        + $"Final width: {finalWidth:F3}",
                    window
                );
            }

            return;
        }

        if (
            modeAtRelease != ActiveMode.Removal
            || modeController.CurrentMode
                != QuestModeController.InteractionMode.WindowRemoval
            || floorAtRelease == null
        )
        {
            return;
        }

        if (
            windowInteractables.TryGetValue(
                window,
                out XRSimpleInteractable interactable
            )
            && interactable != null
        )
        {
            interactable.enabled = false;
        }

        windowInteractables.Remove(window);

        if (logEvents)
        {
            Debug.Log(
                $"[QuestWindowEditing] Removing {window.name} "
                    + $"from {floorAtRelease.name}.",
                window
            );
        }

        floorAtRelease.RemoveWindow(window);
    }

    private void UpdateSelectedWindowWidth(Vector3 pointerWorldPosition)
    {
        if (
            selectedWindow == null
            || selectedFloor == null
        )
        {
            return;
        }

        float currentPointerLocalZ =
            selectedFloor.transform
                .InverseTransformPoint(pointerWorldPosition)
                .z;

        float dragDelta =
            (currentPointerLocalZ - resizeStartPointerLocalZ)
            * resizeSensitivity;

        if (invertResizeDirection)
        {
            dragDelta = -dragDelta;
        }

        float currentWidth = GetWindowWidth(selectedWindow);
        float targetWidth = Mathf.Max(
            minimumWidth,
            resizeStartWidth + dragDelta
        );

        // Shrinking is always safe. Only clamp expansion against edges/neighbours.
        if (targetWidth > currentWidth)
        {
            float maximumExpandableWidth =
                CalculateMaximumExpandableWidth(
                    selectedWindow,
                    selectedFloor
                );

            targetWidth = Mathf.Min(
                targetWidth,
                Mathf.Max(currentWidth, maximumExpandableWidth)
            );
        }

        float widthDelta = targetWidth - currentWidth;

        if (Mathf.Abs(widthDelta) < 0.001f)
        {
            return;
        }

        // Reuse the original AVP business method. It updates both frames and glass.
        selectedWindow.ScaleWindow(widthDelta);
    }

    private float CalculateMaximumExpandableWidth(
        Window window,
        Floor floor
    )
    {
        float centerZ = window.transform.localPosition.z;
        float halfFloorLength =
            Mathf.Max(0f, floor.floorLength * 0.5f);

        float maximumWidth = 2f * Mathf.Max(
            0f,
            halfFloorLength - Mathf.Abs(centerZ) - edgePadding
        );

        Window[] otherWindows =
            floor.GetComponentsInChildren<Window>(false);

        foreach (Window otherWindow in otherWindows)
        {
            if (
                otherWindow == null
                || otherWindow == window
            )
            {
                continue;
            }

            // Only compare windows on the same wall plane.
            if (
                Mathf.Abs(
                    otherWindow.transform.localPosition.x
                    - window.transform.localPosition.x
                ) > 1f
            )
            {
                continue;
            }

            EnsureWindowWidthInitialized(otherWindow);

            float centerDistance = Mathf.Abs(
                otherWindow.transform.localPosition.z - centerZ
            );

            float candidateWidth = 2f * (
                centerDistance
                - GetWindowWidth(otherWindow) * 0.5f
                - minimumWindowGap
            );

            maximumWidth = Mathf.Min(
                maximumWidth,
                candidateWidth
            );
        }

        return Mathf.Max(minimumWidth, maximumWidth);
    }

    private static void EnsureWindowWidthInitialized(Window window)
    {
        if (
            window == null
            || window.width > 0f
            || window.glass == null
        )
        {
            return;
        }

        window.width = Mathf.Abs(
            window.glass.transform.localScale.z
        );
    }

    private static float GetWindowWidth(Window window)
    {
        if (window == null)
        {
            return 0f;
        }

        if (window.width > 0f)
        {
            return window.width;
        }

        if (window.glass != null)
        {
            return Mathf.Abs(
                window.glass.transform.localScale.z
            );
        }

        return 0f;
    }

    private void SetWindowInteractablesEnabled(bool enabledState)
    {
        RemoveDestroyedEntries();

        foreach (
            KeyValuePair<Window, XRSimpleInteractable> pair
            in windowInteractables
        )
        {
            if (pair.Key == null || pair.Value == null)
            {
                continue;
            }

            pair.Value.enabled = enabledState;

            if (!enabledState)
            {
                SetSelectedVisual(pair.Key, false);
            }
        }
    }

    private void ClearSelection()
    {
        if (selectedWindow != null)
        {
            SetSelectedVisual(selectedWindow, false);
        }

        selectedWindow = null;
        selectedFloor = null;
        activeInteractor = null;
        resizeStartPointerLocalZ = 0f;
        resizeStartWidth = 0f;
    }

    private void RemoveDestroyedEntries()
    {
        List<Window> destroyedWindows = new List<Window>();

        foreach (
            KeyValuePair<Window, XRSimpleInteractable> pair
            in windowInteractables
        )
        {
            if (pair.Key == null || pair.Value == null)
            {
                destroyedWindows.Add(pair.Key);
            }
        }

        foreach (Window destroyedWindow in destroyedWindows)
        {
            windowInteractables.Remove(destroyedWindow);
        }
    }

    private void SetSelectedVisual(Window window, bool selected)
    {
        if (
            !highlightOnSelect
            || window == null
            || window.glass == null
            || window.normalMat == null
            || window.selectedMat == null
        )
        {
            return;
        }

        window.SetOnSelectedVisual(selected);
    }
}