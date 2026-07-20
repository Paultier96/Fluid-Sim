using UnityEngine;
using UnityEngine.InputSystem;
using Seb.Fluid2D.Rendering;
using Seb.Fluid2D.Simulation;

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
    public ParticleDisplay2D display;
    [Header("Gamepad")]
    public bool gamepadPanEnabled = true;
    [Tooltip("Camera movement in visible screen heights per second at full stick deflection.")]
    [Min(0f)] public float gamepadPanSpeed = 0.75f;
    public bool gamepadZoomEnabled = true;
    [Tooltip("Zoom scroll units per second at full input.")]
    [Min(0f)] public float gamepadZoomSpeed = 8f;
    [Range(0f, 1f)] public float gamepadZoomDeadZone = 0.15f;

    Camera cam;
    Vector3 previousPanMousePosition;
    bool isPanning;
    bool wasMouseOverCamera;

    void Awake()
    {
        cam = GetComponent<Camera>();
        if (display == null)
        {
            display = FindAnyObjectByType<ParticleDisplay2D>();
        }
    }

    void Update()
    {
        if (!controlsEnabled || !HasInputFocus())
        {
            ResetInteractionState();
            return;
        }

        ApplyGamepadPan();
        ApplyGamepadZoom();

        if (Mouse.current == null)
        {
            ResetInteractionState();
            return;
        }

        Vector3 mousePosition = Mouse.current.position.ReadValue();
        if (!cam.pixelRect.Contains(mousePosition))
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

        if (Mouse.current != null && Mouse.current.middleButton.wasPressedThisFrame)
        {
            previousPanMousePosition = mousePosition;
            isPanning = true;
        }

        if (Mouse.current != null && Mouse.current.middleButton.isPressed && isPanning)
        {
            Vector3 mouseDelta = mousePosition - previousPanMousePosition;
            Pan(mouseDelta);
            previousPanMousePosition = mousePosition;
        }

        if (Mouse.current != null && Mouse.current.middleButton.wasReleasedThisFrame)
        {
            isPanning = false;
        }

        float scroll = (Mouse.current != null ? Mouse.current.scroll.ReadValue() : Vector2.zero).y;
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

    void ApplyGamepadPan()
    {
        if (!gamepadPanEnabled)
        {
            return;
        }

        Vector2 input = ParticleFluidInteractionCursor2D.Actions.Player.Look.ReadValue<Vector2>();

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

        Vector2 movement = input * (worldHeight * gamepadPanSpeed * Time.deltaTime);
        transform.position += (Vector3)movement;
    }

    void ApplyGamepadZoom()
    {
        if (!gamepadZoomEnabled)
        {
            return;
        }

        float input = ParticleFluidInteractionCursor2D.Actions.Player.Zoom.ReadValue<float>();
        if (Mathf.Abs(input) <= gamepadZoomDeadZone)
        {
            return;
        }

        float normalizedInput = Mathf.Sign(input) * Mathf.InverseLerp(gamepadZoomDeadZone, 1f, Mathf.Abs(input));
        Zoom(normalizedInput * gamepadZoomSpeed * Time.deltaTime, GetGamepadZoomScreenPoint());
    }

    Vector3 GetGamepadZoomScreenPoint()
    {
        if (display == null)
        {
            display = FindAnyObjectByType<ParticleDisplay2D>();
        }

        if (display != null && display.interactionCursor != null)
        {
            return cam.WorldToScreenPoint(display.interactionCursor.transform.position);
        }

        return new Vector3(cam.pixelWidth * 0.5f, cam.pixelHeight * 0.5f, 0f);
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
