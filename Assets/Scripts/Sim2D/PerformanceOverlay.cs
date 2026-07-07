using UnityEngine;
using UnityEngine.Rendering;

namespace Seb.Fluid2D.Simulation
{
    public class PerformanceOverlay : MonoBehaviour
    {
        public enum OverlayMode
        {
            Full,
            FpsOnly
        }

        public bool showOverlay = true;
        public OverlayMode overlayMode = OverlayMode.Full;
        public FluidSim2D sim;
        [Tooltip("Rolling sample window in real seconds.")]
        [Min(0.25f)] public float sampleWindow = 5f;
        [Min(40f)] public float graphHeight = 70f;
        [Min(1f)] public float graphMaxFps = 120f;
        [Min(1f)] public float graphReferenceFps = 60f;
        [Min(1f)] public float frameTimeGraphMaxMs = 50f;
        [Min(0.1f)] public float frameTimeGraphReferenceMs = 16.67f;
        [Min(1f)] public float graphRefreshRate = 60f;
        [Tooltip("Refresh rate for the lightweight FPS label.")]
        [Min(0.05f)] public float fpsOnlyRefreshInterval = 0.25f;
        public KeyCode resetKey = KeyCode.F8;
        public Vector2 screenOffset = new Vector2(12, 12);

        float[] frameTimes;
        float[] simulationTimes;
        float[] sampleTimes;
        int sampleStart;
        int sampleCount;
        float frameTimeSum;
        float simulationTimeSum;
        Texture2D fpsGraphTexture;
        Texture2D frameTimeGraphTexture;
        Color32[] fpsGraphPixels;
        Color32[] frameTimeGraphPixels;
        float nextFpsGraphUpdateTime;
        float nextFrameTimeGraphUpdateTime;
        float fpsOnlyAccumulatedTime;
        int fpsOnlyAccumulatedFrames;
        float fpsOnlyNextRefreshTime;
        string fpsOnlyLabel = "FPS: 0";

        void Awake()
        {
            if (sim == null)
            {
                sim = GetComponent<FluidSim2D>();
            }
        }

        void Update()
        {
            if (Input.GetKeyDown(resetKey))
            {
                ResetSamples();
            }
        }

        void LateUpdate()
        {
            if (overlayMode == OverlayMode.FpsOnly)
            {
                UpdateFpsOnlyLabel();
                return;
            }

            RecordSample();
        }

        void RecordSample()
        {
            if (!showOverlay)
            {
                return;
            }

            EnsureBuffers();

            float now = Time.unscaledTime;
            float window = Mathf.Max(0.25f, sampleWindow);
            while (sampleCount > 0 && now - sampleTimes[sampleStart] > window)
            {
                frameTimeSum -= frameTimes[sampleStart];
                simulationTimeSum -= simulationTimes[sampleStart];
                sampleStart = (sampleStart + 1) % frameTimes.Length;
                sampleCount--;
            }

            if (sampleCount == frameTimes.Length)
            {
                GrowBuffers();
            }

            int writeIndex = (sampleStart + sampleCount) % frameTimes.Length;
            float frameTime = Time.unscaledDeltaTime;
            float simulationTime = sim != null ? sim.CurrentSimulationDeltaTime : 0f;
            frameTimes[writeIndex] = frameTime;
            simulationTimes[writeIndex] = simulationTime;
            sampleTimes[writeIndex] = now;
            frameTimeSum += frameTime;
            simulationTimeSum += simulationTime;
            sampleCount++;
        }

        void EnsureBuffers()
        {
            if (frameTimes != null && simulationTimes != null && sampleTimes != null && frameTimes.Length > 0 && simulationTimes.Length == frameTimes.Length && sampleTimes.Length == frameTimes.Length)
            {
                return;
            }

            frameTimes = new float[512];
            simulationTimes = new float[512];
            sampleTimes = new float[512];
            ResetSamples();
        }

        void GrowBuffers()
        {
            int oldLength = frameTimes.Length;
            if (oldLength == 0)
            {
                frameTimes = new float[512];
                simulationTimes = new float[512];
                sampleTimes = new float[512];
                ResetSamples();
                return;
            }

            float[] oldFrameTimes = frameTimes;
            float[] oldSimulationTimes = simulationTimes;
            float[] oldSampleTimes = sampleTimes;
            frameTimes = new float[oldLength * 2];
            simulationTimes = new float[oldLength * 2];
            sampleTimes = new float[oldLength * 2];

            for (int i = 0; i < sampleCount; i++)
            {
                int oldIndex = (sampleStart + i) % oldLength;
                frameTimes[i] = oldFrameTimes[oldIndex];
                simulationTimes[i] = oldSimulationTimes[oldIndex];
                sampleTimes[i] = oldSampleTimes[oldIndex];
            }

            sampleStart = 0;
        }

        void ResetSamples()
        {
            sampleStart = 0;
            sampleCount = 0;
            frameTimeSum = 0;
            simulationTimeSum = 0;
            nextFpsGraphUpdateTime = 0;
            nextFrameTimeGraphUpdateTime = 0;
            fpsOnlyAccumulatedTime = 0;
            fpsOnlyAccumulatedFrames = 0;
            fpsOnlyNextRefreshTime = 0;
            fpsOnlyLabel = "FPS: 0";
        }

        void OnDestroy()
        {
            DestroyGraphTexture(ref fpsGraphTexture, ref fpsGraphPixels);
            DestroyGraphTexture(ref frameTimeGraphTexture, ref frameTimeGraphPixels);
        }

        void OnGUI()
        {
            if (!showOverlay)
            {
                return;
            }

            if (overlayMode == OverlayMode.FpsOnly)
            {
                DrawFpsOnlyOverlay();
                return;
            }

            EnsureBuffers();

            float avgFrameTimeMs = sampleCount > 0 ? frameTimeSum / sampleCount * 1000f : 0;
            float avgFps = avgFrameTimeMs > 0 ? 1000f / avgFrameTimeMs : 0;
            float avgPlaybackSpeed = frameTimeSum > 0 ? simulationTimeSum / frameTimeSum : 0;
            float currentFrameTimeMs = Time.unscaledDeltaTime * 1000f;
            float currentFps = Time.unscaledDeltaTime > 0 ? 1f / Time.unscaledDeltaTime : 0;
            float minFrameTimeMs = float.PositiveInfinity;
            float maxFrameTimeMs = 0;

            for (int i = 0; i < sampleCount; i++)
            {
                int sampleIndex = (sampleStart + i) % frameTimes.Length;
                float frameTimeMs = frameTimes[sampleIndex] * 1000f;
                minFrameTimeMs = Mathf.Min(minFrameTimeMs, frameTimeMs);
                maxFrameTimeMs = Mathf.Max(maxFrameTimeMs, frameTimeMs);
            }

            if (sampleCount == 0)
            {
                minFrameTimeMs = 0;
            }

            const int width = 280;
            float graphDrawHeight = Mathf.Max(40f, graphHeight);
            float lineHeight = Mathf.Max(20f, GUI.skin.label.CalcHeight(new GUIContent("Performance"), width - 20) + GUI.skin.label.margin.vertical);
            float verticalPadding = GUI.skin.box.padding.vertical + GUI.skin.box.margin.vertical + 48f;
            int labelCount = sim != null ? 12 : 9;
            int height = Mathf.RoundToInt(labelCount * lineHeight + graphDrawHeight * 2f + verticalPadding);
            float x = Mathf.Max(screenOffset.x, Screen.width - width - screenOffset.x);
            GUILayout.BeginArea(new Rect(x, screenOffset.y, width, height), GUI.skin.box);
            GUILayout.Label($"Average: {avgFps:F1} fps  {avgFrameTimeMs:F2} ms");
            if (sim != null)
            {
                GUILayout.Label($"Playback:  {avgPlaybackSpeed:F2}x avg{(sim.unlockedTimeScale ? "  unlocked" : "")}");
                GUILayout.Label($"Sim frame/step: {sim.CurrentSimulationDeltaTime * 1000f:F2} / {sim.CurrentSimulationSubstepDeltaTime * 1000f:F2} ms");
                GUILayout.Label($"Substeps: {sim.CurrentSimulationSubstepCount}  Display: {sim.CurrentDisplayRefreshRate:F1} Hz");
            }
            GUILayout.Label($"Window: {Mathf.Max(0.25f, sampleWindow):F1}s  Samples: {sampleCount}");
            GUILayout.Label($"Min/Max frame: {minFrameTimeMs:F2} / {maxFrameTimeMs:F2} ms");
            GUILayout.Label($"FPS graph: 0-{Mathf.Max(1f, graphMaxFps):F0} fps");
            Rect graphRect = GUILayoutUtility.GetRect(width - 20, graphDrawHeight);
            DrawFpsGraphTexture(graphRect, Mathf.Max(1f, graphMaxFps));
            GUILayout.Label($"Frame graph: 0-{Mathf.Max(1f, frameTimeGraphMaxMs):F0} ms");
            Rect frameTimeGraphRect = GUILayoutUtility.GetRect(width - 20, graphDrawHeight);
            DrawFrameTimeGraphTexture(frameTimeGraphRect, Mathf.Max(1f, frameTimeGraphMaxMs));
            GUILayout.Label($"Reset: {resetKey}");
            GUILayout.EndArea();
        }

        void UpdateFpsOnlyLabel()
        {
            fpsOnlyAccumulatedFrames++;
            fpsOnlyAccumulatedTime += Time.unscaledDeltaTime;

            float now = Time.unscaledTime;
            if (now < fpsOnlyNextRefreshTime || fpsOnlyAccumulatedTime <= 0f)
            {
                return;
            }

            float fps = fpsOnlyAccumulatedFrames / fpsOnlyAccumulatedTime;
            fpsOnlyLabel = $"FPS: {fps:F1}";
            fpsOnlyAccumulatedFrames = 0;
            fpsOnlyAccumulatedTime = 0f;
            fpsOnlyNextRefreshTime = now + Mathf.Max(0.05f, fpsOnlyRefreshInterval);
        }

        void DrawFpsOnlyOverlay()
        {
            const float width = 96f;
            const float height = 28f;
            float x = Mathf.Max(screenOffset.x, Screen.width - width - screenOffset.x);
            Rect rect = new Rect(x, screenOffset.y, width, height);
            GUI.Label(rect, fpsOnlyLabel, GUI.skin.box);
        }

        void DrawFpsGraphTexture(Rect rect, float maxFps)
        {
            DrawGraphTexture(
                rect,
                maxFps,
                graphReferenceFps,
                true,
                new Color32(64, 230, 89, 255),
                ref fpsGraphTexture,
                ref fpsGraphPixels,
                ref nextFpsGraphUpdateTime);
        }

        void DrawFrameTimeGraphTexture(Rect rect, float maxFrameTimeMs)
        {
            DrawGraphTexture(
                rect,
                maxFrameTimeMs,
                frameTimeGraphReferenceMs,
                false,
                new Color32(255, 166, 51, 255),
                ref frameTimeGraphTexture,
                ref frameTimeGraphPixels,
                ref nextFrameTimeGraphUpdateTime);
        }

        void DrawGraphTexture(
            Rect rect,
            float maxValue,
            float referenceValue,
            bool drawFps,
            Color32 traceColor,
            ref Texture2D texture,
            ref Color32[] pixels,
            ref float nextUpdateTime)
        {
            if (Event.current.type != EventType.Repaint)
            {
                return;
            }

            int width = Mathf.Max(1, Mathf.RoundToInt(rect.width));
            int height = Mathf.Max(1, Mathf.RoundToInt(rect.height));
            bool textureChanged = EnsureGraphTexture(width, height, ref texture, ref pixels);

            float now = Time.unscaledTime;
            if (textureChanged || now >= nextUpdateTime)
            {
                RebuildGraphTexture(texture, pixels, width, height, maxValue, referenceValue, drawFps, traceColor);
                nextUpdateTime = now + 1f / Mathf.Max(1f, graphRefreshRate);
            }

            GUI.DrawTexture(rect, texture);
        }

        void RebuildGraphTexture(Texture2D texture, Color32[] pixels, int width, int height, float maxValue, float referenceValue, bool drawFps, Color32 traceColor)
        {
            Color32 backgroundColor = new Color32(0, 0, 0, 90);
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = backgroundColor;
            }

            Color32 guideColor = new Color32(255, 255, 255, 55);
            Color32 halfGuideColor = new Color32(255, 255, 255, 31);
            Color32 borderColor = new Color32(255, 255, 255, 127);
            DrawGraphHorizontalLine(pixels, width, height, maxValue, referenceValue, guideColor);
            DrawGraphHorizontalLine(pixels, width, height, maxValue, maxValue * 0.5f, halfGuideColor);
            DrawGraphBorder(pixels, width, height, borderColor);

            if (sampleCount > 1 && maxValue > 0)
            {
                float now = Time.unscaledTime;
                float window = Mathf.Max(0.25f, sampleWindow);
                int previousX = 0;
                int previousY = 0;
                bool hasPreviousPoint = false;

                for (int i = 0; i < sampleCount; i++)
                {
                    int sampleIndex = (sampleStart + i) % frameTimes.Length;
                    float age = now - sampleTimes[sampleIndex];
                    float normalizedX = Mathf.Clamp01(1f - age / window);
                    float value = drawFps
                        ? (frameTimes[sampleIndex] > 0 ? 1f / frameTimes[sampleIndex] : 0f)
                        : frameTimes[sampleIndex] * 1000f;
                    float normalizedY = Mathf.Clamp01(value / maxValue);
                    int x = Mathf.Clamp(Mathf.RoundToInt(normalizedX * (width - 1)), 0, width - 1);
                    int y = Mathf.Clamp(Mathf.RoundToInt(normalizedY * (height - 1)), 0, height - 1);

                    if (hasPreviousPoint)
                    {
                        DrawGraphLine(pixels, width, height, previousX, previousY, x, y, traceColor);
                    }

                    previousX = x;
                    previousY = y;
                    hasPreviousPoint = true;
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply(false);
        }

        static bool EnsureGraphTexture(int width, int height, ref Texture2D texture, ref Color32[] pixels)
        {
            if (texture != null && texture.width == width && texture.height == height && pixels != null && pixels.Length == width * height)
            {
                return false;
            }

            DestroyGraphTexture(ref texture, ref pixels);
            texture = new Texture2D(width, height, TextureFormat.RGBA32, false, true)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.DontSave
            };
            pixels = new Color32[width * height];
            return true;
        }

        static void DestroyGraphTexture(ref Texture2D texture, ref Color32[] pixels)
        {
            if (texture != null)
            {
                UnityEngine.Object.Destroy(texture);
            }

            texture = null;
            pixels = null;
        }

        static void DrawGraphHorizontalLine(Color32[] pixels, int width, int height, float maxValue, float value, Color32 color)
        {
            if (maxValue <= 0 || value <= 0 || value > maxValue)
            {
                return;
            }

            int y = Mathf.Clamp(Mathf.RoundToInt(Mathf.Clamp01(value / maxValue) * (height - 1)), 0, height - 1);
            int rowStart = y * width;
            for (int x = 0; x < width; x++)
            {
                pixels[rowStart + x] = color;
            }
        }

        static void DrawGraphBorder(Color32[] pixels, int width, int height, Color32 color)
        {
            int lastX = width - 1;
            int lastY = height - 1;
            for (int x = 0; x < width; x++)
            {
                pixels[x] = color;
                pixels[lastY * width + x] = color;
            }

            for (int y = 0; y < height; y++)
            {
                pixels[y * width] = color;
                pixels[y * width + lastX] = color;
            }
        }

        static void DrawGraphLine(Color32[] pixels, int width, int height, int x0, int y0, int x1, int y1, Color32 color)
        {
            int dx = Mathf.Abs(x1 - x0);
            int sx = x0 < x1 ? 1 : -1;
            int dy = -Mathf.Abs(y1 - y0);
            int sy = y0 < y1 ? 1 : -1;
            int error = dx + dy;

            while (true)
            {
                if (x0 >= 0 && x0 < width && y0 >= 0 && y0 < height)
                {
                    pixels[y0 * width + x0] = color;
                }

                if (x0 == x1 && y0 == y1)
                {
                    break;
                }

                int doubleError = error * 2;
                if (doubleError >= dy)
                {
                    error += dy;
                    x0 += sx;
                }

                if (doubleError <= dx)
                {
                    error += dx;
                    y0 += sy;
                }
            }
        }

        static string GetPipelineName()
        {
            RenderPipelineAsset pipeline = GraphicsSettings.currentRenderPipeline;
            return pipeline != null ? pipeline.GetType().Name : "Built-in";
        }
    }
}

