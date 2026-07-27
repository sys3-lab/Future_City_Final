using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

/// <summary>
/// Quest 3 正面墙动态窗户添加控制器。
///
/// 操作流程：
/// 1. 切换到 WindowAddition 模式。
/// 2. 使用 Near-Far Interactor 指向楼层 Front 表面。
/// 3. 按住 Trigger 创建并移动预览窗户。
/// 4. 松开 Trigger，调用 Floor.AddWindow 正式添加窗户。
/// </summary>
public sealed class QuestWindowAdditionController : MonoBehaviour
{
    [Header("Required Reference")]
    [SerializeField]
    private QuestModeController modeController;

    [Header("Window Placement")]
    [Tooltip("让窗户稍微位于墙面外侧，避免与墙面闪烁。")]
    [SerializeField, Min(0f)]
    private float frontSurfaceOffset = 0.11f;

    [Tooltip("防止窗户过于靠近墙面左右边缘。")]
    [SerializeField, Min(0f)]
    private float edgePadding = 2f;

    [Header("Debug")]
    [SerializeField]
    private bool logEvents = true;

    private readonly List<XRSimpleInteractable> frontSurfaceInteractables = new();

    private Floor activeFloor;
    private Window previewWindow;
    private NearFarInteractor activeInteractor;

    private bool surfacesEnabled;

    private void Start()
    {
        if (modeController == null)
        {
            modeController = FindFirstObjectByType<QuestModeController>();
        }

        if (modeController == null)
        {
            Debug.LogError(
                "QuestWindowAdditionController: QuestModeController is missing.",
                this
            );
            enabled = false;
            return;
        }

        RegisterFrontSurfaces();
        UpdateSurfaceState(force: true);
    }

    private void Update()
    {
        UpdateSurfaceState(force: false);

        if (
            previewWindow != null
            && activeFloor != null
            && activeInteractor != null
        )
        {
            UpdatePreviewPosition(activeInteractor.attachTransform.position);
        }
    }

    private void OnDisable()
    {
        CancelPlacement();
        SetSurfaceInteractablesEnabled(false);
    }

    /// <summary>
    /// 在场景中的每个 Floor.front 上，运行时添加 XR Simple Interactable。
    /// 不会永久修改 Floor prefab。
    /// </summary>
    private void RegisterFrontSurfaces()
    {
        Floor[] floors = FindObjectsByType<Floor>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None
        );

        foreach (Floor floor in floors)
        {
            if (floor == null || floor.front == null)
            {
                continue;
            }

            Collider frontCollider = floor.front.GetComponent<Collider>();

            if (frontCollider == null)
            {
                Debug.LogWarning(
                    $"QuestWindowAdditionController: "
                    + $"{floor.name} 的 Front 没有 Collider。",
                    floor
                );
                continue;
            }

            XRSimpleInteractable interactable =
                floor.front.GetComponent<XRSimpleInteractable>();

            if (interactable == null)
            {
                interactable =
                    floor.front.gameObject.AddComponent<XRSimpleInteractable>();
            }

            // 先关闭再配置，防止配置过程中参与交互。
            interactable.enabled = false;

            interactable.colliders.Clear();
            interactable.colliders.Add(frontCollider);

            Floor capturedFloor = floor;

            interactable.selectEntered.AddListener(
                args => HandleSurfaceSelectEntered(capturedFloor, args)
            );

            interactable.selectExited.AddListener(
                args => HandleSurfaceSelectExited(capturedFloor, args)
            );

            frontSurfaceInteractables.Add(interactable);
        }

        if (logEvents)
        {
            Debug.Log(
                $"[QuestWindowAddition] Registered "
                + $"{frontSurfaceInteractables.Count} front surfaces.",
                this
            );
        }
    }

    private void UpdateSurfaceState(bool force)
    {
        bool shouldEnable =
            modeController.CurrentMode
            == QuestModeController.InteractionMode.WindowAddition;

        if (!force && shouldEnable == surfacesEnabled)
        {
            return;
        }

        if (!shouldEnable)
        {
            CancelPlacement();
        }

        SetSurfaceInteractablesEnabled(shouldEnable);

        if (logEvents)
        {
            Debug.Log(
                $"[QuestWindowAddition] WindowAddition mode: {shouldEnable}",
                this
            );
        }
    }

    private void SetSurfaceInteractablesEnabled(bool value)
    {
        surfacesEnabled = value;

        foreach (XRSimpleInteractable interactable in frontSurfaceInteractables)
        {
            if (interactable != null)
            {
                interactable.enabled = value;
            }
        }
    }

    private void HandleSurfaceSelectEntered(
        Floor floor,
        SelectEnterEventArgs args
    )
    {
        if (!surfacesEnabled || previewWindow != null)
        {
            return;
        }

        if (args.interactorObject is not NearFarInteractor nearFarInteractor)
        {
            Debug.LogWarning(
                "QuestWindowAdditionController: "
                + "当前选择来源不是 NearFarInteractor。",
                this
            );
            return;
        }

        if (floor.windowPrefab == null)
        {
            Debug.LogError(
                $"QuestWindowAdditionController: "
                + $"{floor.name} 没有配置 Window Prefab。",
                floor
            );
            return;
        }

        activeFloor = floor;
        activeInteractor = nearFarInteractor;

        GameObject previewObject = Instantiate(
            floor.windowPrefab,
            floor.transform
        );

        previewWindow = previewObject.GetComponent<Window>();

        if (previewWindow == null)
        {
            Debug.LogError(
                "QuestWindowAdditionController: "
                + "Window Prefab 上没有 Window 组件。",
                previewObject
            );

            Destroy(previewObject);
            ResetActiveState();
            return;
        }

        previewWindow.TurnOffCollider();

        previewWindow.transform.localRotation = Quaternion.identity;
        previewWindow.transform.localScale = Vector3.one;

        UpdatePreviewPosition(activeInteractor.attachTransform.position);

        if (logEvents)
        {
            Debug.Log(
                $"[QuestWindowAddition] Preview started on {floor.name}.",
                floor
            );
        }
    }

    private void HandleSurfaceSelectExited(
        Floor floor,
        SelectExitEventArgs args
    )
    {
        if (
            previewWindow == null
            || activeFloor == null
            || activeFloor != floor
        )
        {
            return;
        }

        bool shouldCommit =
            surfacesEnabled
            && modeController.CurrentMode
                == QuestModeController.InteractionMode.WindowAddition;

        Vector3 finalLocalPosition =
            previewWindow.transform.localPosition;

        Destroy(previewWindow.gameObject);
        previewWindow = null;

        Floor floorToCommit = activeFloor;
        ResetActiveState();

        if (!shouldCommit)
        {
            return;
        }

        // 0 代表正面 Front。
        floorToCommit.AddWindow(0, finalLocalPosition);

        if (logEvents)
        {
            Debug.Log(
                $"[QuestWindowAddition] Window added to "
                + $"{floorToCommit.name} at {finalLocalPosition}.",
                floorToCommit
            );
        }
    }

    private void UpdatePreviewPosition(Vector3 worldPosition)
    {
        if (activeFloor == null || previewWindow == null)
        {
            return;
        }

        Vector3 localPosition =
            activeFloor.transform.InverseTransformPoint(worldPosition);

        float frontX =
            activeFloor.front.localPosition.x - frontSurfaceOffset;

        float frontY =
            activeFloor.front.localPosition.y;

        float halfLength =
            Mathf.Max(0f, activeFloor.floorLength * 0.5f);

        float minimumZ = -halfLength + edgePadding;
        float maximumZ = halfLength - edgePadding;

        if (minimumZ > maximumZ)
        {
            minimumZ = -halfLength;
            maximumZ = halfLength;
        }

        localPosition.x = frontX;
        localPosition.y = frontY;
        localPosition.z = Mathf.Clamp(
            localPosition.z,
            minimumZ,
            maximumZ
        );

        previewWindow.transform.localPosition = localPosition;
        previewWindow.transform.localRotation = Quaternion.identity;
        previewWindow.transform.localScale = Vector3.one;
    }

    private void CancelPlacement()
    {
        if (previewWindow != null)
        {
            Destroy(previewWindow.gameObject);
        }

        previewWindow = null;
        ResetActiveState();
    }

    private void ResetActiveState()
    {
        activeFloor = null;
        activeInteractor = null;
    }
}