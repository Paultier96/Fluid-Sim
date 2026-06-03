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
            int height = sim != null ? 190 : 148;
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
            GUILayout.Label($"Reset: {resetKey}");
            GUILayout.EndArea();
        }

        static string GetPipelineName()
        {
            RenderPipelineAsset pipeline = GraphicsSettings.currentRenderPipeline;
            return pipeline != null ? pipeline.GetType().Name : "Built-in";
        }
    }
}
