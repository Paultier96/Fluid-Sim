using UnityEngine;

[RequireComponent(typeof(Camera))]
public class Camera2D : MonoBehaviour
{
    [Tooltip("Enable Scene-view style navigation: scroll to zoom, middle mouse drag to pan.")]
    public bool controlsEnabled = true;
    [Tooltip("Multiplicative zoom speed for the mouse wheel.")]
    [Min(0.001f)] public float zoomSpeed = 0.12f;
    [Tooltip("Minimum orthographic size when zooming.")]
    [Min(0.001f)] public float minZoom = 0.1f;
    [Tooltip("Maximum orthographic size when zooming.")]
    [Min(0.001f)] public float maxZoom = 100f;
    [Tooltip("When enabled, orthographic zoom keeps the world point under the cursor fixed.")]
    public bool zoomTowardMouse = true;

    Camera cam;
    Vector3 previousPanMousePosition;
    bool isPanning;
    bool wasMouseOverCamera;

    void Awake()
    {
        cam = GetComponent<Camera>();
    }

    void Update()
    {
        if (!controlsEnabled)
        {
            ResetInteractionState();
            return;
        }

        if (cam == null)
        {
            cam = GetComponent<Camera>();
        }

        if (!HasInputFocus())
        {
            ResetInteractionState();
            return;
        }

        Vector3 mousePosition = Input.mousePosition;
        bool mouseOverCamera = cam.pixelRect.Contains(mousePosition);
        if (!mouseOverCamera)
        {
            ResetInteractionState();
            return;
        }

        if (!wasMouseOverCamera)
        {
            wasMouseOverCamera = true;
            previousPanMousePosition = mousePosition;
            return;
        }

        if (Input.GetMouseButtonDown(2))
        {
            previousPanMousePosition = mousePosition;
            isPanning = true;
        }

        if (Input.GetMouseButton(2) && isPanning)
        {
            Vector3 mouseDelta = mousePosition - previousPanMousePosition;
            Pan(mouseDelta);
            previousPanMousePosition = mousePosition;
        }

        if (Input.GetMouseButtonUp(2))
        {
            isPanning = false;
        }

        float scroll = Input.mouseScrollDelta.y;
        if (Mathf.Abs(scroll) > 0.0001f)
        {
            Zoom(scroll, mousePosition);
        }
    }

    void OnApplicationFocus(bool hasFocus)
    {
        if (!hasFocus)
        {
            ResetInteractionState();
        }
    }

    void OnApplicationPause(bool isPaused)
    {
        if (isPaused)
        {
            ResetInteractionState();
        }
    }

    void ResetInteractionState()
    {
        isPanning = false;
        wasMouseOverCamera = false;
    }

    bool HasInputFocus()
    {
#if UNITY_EDITOR
        return UnityEditor.EditorWindow.focusedWindow != null &&
            UnityEditor.EditorWindow.focusedWindow.GetType().Name == "GameView";
#else
        return Application.isFocused;
#endif
    }

    void Pan(Vector3 mouseDelta)
    {
        float worldHeight;
        if (cam.orthographic)
        {
            worldHeight = cam.orthographicSize * 2f;
        }
        else
        {
            float distance = GetCameraPlaneDistance();
            worldHeight = 2f * distance * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
        }

        float worldWidth = worldHeight * cam.aspect;
        Vector3 moveRight = transform.right * (-mouseDelta.x / Mathf.Max(cam.pixelWidth, 1) * worldWidth);
        Vector3 moveUp = transform.up * (-mouseDelta.y / Mathf.Max(cam.pixelHeight, 1) * worldHeight);
        transform.position += moveRight + moveUp;
    }

    void Zoom(float scroll, Vector3 mousePosition)
    {
        if (cam.orthographic)
        {
            Vector3 worldBeforeZoom = zoomTowardMouse ? ScreenToWorldOnSimulationPlane(mousePosition) : Vector3.zero;
            float zoomFactor = Mathf.Exp(-scroll * zoomSpeed);
            cam.orthographicSize = Mathf.Clamp(cam.orthographicSize * zoomFactor, minZoom, maxZoom);

            if (zoomTowardMouse)
            {
                Vector3 worldAfterZoom = ScreenToWorldOnSimulationPlane(mousePosition);
                transform.position += worldBeforeZoom - worldAfterZoom;
            }
        }
        else
        {
            float distance = GetCameraPlaneDistance();
            float moveDistance = scroll * zoomSpeed * Mathf.Max(distance, 0.001f);
            transform.position += transform.forward * moveDistance;
        }
    }

    Vector3 ScreenToWorldOnSimulationPlane(Vector3 mousePosition)
    {
        Plane plane = new Plane(Vector3.forward, Vector3.zero);
        Ray ray = cam.ScreenPointToRay(mousePosition);
        return plane.Raycast(ray, out float distance) ? ray.GetPoint(distance) : cam.ScreenToWorldPoint(mousePosition);
    }

    float GetCameraPlaneDistance()
    {
        float distance = Mathf.Abs(Vector3.Dot(Vector3.zero - transform.position, transform.forward));
        return Mathf.Max(distance, 0.001f);
    }
}
