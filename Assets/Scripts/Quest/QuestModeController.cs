using System;
using TMPro;
using UnityEngine;

/// <summary>
/// Quest 3 模式与菜单面板控制器。
/// 只负责模式状态和 UI 面板显隐，不负责控制器射线或建筑编辑输入。
/// </summary>
public sealed class QuestModeController : MonoBehaviour
{
    [Serializable]
    public enum InteractionMode
    {
        Idle,
        FacadeEditing,
        WindowAddition,
        WindowRemoval,
        WindowsLayoutEditing,
        FloorEditing,
        AdditionalFloorEditing,
        PushAndPull
    }

    [Header("Current Mode")]
    [SerializeField]
    private InteractionMode mode = InteractionMode.Idle;

    [SerializeField]
    private TMP_Text modeText;

    [Header("Mode Panels")]
    [SerializeField]
    private GameObject alignment;

    [SerializeField]
    private GameObject facadeEditing;

    [SerializeField]
    private GameObject floorEditing;

    [SerializeField]
    private GameObject otherEditing;

    [SerializeField]
    private GameObject floorCurtainChange;

    public InteractionMode CurrentMode => mode;

    private static readonly int ModeCount =
        Enum.GetValues(typeof(InteractionMode)).Length;

    private void Awake()
    {
        ApplyMode(mode);
    }

    /// <summary>
    /// 供 Change_Mode 按钮的 UnityEvent 调用。
    /// </summary>
    public void SwitchToNextMode()
    {
        int nextIndex = ((int)mode + 1) % ModeCount;
        mode = (InteractionMode)nextIndex;

        ApplyMode(mode);
    }

    public void SetMode(InteractionMode newMode)
    {
        mode = newMode;
        ApplyMode(mode);
    }

    private void ApplyMode(InteractionMode currentMode)
    {
        SetAllPanels(false);

        switch (currentMode)
        {
            case InteractionMode.Idle:
                SetPanel(alignment, true);
                break;

            case InteractionMode.FacadeEditing:
                SetPanel(facadeEditing, true);
                break;

            case InteractionMode.WindowAddition:
            case InteractionMode.WindowRemoval:
            case InteractionMode.WindowsLayoutEditing:
                SetPanel(otherEditing, true);
                break;

            case InteractionMode.FloorEditing:
                SetPanel(floorCurtainChange, true);
                break;

            case InteractionMode.AdditionalFloorEditing:
            case InteractionMode.PushAndPull:
                SetPanel(floorEditing, true);
                break;

            default:
                Debug.LogWarning($"Unsupported interaction mode: {currentMode}");
                break;
        }

        if (modeText != null)
        {
            modeText.text = currentMode.ToString();
        }
        else
        {
            Debug.LogError(
                "QuestModeController: Mode Text reference is missing.",
                this
            );
        }

        Debug.Log($"[QuestModeController] Switched to mode: {currentMode}");
    }

    private void SetAllPanels(bool active)
    {
        SetPanel(alignment, active);
        SetPanel(facadeEditing, active);
        SetPanel(floorEditing, active);
        SetPanel(otherEditing, active);
        SetPanel(floorCurtainChange, active);
    }

    private static void SetPanel(GameObject panel, bool active)
    {
        if (panel != null)
        {
            panel.SetActive(active);
        }
    }
}