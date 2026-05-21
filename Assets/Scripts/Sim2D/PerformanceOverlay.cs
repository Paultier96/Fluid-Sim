using UnityEngine;

namespace Seb.Fluid2D.Simulation
{
    public class PerformanceOverlay : MonoBehaviour
    {
        public bool showOverlay = true;
        [Tooltip("Rolling sample window in real seconds.")]
        [Min(0.25f)] public float sampleWindow = 5f;
        public KeyCode resetKey = KeyCode.F8;
        public Vector2 screenOffset = new Vector2(12, 12);

        float[] frameTimes;
        float[] sampleTimes;
        int sampleStart;
        int sampleCount;
        float frameTimeSum;

        void Update()
        {
            if (Input.GetKeyDown(resetKey))
            {
                ResetSamples();
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
                sampleStart = (sampleStart + 1) % frameTimes.Length;
                sampleCount--;
            }

            if (sampleCount == frameTimes.Length)
            {
                GrowBuffers();
            }

            int writeIndex = (sampleStart + sampleCount) % frameTimes.Length;
            float frameTime = Time.unscaledDeltaTime;
            frameTimes[writeIndex] = frameTime;
            sampleTimes[writeIndex] = now;
            frameTimeSum += frameTime;
            sampleCount++;
        }

        void EnsureBuffers()
        {
            if (frameTimes != null && sampleTimes != null && frameTimes.Length > 0 && sampleTimes.Length == frameTimes.Length)
            {
                return;
            }

            frameTimes = new float[512];
            sampleTimes = new float[512];
            ResetSamples();
        }

        void GrowBuffers()
        {
            int oldLength = frameTimes.Length;
            if (oldLength == 0)
            {
                frameTimes = new float[512];
                sampleTimes = new float[512];
                ResetSamples();
                return;
            }

            float[] oldFrameTimes = frameTimes;
            float[] oldSampleTimes = sampleTimes;
            frameTimes = new float[oldLength * 2];
            sampleTimes = new float[oldLength * 2];

            for (int i = 0; i < sampleCount; i++)
            {
                int oldIndex = (sampleStart + i) % oldLength;
                frameTimes[i] = oldFrameTimes[oldIndex];
                sampleTimes[i] = oldSampleTimes[oldIndex];
            }

            sampleStart = 0;
        }

        void ResetSamples()
        {
            sampleStart = 0;
            sampleCount = 0;
            frameTimeSum = 0;
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

            const int width = 260;
            const int height = 128;
            float x = Mathf.Max(screenOffset.x, Screen.width - width - screenOffset.x);
            GUILayout.BeginArea(new Rect(x, screenOffset.y, width, height), GUI.skin.box);
            GUILayout.Label("Performance");
            GUILayout.Label($"Current: {currentFps:F1} fps  {currentFrameTimeMs:F2} ms");
            GUILayout.Label($"Average: {avgFps:F1} fps  {avgFrameTimeMs:F2} ms");
            GUILayout.Label($"Window: {Mathf.Max(0.25f, sampleWindow):F1}s  Samples: {sampleCount}");
            GUILayout.Label($"Min/Max frame: {minFrameTimeMs:F2} / {maxFrameTimeMs:F2} ms");
            GUILayout.Label($"Reset: {resetKey}");
            GUILayout.EndArea();
        }
    }
}
