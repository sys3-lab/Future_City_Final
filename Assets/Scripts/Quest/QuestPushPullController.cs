using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

/// <summary>
/// Quest 3 adapter for the original AVP PushAndPull mode.
///
/// While the app is in PushAndPull mode, selecting the configured target and
/// moving the Quest controller moves the target only along its parent's local Z
/// axis. The original AVP local-Z range of [-16, 16] is preserved.
/// </summary>
public sealed class QuestPushPullController : MonoBehaviour
{
    [Header("Required References")]
    [SerializeField]
    private QuestModeController modeController;

    [Tooltip("Original AVP pullObject. In the current scene this is AdditionFloor/Box/Base.")]
    [SerializeField]
    private Transform target;

    [Header("Constrained Movement")]
    [SerializeField]
    private float minimumLocalZ = -16f;

    [SerializeField]
    private float maximumLocalZ = 16f;

    [Tooltip("Multiplier applied to Quest controller movement in the target parent's local Z axis.")]
    [SerializeField, Min(0.01f)]
    private float dragSensitivity = 1f;

    [Tooltip("Enable when the physical push/pull direction feels reversed.")]
    [SerializeField]
    private bool invertDirection;

    [Header("Debug")]
    [SerializeField]
    private bool logEvents = true;

    private XRSimpleInteractable targetInteractable;
    private NearFarInteractor activeInteractor;

    private bool pushPullModeEnabled;
    private bool isDragging;

    private float dragStartPointerParentLocalZ;
    private float dragStartTargetLocalZ;

    private void Start()
    {
        if (modeController == null)
        {
            modeController = FindFirstObjectByType<QuestModeController>();
        }

        if (modeController == null)
        {
            Debug.LogError(
                "QuestPushPullController: QuestModeController is missing.",
                this
            );
            enabled = false;
            return;
        }

        if (target == null)
        {
            Debug.LogError(
                "QuestPushPullController: Target is missing. "
                    + "Assign AdditionFloor/Box/Base.",
                this
            );
            enabled = false;
            return;
        }

        if (target.parent == null)
        {
            Debug.LogError(
                "QuestPushPullController: Target must have a parent transform.",
                target
            );
            enabled = false;
            return;
        }

        if (minimumLocalZ > maximumLocalZ)
        {
            (minimumLocalZ, maximumLocalZ) =
                (maximumLocalZ, minimumLocalZ);
        }

        RegisterTargetInteractable();
        UpdateModeState(force: true);
    }

    private void Update()
    {
        UpdateModeState(force: false);

        if (
            pushPullModeEnabled
            && isDragging
            && activeInteractor != null
        )
        {
            UpdateTargetPosition();
        }
    }

    private void OnDisable()
    {
        CancelDrag();

        if (targetInteractable != null)
        {
            targetInteractable.enabled = false;
        }
    }

    private void RegisterTargetInteractable()
    {
        targetInteractable =
            target.GetComponent<XRSimpleInteractable>();

        if (targetInteractable == null)
        {
            targetInteractable =
                target.gameObject.AddComponent<XRSimpleInteractable>();
        }

        targetInteractable.enabled = false;
        targetInteractable.colliders.Clear();

        Collider[] childColliders =
            target.GetComponentsInChildren<Collider>(includeInactive: true);

        foreach (Collider childCollider in childColliders)
        {
            if (childCollider != null)
            {
                targetInteractable.colliders.Add(childCollider);
            }
        }

        if (targetInteractable.colliders.Count == 0)
        {
            Debug.LogError(
                "QuestPushPullController: Target has no child Collider. "
                    + "PushAndPull cannot be selected.",
                target
            );
            enabled = false;
            return;
        }

        targetInteractable.selectEntered.AddListener(
            HandleSelectEntered
        );
        targetInteractable.selectExited.AddListener(
            HandleSelectExited
        );

        if (logEvents)
        {
            Debug.Log(
                $"[QuestPushPull] Registered target {target.name} with "
                    + $"{targetInteractable.colliders.Count} colliders.",
                target
            );
        }
    }

    private void UpdateModeState(bool force)
    {
        bool shouldEnable =
            modeController.CurrentMode
            == QuestModeController.InteractionMode.PushAndPull;

        if (!force && shouldEnable == pushPullModeEnabled)
        {
            return;
        }

        pushPullModeEnabled = shouldEnable;

        if (!pushPullModeEnabled)
        {
            CancelDrag();
        }

        if (targetInteractable != null)
        {
            targetInteractable.enabled = pushPullModeEnabled;
        }

        if (logEvents)
        {
            Debug.Log(
                $"[QuestPushPull] PushAndPull mode: "
                    + $"{pushPullModeEnabled}",
                this
            );
        }
    }

    private void HandleSelectEntered(SelectEnterEventArgs args)
    {
        if (!pushPullModeEnabled || isDragging)
        {
            return;
        }

        activeInteractor =
            args.interactorObject as NearFarInteractor;

        if (activeInteractor == null)
        {
            Debug.LogWarning(
                "QuestPushPullController: Selection source is not a "
                    + "NearFarInteractor.",
                this
            );
            return;
        }

        dragStartPointerParentLocalZ =
            GetPointerParentLocalZ();
        dragStartTargetLocalZ =
            target.localPosition.z;
        isDragging = true;

        if (logEvents)
        {
            Debug.Log(
                $"[QuestPushPull] Drag started. Target local Z: "
                    + $"{dragStartTargetLocalZ:F3}",
                target
            );
        }
    }

    private void HandleSelectExited(SelectExitEventArgs args)
    {
        if (!isDragging)
        {
            return;
        }

        float finalLocalZ = target.localPosition.z;
        CancelDrag();

        if (logEvents)
        {
            Debug.Log(
                $"[QuestPushPull] Drag finished. Target local Z: "
                    + $"{finalLocalZ:F3}",
                target
            );
        }
    }

    private void UpdateTargetPosition()
    {
        float pointerDelta =
            GetPointerParentLocalZ()
            - dragStartPointerParentLocalZ;

        if (invertDirection)
        {
            pointerDelta = -pointerDelta;
        }

        float targetLocalZ = Mathf.Clamp(
            dragStartTargetLocalZ
                + pointerDelta * dragSensitivity,
            minimumLocalZ,
            maximumLocalZ
        );

        Vector3 localPosition = target.localPosition;
        localPosition.z = targetLocalZ;
        target.localPosition = localPosition;
    }

    private float GetPointerParentLocalZ()
    {
        // Use the interactor transform rather than a dynamic attach point so
        // the measured controller motion does not follow the moving target.
        Vector3 pointerWorldPosition =
            activeInteractor.transform.position;

        return target.parent
            .InverseTransformPoint(pointerWorldPosition)
            .z;
    }

    private void CancelDrag()
    {
        isDragging = false;
        activeInteractor = null;
        dragStartPointerParentLocalZ = 0f;
        dragStartTargetLocalZ = 0f;
    }
}