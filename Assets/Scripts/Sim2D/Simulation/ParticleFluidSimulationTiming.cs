using UnityEngine;

namespace Seb.Fluid2D.Simulation
{
    internal sealed class ParticleFluidSimulationTiming
    {
        int _unlockedAdaptiveIterations;

        public readonly struct Frame
        {
            public readonly float deltaTime;
            public readonly float substepDeltaTime;
            public readonly int substepCount;
            public readonly float playbackSpeed;
            public readonly float displayRefreshRate;
            public readonly int resolvedIterationsPerFrame;

            public Frame(float deltaTime, float substepDeltaTime, int substepCount, float playbackSpeed, float displayRefreshRate, int resolvedIterationsPerFrame)
            {
                this.deltaTime = deltaTime;
                this.substepDeltaTime = substepDeltaTime;
                this.substepCount = substepCount;
                this.playbackSpeed = playbackSpeed;
                this.displayRefreshRate = displayRefreshRate;
                this.resolvedIterationsPerFrame = resolvedIterationsPerFrame;
            }
        }

        public Frame ResolveFrame(
            float deltaTime,
            float unscaledDeltaTime,
            float timeScale,
            bool unlockedTimeScale,
            bool autoIterationsPerFrame,
            int iterationsPerFrame,
            int maxAutoIterationsPerFrame,
            float minSubstepHz)
        {
            float displayRefreshRate = GetDisplayRefreshRate();
            float maxTimestepSeconds = GetMaxTimestepSeconds(minSubstepHz);
            int substepCount = ResolveIterationsPerFrame(
                displayRefreshRate,
                timeScale,
                unlockedTimeScale,
                autoIterationsPerFrame,
                iterationsPerFrame,
                maxAutoIterationsPerFrame,
                maxTimestepSeconds,
                unscaledDeltaTime);

            float maxFrameTime = maxTimestepSeconds * substepCount;
            float resolvedDeltaTime = unlockedTimeScale ? maxFrameTime : Mathf.Min(deltaTime * timeScale, maxFrameTime);
            float substepDeltaTime = resolvedDeltaTime / substepCount;
            float playbackSpeed = unscaledDeltaTime > 0 ? resolvedDeltaTime / unscaledDeltaTime : 0f;
            return new Frame(resolvedDeltaTime, substepDeltaTime, substepCount, playbackSpeed, displayRefreshRate, substepCount);
        }

        public static Frame PausedFrame(float displayRefreshRate)
        {
            return new Frame(0f, 0f, 0, 0f, displayRefreshRate, 0);
        }

        int ResolveIterationsPerFrame(
            float displayRefreshRate,
            float timeScale,
            bool unlockedTimeScale,
            bool autoIterationsPerFrame,
            int iterationsPerFrame,
            int maxAutoIterationsPerFrame,
            float maxTimestepSeconds,
            float unscaledDeltaTime)
        {
            if (!autoIterationsPerFrame)
            {
                return Mathf.Max(1, iterationsPerFrame);
            }

            if (unlockedTimeScale)
            {
                return ResolveUnlockedIterationsPerFrame(displayRefreshRate, iterationsPerFrame, maxAutoIterationsPerFrame, unscaledDeltaTime);
            }

            float displayFrameTime = 1f / Mathf.Max(displayRefreshRate, 1f);
            float requestedSimulationFrameTime = displayFrameTime * Mathf.Max(0f, timeScale);
            int idealIterations = Mathf.CeilToInt(requestedSimulationFrameTime / maxTimestepSeconds);
            return Mathf.Clamp(Mathf.Max(1, idealIterations), 1, Mathf.Max(1, maxAutoIterationsPerFrame));
        }

        int ResolveUnlockedIterationsPerFrame(float displayRefreshRate, int iterationsPerFrame, int maxAutoIterationsPerFrame, float unscaledDeltaTime)
        {
            int maxIterations = Mathf.Max(1, maxAutoIterationsPerFrame);
            if (_unlockedAdaptiveIterations <= 0 || _unlockedAdaptiveIterations > maxIterations)
            {
                _unlockedAdaptiveIterations = Mathf.Clamp(Mathf.Max(1, iterationsPerFrame), 1, maxIterations);
            }

            float targetFrameTime = 1f / Mathf.Max(displayRefreshRate, 1f);
            if (unscaledDeltaTime > 0f)
            {
                if (unscaledDeltaTime > targetFrameTime)
                {
                    float scale = Mathf.Clamp(targetFrameTime / unscaledDeltaTime * 0.9f, 0.25f, 0.95f);
                    _unlockedAdaptiveIterations = Mathf.Max(1, Mathf.FloorToInt(_unlockedAdaptiveIterations * scale));
                }
                else if (unscaledDeltaTime < targetFrameTime * 0.85f && _unlockedAdaptiveIterations < maxIterations)
                {
                    _unlockedAdaptiveIterations++;
                }
            }

            return _unlockedAdaptiveIterations;
        }

        static float GetDisplayRefreshRate()
        {
            return (float)Screen.currentResolution.refreshRateRatio.value;
        }

        static float GetMaxTimestepSeconds(float minSubstepHz)
        {
            return 1f / Mathf.Max(1f, minSubstepHz);
        }
    }
}
