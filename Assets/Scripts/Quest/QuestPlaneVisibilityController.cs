using System.Collections;
using UnityEngine;
using UnityEngine.XR.ARFoundation;

/// <summary>
/// P5 diagnostic version of the Quest plane visibility controller.
/// Keeps Hide Plane behavior and logs manager/subsystem/trackable/render state.
/// Replace the contents of Assets/Scripts/Quest/QuestPlaneVisibilityController.cs
/// with this file; do not create a second class with the same name.
/// </summary>
public sealed class QuestPlaneVisibilityController : MonoBehaviour
{
    [Header("Required Reference")]
    [SerializeField]
    private ARPlaneManager planeManager;

    [Header("Initial State")]
    [SerializeField]
    private bool visibleOnStart = true;

    [Header("Debug")]
    [SerializeField]
    private bool logEvents = true;

    public bool PlanesVisible { get; private set; }

    private void Awake()
    {
        if (planeManager == null)
        {
            planeManager = FindFirstObjectByType<ARPlaneManager>();
        }

        if (planeManager == null)
        {
            Debug.LogError(
                "QuestPlaneVisibilityController: ARPlaneManager is missing.",
                this
            );

            enabled = false;
        }
    }

    private void OnEnable()
    {
        if (planeManager != null)
        {
            planeManager.trackablesChanged.AddListener(HandlePlanesChanged);
        }
    }

    private IEnumerator Start()
    {
        SetPlanesVisible(visibleOnStart);

        // Give OpenXR/AR Foundation time to create and start the subsystem,
        // then time to query the saved Meta Space Setup Scene Model.
        yield return null;
        DumpPlaneState("first frame");

        yield return new WaitForSeconds(1f);
        DumpPlaneState("1 second after startup");

        yield return new WaitForSeconds(4f);
        DumpPlaneState("5 seconds after startup");
    }

    private void OnDisable()
    {
        if (planeManager != null)
        {
            planeManager.trackablesChanged.RemoveListener(HandlePlanesChanged);
        }
    }

    public void TogglePlanes()
    {
        SetPlanesVisible(!PlanesVisible);
        DumpPlaneState("after TogglePlanes");
    }

    public void ShowPlanes()
    {
        SetPlanesVisible(true);
        DumpPlaneState("after ShowPlanes");
    }

    public void HidePlanes()
    {
        SetPlanesVisible(false);
        DumpPlaneState("after HidePlanes");
    }

    public void DumpPlaneState(string reason = "manual")
    {
        if (!logEvents || planeManager == null)
        {
            return;
        }

        bool subsystemExists = planeManager.subsystem != null;
        bool subsystemRunning = subsystemExists && planeManager.subsystem.running;

        Debug.Log(
            $"[QuestPlaneDiagnostics] {reason}; "
            + $"ARSession.state: {ARSession.state}; "
            + $"manager enabled: {planeManager.enabled}; "
            + $"manager active: {planeManager.gameObject.activeInHierarchy}; "
            + $"subsystem exists: {subsystemExists}; "
            + $"subsystem running: {subsystemRunning}; "
            + $"requested mode: {planeManager.requestedDetectionMode}; "
            + $"current mode: {planeManager.currentDetectionMode}; "
            + $"tracked planes: {CountTrackedPlanes()}; "
            + $"visuals visible: {PlanesVisible}",
            this
        );

        foreach (ARPlane plane in planeManager.trackables)
        {
            if (plane == null)
            {
                continue;
            }

            Renderer[] renderers = plane.GetComponentsInChildren<Renderer>(true);
            int enabledRenderers = 0;
            foreach (Renderer planeRenderer in renderers)
            {
                if (planeRenderer != null && planeRenderer.enabled)
                {
                    enabledRenderers++;
                }
            }

            MeshFilter meshFilter = plane.GetComponent<MeshFilter>();
            int vertexCount =
                meshFilter != null && meshFilter.sharedMesh != null
                    ? meshFilter.sharedMesh.vertexCount
                    : 0;

            Debug.Log(
                $"[QuestPlaneDiagnostics] plane {plane.trackableId}; "
                + $"tracking: {plane.trackingState}; "
                + $"alignment: {plane.alignment}; "
                + $"size: {plane.size}; "
                + $"renderers enabled: {enabledRenderers}/{renderers.Length}; "
                + $"mesh vertices: {vertexCount}; "
                + $"active: {plane.gameObject.activeInHierarchy}",
                plane
            );
        }
    }

    private void SetPlanesVisible(bool visible)
    {
        PlanesVisible = visible;

        if (planeManager == null)
        {
            return;
        }

        foreach (ARPlane plane in planeManager.trackables)
        {
            SetPlaneVisualState(plane, visible);
        }

        if (logEvents)
        {
            Debug.Log(
                $"[QuestPlaneVisibility] Planes visible: {visible}; "
                + $"tracked planes: {CountTrackedPlanes()}",
                this
            );
        }
    }

    private void HandlePlanesChanged(
        ARTrackablesChangedEventArgs<ARPlane> changes
    )
    {
        int addedCount = 0;
        int updatedCount = 0;
        int removedCount = 0;

        foreach (ARPlane plane in changes.added)
        {
            addedCount++;
            SetPlaneVisualState(plane, PlanesVisible);
        }

        foreach (ARPlane plane in changes.updated)
        {
            updatedCount++;
            SetPlaneVisualState(plane, PlanesVisible);
        }

        foreach (var _ in changes.removed)
        {
            removedCount++;
        }

        if (logEvents)
        {
            Debug.Log(
                $"[QuestPlaneDiagnostics] trackables changed; "
                + $"added: {addedCount}; "
                + $"updated: {updatedCount}; "
                + $"removed: {removedCount}; "
                + $"tracked planes now: {CountTrackedPlanes()}",
                this
            );
        }
    }

    private int CountTrackedPlanes()
    {
        if (planeManager == null)
        {
            return 0;
        }

        int count = 0;
        foreach (ARPlane plane in planeManager.trackables)
        {
            if (plane != null)
            {
                count++;
            }
        }

        return count;
    }

    private static void SetPlaneVisualState(ARPlane plane, bool visible)
    {
        if (plane == null)
        {
            return;
        }

        Renderer[] renderers = plane.GetComponentsInChildren<Renderer>(true);
        foreach (Renderer planeRenderer in renderers)
        {
            if (planeRenderer != null)
            {
                planeRenderer.enabled = visible;
            }
        }
    }
}