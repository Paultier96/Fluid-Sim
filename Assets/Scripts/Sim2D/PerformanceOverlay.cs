using UnityEngine;
using UnityEngine.Rendering;

namespace Seb.Fluid2D.Simulation
{
    public class PerformanceOverlay : MonoBehaviour
    {
        public bool showOverlay = true;
        public FluidSim2D sim;
        [Tooltip("Rolling sample window in real seconds.")]
        [Min(0.25f)] public float sampleWindow = 5f;
        [Min(40f)] public float graphHeight = 70f;
        [Min(1f)] public float graphMaxFps = 120f;
        [Min(1f)] public float graphReferenceFps = 60f;
        [Min(1f)] public float frameTimeGraphMaxMs = 50f;
        [Min(0.1f)] public float frameTimeGraphReferenceMs = 16.67f;
        public KeyCode resetKey = KeyCode.F8;
        public Vector2 screenOffset = new Vector2(12, 12);

        float[] frameTimes;
        float[] simulationTimes;
        float[] sampleTimes;
        int sampleStart;
        int sampleCount;
        float frameTimeSum;
        float simulationTimeSum;

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
        }

        void OnGUI()
        {
            if (!showOverlay)
            {
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
            GUILayout.Label("Performance");
            GUILayout.Label($"API: {SystemInfo.graphicsDeviceType}  Pipeline: {GetPipelineName()}");
            GUILayout.Label($"Current: {currentFps:F1} fps  {currentFrameTimeMs:F2} ms");
            GUILayout.Label($"Average: {avgFps:F1} fps  {avgFrameTimeMs:F2} ms");
            if (sim != null)
            {
                GUILayout.Label($"Playback: {sim.CurrentPlaybackSpeed:F2}x current  {avgPlaybackSpeed:F2}x avg{(sim.unlockedTimeScale ? "  unlocked" : "")}");
                GUILayout.Label($"Sim frame/step: {sim.CurrentSimulationDeltaTime * 1000f:F2} / {sim.CurrentSimulationSubstepDeltaTime * 1000f:F2} ms");
                GUILayout.Label($"Substeps: {sim.CurrentSimulationSubstepCount}  Display: {sim.CurrentDisplayRefreshRate:F1} Hz");
            }
            GUILayout.Label($"Window: {Mathf.Max(0.25f, sampleWindow):F1}s  Samples: {sampleCount}");
            GUILayout.Label($"Min/Max frame: {minFrameTimeMs:F2} / {maxFrameTimeMs:F2} ms");
            GUILayout.Label($"FPS graph: 0-{Mathf.Max(1f, graphMaxFps):F0} fps");
            Rect graphRect = GUILayoutUtility.GetRect(width - 20, graphDrawHeight);
            DrawFpsGraph(graphRect, Mathf.Max(1f, graphMaxFps));
            GUILayout.Label($"Frame graph: 0-{Mathf.Max(1f, frameTimeGraphMaxMs):F0} ms");
            Rect frameTimeGraphRect = GUILayoutUtility.GetRect(width - 20, graphDrawHeight);
            DrawFrameTimeGraph(frameTimeGraphRect, Mathf.Max(1f, frameTimeGraphMaxMs));
            GUILayout.Label($"Reset: {resetKey}");
            GUILayout.EndArea();
        }

        void DrawFpsGraph(Rect rect, float maxFps)
        {
            if (Event.current.type != EventType.Repaint)
            {
                return;
            }

            Color previousColor = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.35f);
            GUI.DrawTexture(rect, Texture2D.whiteTexture);

            DrawHorizontalGraphLine(rect, maxFps, graphReferenceFps, new Color(1f, 1f, 1f, 0.22f));
            DrawHorizontalGraphLine(rect, maxFps, maxFps * 0.5f, new Color(1f, 1f, 1f, 0.12f));

            if (sampleCount > 1 && maxFps > 0)
            {
                float now = Time.unscaledTime;
                float window = Mathf.Max(0.25f, sampleWindow);
                Vector2 previousPoint = Vector2.zero;
                bool hasPreviousPoint = false;

                for (int i = 0; i < sampleCount; i++)
                {
                    int sampleIndex = (sampleStart + i) % frameTimes.Length;
                    float age = now - sampleTimes[sampleIndex];
                    float normalizedX = Mathf.Clamp01(1f - age / window);
                    float fps = frameTimes[sampleIndex] > 0 ? 1f / frameTimes[sampleIndex] : 0f;
                    float normalizedY = Mathf.Clamp01(fps / maxFps);
                    Vector2 point = new Vector2(rect.xMin + normalizedX * rect.width, rect.yMax - normalizedY * rect.height);

                    if (hasPreviousPoint)
                    {
                        DrawLine(previousPoint, point, new Color(0.25f, 0.9f, 0.35f, 1f), 2f);
                    }

                    previousPoint = point;
                    hasPreviousPoint = true;
                }
            }

            GUI.color = new Color(1f, 1f, 1f, 0.5f);
            GUI.DrawTexture(new Rect(rect.xMin, rect.yMin, rect.width, 1f), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(rect.xMin, rect.yMax - 1f, rect.width, 1f), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(rect.xMin, rect.yMin, 1f, rect.height), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(rect.xMax - 1f, rect.yMin, 1f, rect.height), Texture2D.whiteTexture);
            GUI.color = previousColor;
        }

        void DrawFrameTimeGraph(Rect rect, float maxFrameTimeMs)
        {
            if (Event.current.type != EventType.Repaint)
            {
                return;
            }

            Color previousColor = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.35f);
            GUI.DrawTexture(rect, Texture2D.whiteTexture);

            DrawHorizontalGraphLine(rect, maxFrameTimeMs, frameTimeGraphReferenceMs, new Color(1f, 1f, 1f, 0.22f));
            DrawHorizontalGraphLine(rect, maxFrameTimeMs, maxFrameTimeMs * 0.5f, new Color(1f, 1f, 1f, 0.12f));

            if (sampleCount > 1 && maxFrameTimeMs > 0)
            {
                float now = Time.unscaledTime;
                float window = Mathf.Max(0.25f, sampleWindow);
                Vector2 previousPoint = Vector2.zero;
                bool hasPreviousPoint = false;

                for (int i = 0; i < sampleCount; i++)
                {
                    int sampleIndex = (sampleStart + i) % frameTimes.Length;
                    float age = now - sampleTimes[sampleIndex];
                    float normalizedX = Mathf.Clamp01(1f - age / window);
                    float frameTimeMs = frameTimes[sampleIndex] * 1000f;
                    float normalizedY = Mathf.Clamp01(frameTimeMs / maxFrameTimeMs);
                    Vector2 point = new Vector2(rect.xMin + normalizedX * rect.width, rect.yMax - normalizedY * rect.height);

                    if (hasPreviousPoint)
                    {
                        DrawLine(previousPoint, point, new Color(1f, 0.65f, 0.2f, 1f), 2f);
                    }

                    previousPoint = point;
                    hasPreviousPoint = true;
                }
            }

            DrawGraphBorder(rect);
            GUI.color = previousColor;
        }

        void DrawHorizontalGraphLine(Rect rect, float maxValue, float value, Color color)
        {
            if (maxValue <= 0 || value <= 0 || value > maxValue)
            {
                return;
            }

            Color previousColor = GUI.color;
            GUI.color = color;
            float y = rect.yMax - Mathf.Clamp01(value / maxValue) * rect.height;
            GUI.DrawTexture(new Rect(rect.xMin, y, rect.width, 1f), Texture2D.whiteTexture);
            GUI.color = previousColor;
        }

        void DrawGraphBorder(Rect rect)
        {
            GUI.color = new Color(1f, 1f, 1f, 0.5f);
            GUI.DrawTexture(new Rect(rect.xMin, rect.yMin, rect.width, 1f), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(rect.xMin, rect.yMax - 1f, rect.width, 1f), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(rect.xMin, rect.yMin, 1f, rect.height), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(rect.xMax - 1f, rect.yMin, 1f, rect.height), Texture2D.whiteTexture);
        }

        static void DrawLine(Vector2 start, Vector2 end, Color color, float thickness)
        {
            Matrix4x4 previousMatrix = GUI.matrix;
            Color previousColor = GUI.color;
            Vector2 direction = end - start;
            float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;

            GUI.color = color;
            GUIUtility.RotateAroundPivot(angle, start);
            GUI.DrawTexture(new Rect(start.x, start.y - thickness * 0.5f, direction.magnitude, thickness), Texture2D.whiteTexture);
            GUI.matrix = previousMatrix;
            GUI.color = previousColor;
        }

        static string GetPipelineName()
        {
            RenderPipelineAsset pipeline = GraphicsSettings.currentRenderPipeline;
            return pipeline != null ? pipeline.GetType().Name : "Built-in";
        }
    }
}
