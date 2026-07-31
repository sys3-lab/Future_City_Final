using UnityEngine;
using UnityEngine.XR.ARFoundation;

#if UNITY_ANDROID
using UnityEngine.Android;
#endif

/// <summary>
/// Requests Meta Quest Scene permission and enables scene-dependent
/// AR Foundation managers after permission is granted.
/// </summary>
public sealed class QuestScenePermissionController : MonoBehaviour
{
    private const string ScenePermission =
        "com.oculus.permission.USE_SCENE";

    [Header("Scene Managers")]
    [SerializeField]
    private ARPlaneManager planeManager;

    [SerializeField]
    private ARRaycastManager raycastManager;

    [Header("Debug")]
    [SerializeField]
    private bool logEvents = true;

    private void Awake()
    {
        SetSceneManagersEnabled(false);
    }

    private void Start()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (
            Permission.HasUserAuthorizedPermission(
                ScenePermission
            )
        )
        {
            HandlePermissionGranted(ScenePermission);
            return;
        }

        if (logEvents)
        {
            Debug.Log(
                $"[QuestScenePermission] Requesting: "
                + $"{ScenePermission}",
                this
            );
        }

        PermissionCallbacks callbacks =
            new PermissionCallbacks();

        callbacks.PermissionGranted +=
            HandlePermissionGranted;

        callbacks.PermissionDenied +=
            HandlePermissionDenied;

        Permission.RequestUserPermission(
            ScenePermission,
            callbacks
        );
#else
        // Editor validation only. Real permission is requested on Quest.
        if (logEvents)
        {
            Debug.Log(
                "[QuestScenePermission] Editor mode: "
                + "enabling scene managers for configuration checks.",
                this
            );
        }

        SetSceneManagersEnabled(true);
#endif
    }

    private void HandlePermissionGranted(string permission)
    {
        if (logEvents)
        {
            Debug.Log(
                $"[QuestScenePermission] Granted: {permission}",
                this
            );
        }

        SetSceneManagersEnabled(true);
    }

    private void HandlePermissionDenied(string permission)
    {
        SetSceneManagersEnabled(false);

        Debug.LogWarning(
            $"[QuestScenePermission] Denied: {permission}. "
            + "Plane detection and environment raycasts "
            + "will remain unavailable.",
            this
        );
    }

    private void SetSceneManagersEnabled(bool value)
    {
        if (planeManager != null)
        {
            planeManager.enabled = value;
        }

        if (raycastManager != null)
        {
            raycastManager.enabled = value;
        }
    }
}