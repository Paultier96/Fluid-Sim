using UnityEngine;

namespace Seb.Fluid2D.Simulation
{
    internal sealed class ParticleFluidSimulationTiming
    {
        const float SlowFrameThreshold = 1.05f;
        const float FastFrameThreshold = 0.80f;
        const int RecoveryFrameCount = 15;

        int _adaptiveIterations;
        float _smoothedFrameTime;
        int _headroomFrameCount;

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
                _adaptiveIterations = 0;
                _smoothedFrameTime = 0f;
                _headroomFrameCount = 0;
                return Mathf.Max(1, iterationsPerFrame);
            }

            float displayFrameTime = 1f / Mathf.Max(displayRefreshRate, 1f);
            float requestedSimulationFrameTime = displayFrameTime * Mathf.Max(0f, timeScale);
            int idealIterations = Mathf.CeilToInt(requestedSimulationFrameTime / maxTimestepSeconds);
            int maxIterations = Mathf.Max(1, maxAutoIterationsPerFrame);
            int qualityCeiling = unlockedTimeScale
                ? maxIterations
                : Mathf.Clamp(Mathf.Max(1, idealIterations), 1, maxIterations);
            int initialIterations = unlockedTimeScale
                ? Mathf.Clamp(Mathf.Max(1, iterationsPerFrame), 1, qualityCeiling)
                : qualityCeiling;

            return ResolveAdaptiveIterationsPerFrame(qualityCeiling, initialIterations, displayFrameTime, unscaledDeltaTime);
        }

        int ResolveAdaptiveIterationsPerFrame(int qualityCeiling, int initialIterations, float targetFrameTime, float unscaledDeltaTime)
        {
            if (_adaptiveIterations <= 0 || _adaptiveIterations > qualityCeiling)
            {
                _adaptiveIterations = Mathf.Clamp(initialIterations, 1, qualityCeiling);
            }

            if (unscaledDeltaTime <= 0f)
            {
                return _adaptiveIterations;
            }

            _smoothedFrameTime = _smoothedFrameTime <= 0f
                ? unscaledDeltaTime
                : Mathf.Lerp(_smoothedFrameTime, unscaledDeltaTime, 0.15f);

            if (_smoothedFrameTime > targetFrameTime * SlowFrameThreshold)
            {
                float scale = Mathf.Clamp(targetFrameTime / _smoothedFrameTime * 0.95f, 0.25f, 0.95f);
                _adaptiveIterations = Mathf.Max(1, Mathf.FloorToInt(_adaptiveIterations * scale));
                _headroomFrameCount = 0;
            }
            else if (_smoothedFrameTime < targetFrameTime * FastFrameThreshold && _adaptiveIterations < qualityCeiling)
            {
                _headroomFrameCount++;
                if (_headroomFrameCount >= RecoveryFrameCount)
                {
                    _adaptiveIterations++;
                    _headroomFrameCount = 0;
                }
            }
            else
            {
                _headroomFrameCount = 0;
            }

            return _adaptiveIterations;
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
