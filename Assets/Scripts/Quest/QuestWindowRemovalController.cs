using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

/// <summary>
/// Quest 3 窗户删除控制器。
///
/// 操作流程：
/// 1. 切换到 WindowRemoval 模式。
/// 2. 用控制器射线选中窗户玻璃。
/// 3. 按住 Trigger 时显示选中效果。
/// 4. 松开 Trigger 后调用 Floor.RemoveWindow 删除窗户。
/// </summary>
public sealed class QuestWindowRemovalController : MonoBehaviour
{
    [Header("Required Reference")]
    [SerializeField]
    private QuestModeController modeController;

    [Header("Selection")]
    [SerializeField]
    private bool highlightOnSelect = true;

    [Header("Debug")]
    [SerializeField]
    private bool logEvents = true;

    private readonly Dictionary<Window, XRSimpleInteractable>
        windowInteractables = new();

    private bool removalModeEnabled;
    private Window selectedWindow;

    private void Start()
    {
        if (modeController == null)
        {
            modeController = FindFirstObjectByType<QuestModeController>();
        }

        if (modeController == null)
        {
            Debug.LogError(
                "QuestWindowRemovalController: "
                + "QuestModeController is missing.",
                this
            );

            enabled = false;
            return;
        }

        RefreshWindowInteractables();
        UpdateModeState(force: true);
    }

    private void Update()
    {
        UpdateModeState(force: false);
    }

    private void OnDisable()
    {
        removalModeEnabled = false;
        ClearSelection();
        SetWindowInteractablesEnabled(false);
    }

    private void UpdateModeState(bool force)
    {
        bool shouldEnable =
            modeController.CurrentMode
            == QuestModeController.InteractionMode.WindowRemoval;

        if (!force && shouldEnable == removalModeEnabled)
        {
            return;
        }

        removalModeEnabled = shouldEnable;

        if (shouldEnable)
        {
            // 重新扫描，确保 P3A 新添加的窗户也能被删除。
            RefreshWindowInteractables();
        }
        else
        {
            ClearSelection();
        }

        SetWindowInteractablesEnabled(shouldEnable);

        if (logEvents)
        {
            Debug.Log(
                $"[QuestWindowRemoval] WindowRemoval mode: "
                + $"{shouldEnable}",
                this
            );
        }
    }

    /// <summary>
    /// 查找当前激活的 Window，并把玻璃 Collider 注册为可选择区域。
    /// </summary>
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

            Collider glassCollider =
                window.glass.GetComponent<Collider>();

            if (glassCollider == null)
            {
                Debug.LogWarning(
                    $"QuestWindowRemovalController: "
                    + $"{window.name} 的 Glass 没有 Collider。",
                    window
                );

                continue;
            }

            if (
                windowInteractables.TryGetValue(
                    window,
                    out XRSimpleInteractable registeredInteractable
                )
                && registeredInteractable != null
            )
            {
                registeredInteractable.colliders.Clear();
                registeredInteractable.colliders.Add(glassCollider);
                continue;
            }

            XRSimpleInteractable interactable =
                window.GetComponent<XRSimpleInteractable>();

            if (interactable == null)
            {
                interactable =
                    window.gameObject.AddComponent<XRSimpleInteractable>();
            }

            // 配置完成前先禁用，避免在错误模式中参与交互。
            interactable.enabled = false;

            interactable.colliders.Clear();
            interactable.colliders.Add(glassCollider);

            Window capturedWindow = window;

            interactable.selectEntered.AddListener(
                _ => HandleSelectEntered(capturedWindow)
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
                $"[QuestWindowRemoval] Registered "
                + $"{newlyRegistered} new windows; "
                + $"{windowInteractables.Count} total.",
                this
            );
        }
    }

    private void HandleSelectEntered(Window window)
    {
        if (
            !removalModeEnabled
            || window == null
            || selectedWindow != null
        )
        {
            return;
        }

        selectedWindow = window;
        SetSelectedVisual(window, true);

        if (logEvents)
        {
            Debug.Log(
                $"[QuestWindowRemoval] Selected {window.name}.",
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

        selectedWindow = null;

        bool shouldRemove =
            removalModeEnabled
            && modeController.CurrentMode
                == QuestModeController.InteractionMode.WindowRemoval;

        if (!shouldRemove)
        {
            SetSelectedVisual(window, false);
            return;
        }

        Floor floor =
            window.floor != null
                ? window.floor
                : window.GetComponentInParent<Floor>();

        if (floor == null)
        {
            SetSelectedVisual(window, false);

            Debug.LogError(
                $"QuestWindowRemovalController: "
                + $"{window.name} 没有对应的 Floor。",
                window
            );

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
                $"[QuestWindowRemoval] Removing "
                + $"{window.name} from {floor.name}.",
                window
            );
        }

        // 原项目业务方法：从布局列表中移除并销毁窗户。
        floor.RemoveWindow(window);
    }

    private void SetWindowInteractablesEnabled(bool enabledState)
    {
        RemoveDestroyedEntries();

        foreach (
            KeyValuePair<Window, XRSimpleInteractable> pair
            in windowInteractables
        )
        {
            Window window = pair.Key;
            XRSimpleInteractable interactable = pair.Value;

            if (window == null || interactable == null)
            {
                continue;
            }

            interactable.enabled = enabledState;

            if (!enabledState)
            {
                SetSelectedVisual(window, false);
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
    }

    private void RemoveDestroyedEntries()
    {
        List<Window> destroyedWindows = new();

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

        foreach (Window window in destroyedWindows)
        {
            windowInteractables.Remove(window);
        }
    }

    private void SetSelectedVisual(Window window, bool selected)
    {
        if (
            !highlightOnSelect
            || window == null
            || window.glass == null
        )
        {
            return;
        }

        MeshRenderer glassRenderer =
            window.glass.GetComponent<MeshRenderer>();

        if (
            glassRenderer == null
            || window.normalMat == null
            || window.selectedMat == null
        )
        {
            return;
        }

        window.SetOnSelectedVisual(selected);
    }
}