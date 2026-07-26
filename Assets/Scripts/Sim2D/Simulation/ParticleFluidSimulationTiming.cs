using UnityEngine;

namespace Seb.Fluid2D.Simulation
{
    internal sealed class ParticleFluidSimulationTiming
    {
        const float SlowFrameThreshold = 1.05f;
        const float FastFrameThreshold = 0.80f;
        const int SlowFrameConfirmationCount = 8;
        const int RecoveryFrameCount = 30;
        const int AdjustmentCooldownFrameCount = 30;

        int _adaptiveIterations;
        float _smoothedFrameTime;
        int _slowFrameCount;
        int _headroomFrameCount;
        int _adjustmentCooldownFrames;

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
            float targetFrameRate,
            int iterationsPerFrame,
            int maxAutoIterationsPerFrame,
            float minSubstepHz)
        {
            float displayRefreshRate = GetDisplayRefreshRate();
            float maxTimestepSeconds = GetMaxTimestepSeconds(minSubstepHz);
            int substepCount = ResolveIterationsPerFrame(
                targetFrameRate,
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
            float targetFrameRate,
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
                _slowFrameCount = 0;
                _headroomFrameCount = 0;
                _adjustmentCooldownFrames = 0;
                return Mathf.Max(1, iterationsPerFrame);
            }

            float targetFrameTime = 1f / Mathf.Max(targetFrameRate, 1f);
            float requestedSimulationFrameTime = targetFrameTime * Mathf.Max(0f, timeScale);
            int idealIterations = Mathf.CeilToInt(requestedSimulationFrameTime / maxTimestepSeconds);
            int maxIterations = Mathf.Max(1, maxAutoIterationsPerFrame);
            int qualityCeiling = unlockedTimeScale
                ? maxIterations
                : Mathf.Clamp(Mathf.Max(1, idealIterations), 1, maxIterations);
            int initialIterations = unlockedTimeScale
                ? Mathf.Clamp(Mathf.Max(1, iterationsPerFrame), 1, qualityCeiling)
                : qualityCeiling;

            return ResolveAdaptiveIterationsPerFrame(qualityCeiling, initialIterations, targetFrameTime, unscaledDeltaTime);
        }

        int ResolveAdaptiveIterationsPerFrame(int qualityCeiling, int initialIterations, float targetFrameTime, float unscaledDeltaTime)
        {
            if (_adaptiveIterations <= 0 || _adaptiveIterations > qualityCeiling)
            {
                _adaptiveIterations = Mathf.Clamp(initialIterations, 1, qualityCeiling);
                _slowFrameCount = 0;
                _headroomFrameCount = 0;
                _adjustmentCooldownFrames = 0;
            }

            if (unscaledDeltaTime <= 0f)
            {
                return _adaptiveIterations;
            }

            _smoothedFrameTime = _smoothedFrameTime <= 0f
                ? unscaledDeltaTime
                : Mathf.Lerp(_smoothedFrameTime, unscaledDeltaTime, 0.15f);
            _adjustmentCooldownFrames++;

            if (_smoothedFrameTime > targetFrameTime * SlowFrameThreshold)
            {
                _slowFrameCount++;
                _headroomFrameCount = 0;
                if (_slowFrameCount >= SlowFrameConfirmationCount &&
                    _adjustmentCooldownFrames >= AdjustmentCooldownFrameCount &&
                    _adaptiveIterations > 1)
                {
                    _adaptiveIterations--;
                    _slowFrameCount = 0;
                    _adjustmentCooldownFrames = 0;
                }
            }
            else if (_smoothedFrameTime < targetFrameTime * FastFrameThreshold && _adaptiveIterations < qualityCeiling)
            {
                _slowFrameCount = 0;
                _headroomFrameCount++;
                if (_headroomFrameCount >= RecoveryFrameCount &&
                    _adjustmentCooldownFrames >= AdjustmentCooldownFrameCount)
                {
                    _adaptiveIterations++;
                    _headroomFrameCount = 0;
                    _adjustmentCooldownFrames = 0;
                }
            }
            else
            {
                _slowFrameCount = 0;
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
