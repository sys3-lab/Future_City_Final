using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using TMPro;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
#if UNITY_VISIONOS
using UnityEngine.XR.VisionOS;
#endif

public class AnchorManager : MonoBehaviour
{
    public ARAnchorManager anchorManager;
    public List<GameObject> publicGameObjects; // 存储多个公共 GameObject
    private int currentAnchorIndex = 0; // 当前锚点索引

    public Vector3 modelPosition; // Store position
    public Quaternion modelRotation; // Store rotation

    public GameObject alignWall;

    void Start()
    {
    #if UNITY_VISIONOS
        if (LoaderUtility.GetActiveLoader()?.GetLoadedSubsystem<XRAnchorSubsystem>() != null)
        {
            Debug.Log("XRAnchorSubsystem was loaded. The platform supports anchors.");
            CheckForOptionalFeatureSupport(anchorManager);
        }
    #else
    // Quest P0 暂不运行 visionOS Anchor 链路。
        enabled = false;
    #endif
    }

    void CheckForOptionalFeatureSupport(ARAnchorManager manager)
    {
        // Use manager.descriptor to determine which optional features
        // are supported on the device. For example:

        if (manager.descriptor.supportsTrackableAttachments)
        {
            Debug.Log("Trackable attachments are supported.");
        }
    }

    void OnEnable()
    {
        if (anchorManager != null)
        {
            anchorManager.trackablesChanged.AddListener(OnAnchorsChanged);
            Debug.Log("AnchorsChanged event subscribed on enable.");
        }
    }

    void OnDisable()
    {
        if (anchorManager != null)
        {
            anchorManager.trackablesChanged.RemoveListener(OnAnchorsChanged);
        }
    }

    public void ToggleARPlane()
    {
        // hide the align wall
        if (alignWall != null)
        {
            alignWall.SetActive(false);
        }
        
        // hide menu
        GameObject menu = GameObject.Find("Menu");
        if (menu != null)
        {
            // @Carton
            // menu.SetActive(false);
        }

        if (anchorManager == null)
        {
            Debug.LogError("AR Anchor Manager is not initialized.");
            //debugText.text = "AR Anchor Manager is not initialized.";
            return;
        }

        // disable the component
        var arPlane = GetComponent<ARPlaneManager>();

        // @Carton
        var meshManager = transform.Find("MeshManager").gameObject;

        if (arPlane.enabled)
        {
            // disable the ARPlaneManager
            arPlane.enabled = false;

            // @Carton
            if (meshManager != null)
            meshManager.SetActive(false);

            // disable the ARPlanes
            GameObject trackables = GameObject.Find("XR Interaction Hands Setup/XRRig/Trackables");
            foreach (Transform child in trackables.transform)
            {
                // @Carton
                if (
                    child.gameObject.name.StartsWith("Mesh")
                    || child.gameObject.name.StartsWith("ARPlane")
                )
                {
                    child.gameObject.SetActive(false);
                }
            }
        }
        else
        {
            arPlane.enabled = true;
            // enable the ARPlanes

            // @Carton
            if (meshManager != null)
            meshManager.SetActive(true);

            GameObject trackables = GameObject.Find("XRRig/Trackables");
            foreach (Transform child in trackables.transform)
            {
                // @Carton
                if (
                    child.gameObject.name.StartsWith("Mesh")
                    || child.gameObject.name.StartsWith("ARPlane")
                )
                {
                    child.gameObject.SetActive(true);
                }
            }
        }
    }

    public void ToggleMenu(){
        GameObject menu = GameObject.Find("Menu");
        if (menu != null)
        {
            menu.SetActive(!menu.activeSelf);
        }
    }

    public async void CreateAnchor()
    {
        await CreateAnchorWithDelay();
    }

    private async Task CreateAnchorWithDelay()
    {
        // 确保 AR 系统已启动
        while (ARSession.state != ARSessionState.SessionTracking)
        {
            Debug.Log("Waiting for AR system to initialize...");
            await Task.Yield(); // 用于异步等待，不会阻塞主线程
        }

        // 检查 ARAnchorManager 的初始化
        if (anchorManager == null)
        {
            Debug.LogError("AR Anchor Manager is not initialized.");
            return;
        }

        Debug.Log("AR Anchor Manager is initialized.");

        // 使用当前索引选择 GameObject
        if (publicGameObjects.Count == 0)
        {
            Debug.LogError("No public GameObjects assigned.");
            return;
        }
        RemoveAllAnchors();

        GameObject currentGameObject = publicGameObjects[currentAnchorIndex];

        modelPosition = currentGameObject.transform.position;
        modelRotation = currentGameObject.transform.rotation;
        // modelPosition = mainCamera.transform.position;
        // modelRotation = mainCamera.transform.rotation; 
        // 创建锚点
        var anchorPose = new Pose(modelPosition, modelRotation);

        try
        {
            var result = await anchorManager.TryAddAnchorAsync(anchorPose);
            if (result.status.IsSuccess())
            {
                var anchor = result.value;
                Debug.Log("Anchor created: " + anchor);
                currentGameObject.transform.SetParent(anchor.transform);
                Debug.Log($"{currentGameObject.transform.name}" + $"{anchor.transform.name}");
            }
            else
            {
                Debug.LogError("Failed to create anchor. Status: " + result.status);
            }
        }
        catch (Exception ex)
        {
            Debug.LogError("Exception occurred while creating anchor: " + ex.Message);
        }

        // 更新索引以便下次调用时使用下一个 GameObject
        currentAnchorIndex = (currentAnchorIndex + 1) % publicGameObjects.Count;
    }

    void RemoveAllAnchors()
    {
        if (anchorManager == null)
        {
            Debug.LogError("AR Anchor Manager is not initialized.");
            //debugText.text = "AR Anchor Manager is not initialized.";
            return;
        }

        // 获取所有当前存在的 anchors
        var anchors = anchorManager.trackables;
        foreach (var anchor in anchors)
        {
            Destroy(anchor);
            Debug.Log("Destroyed anchor: " + anchor);
            //debugText.text = "Destroyed anchor at position: " + anchor.transform.position;
        }

        Debug.Log("All anchors removed.");
    }

    public void OnAnchorsChanged(ARTrackablesChangedEventArgs<ARAnchor> eventArgs)
    {
        Debug.Log("OnAnchorsChanged called.");
        //debugText.text = "OnAnchorsChanged called.";

        foreach (ARAnchor addedAnchor in eventArgs.added)
        {
            Debug.Log(
                $"Anchor added - Position: {addedAnchor.transform.position}, Rotation: {addedAnchor.transform.rotation}, UUID: {addedAnchor.trackableId}"
            );
            publicGameObjects[currentAnchorIndex]
                .transform.SetPositionAndRotation(
                    addedAnchor.transform.position,
                    addedAnchor.transform.rotation
                );

            publicGameObjects[currentAnchorIndex].transform.SetParent(addedAnchor.transform);
        }

        foreach (ARAnchor updatedAnchor in eventArgs.updated)
        {
            var isTracking = updatedAnchor.trackingState == TrackingState.Tracking;
            if (isTracking)
            {
                // @Carton
                // Do not update, stay at the original position
                // publicGameObjects[currentAnchorIndex].transform.position = modelPosition;
                // publicGameObjects[currentAnchorIndex].transform.rotation = modelRotation;
            }
            Debug.Log(
                $"Anchor updated - Position: {updatedAnchor.transform.position}, Rotation: {updatedAnchor.transform.rotation}, UUID: {updatedAnchor.trackableId}"
            );
        }

        foreach (KeyValuePair<TrackableId, ARAnchor> removedAnchor in eventArgs.removed)
        {
            Debug.Log("removing Anchor, UUID: " + removedAnchor.Key);
            // Check if the removed anchor is a child of the current GameObject
            if (
                publicGameObjects[currentAnchorIndex]
                    .transform.IsChildOf(removedAnchor.Value.transform)
            )
            {
                Debug.Log(
                    "creating another anchor as this anchor contains the Keller Hall Model as child GameObject."
                );
                CreateAnchor();
            }
            else
            {
                Destroy(removedAnchor.Value);
                Debug.Log("Destroyed Anchor UUID:" + removedAnchor.Key);
            }
        }
    }
}
