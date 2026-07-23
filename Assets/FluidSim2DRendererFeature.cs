using Seb.Fluid2D.Rendering;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Seb.Fluid2D.Simulation
{
    public class FluidSim2DRendererFeature : ScriptableRendererFeature
    {
        private FluidSim2DRenderPass _pass;

        public override void Create()
        {
            _pass = new FluidSim2DRenderPass
            {
                renderPassEvent = RenderPassEvent.BeforeRenderingTransparents
            };
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (!Application.isPlaying)
                return;
            if (renderingData.cameraData.cameraType != CameraType.Game)
                return;

            ParticleDisplay2D display = FindAnyObjectByType<ParticleDisplay2D>();
            if (display == null || !display.isActiveAndEnabled)
                return;
            if (display.sim == null || display.sim.resources.positionBuffer == null)
                return;

            bool canRenderMetaballs = display.renderMode == ParticleDisplay2D.RenderMode.Metaballs &&
                                      display.particleMesh != null &&
                                      display.argsBuffer != null &&
                                      display.metaballs.blurShader != null;
            if (!canRenderMetaballs)
                return;

            _pass.Setup(display);
            renderer.EnqueuePass(_pass);
        }
    }

    public class FluidSim2DRenderPass : ScriptableRenderPass
    {
        private ParticleDisplay2D _display;
        private readonly ImportedTexture _combinedAccumulation = new();
        private readonly ImportedTexture _combinedBlur = new();
        private readonly ImportedTexture _normalAccumulation = new();
        private readonly ImportedTexture _normalBlur = new();
        private readonly ImportedTexture _velocity = new();
        private readonly ImportedTexture _velocityBlur = new();
        private readonly ImportedTexture _materialAlbedo = new();
        private readonly ImportedTexture _materialNormal = new();
        private readonly ImportedTexture _materialTransport = new();
        private readonly ImportedTexture _causticResolved = new();
        private readonly ImportedTexture _causticBlur = new();
        private readonly ImportedTexture _causticTemporal = new();
        private readonly ImportedTexture _causticHistory = new();
        private readonly ImportedTexture _gradientAtlas = new();
        private readonly ImportedTexture _softLight0 = new();
        private readonly ImportedTexture _softLight1 = new();
        private readonly ImportedTexture _radianceCascade0 = new();
        private readonly ImportedTexture _radianceCascade1 = new();
        private readonly ImportedTexture _radianceCascadeSdfSeedA = new();
        private readonly ImportedTexture _radianceCascadeSdfSeedB = new();
        private readonly ImportedTexture _radianceCascadeSdfPayloadA = new();
        private readonly ImportedTexture _radianceCascadeSdfPayloadB = new();

        public void Setup(ParticleDisplay2D display)
        {
            _display = display;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            if (_display == null) return;
            if (!Application.isPlaying || _display.sim == null || _display.sim.resources.positionBuffer == null) return;

            UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
            if (!resourceData.activeColorTexture.IsValid())
                return;

            if (_display.renderMode == ParticleDisplay2D.RenderMode.Metaballs)
            {
                RecordMetaballRenderGraph(renderGraph, cameraData.camera, resourceData);
            }
        }

        private void RecordMetaballRenderGraph(RenderGraph renderGraph, Camera camera, UniversalResourceData resourceData)
        {
            MetaballRenderer2D metaballRenderer = _display.metaballs;
            ParticleFluidLighting2D lighting = _display.ActiveLighting;
            metaballRenderer.PrepareForRender(_display, camera);

            TextureHandle combinedHandle = _combinedAccumulation.Import(renderGraph, metaballRenderer.combinedAccumulationTexture, "FluidSim2D Combined Accumulation");
            TextureHandle combinedBlurHandle = _combinedBlur.Import(renderGraph, metaballRenderer.combinedBlurTexture, "FluidSim2D Combined Blur");
            TextureHandle normalHandle = _normalAccumulation.Import(renderGraph, metaballRenderer.normalAccumulationTexture, "FluidSim2D Normal Accumulation");
            TextureHandle normalBlurHandle = _normalBlur.Import(renderGraph, metaballRenderer.normalBlurTexture, "FluidSim2D Normal Blur");
            bool useMaterialPipeline = metaballRenderer.ShouldUseMaterialPipeline(_display) && metaballRenderer.MaterialRenderer.materialMaps.IsAllocated;
            bool canRenderLighting = useMaterialPipeline && lighting != null;
            bool renderCaustics = canRenderLighting && lighting.directLight.isActiveAndEnabled;
            bool renderVelocityTextures = lighting.directLight.temporalCaustics.ShouldRenderVelocityTextures(_display);
            TextureHandle velocityHandle = renderVelocityTextures ? _velocity.Import(renderGraph, metaballRenderer.velocityTexture, "FluidSim2D Velocity") : TextureHandle.nullHandle;
            TextureHandle velocityBlurHandle = renderVelocityTextures ? _velocityBlur.Import(renderGraph, metaballRenderer.velocityBlurTexture, "FluidSim2D Velocity Blur") : TextureHandle.nullHandle;
            bool temporalUsesVelocity = renderCaustics && lighting.directLight.temporalCaustics.denoisingEnabled && lighting.directLight.temporalCaustics.temporalMotionSource == ParticleFluidCausticsTemporal.TemporalMotionSource.ParticleMotion;
            TextureHandle temporalVelocityHandle = temporalUsesVelocity
                ? renderVelocityTextures ? velocityHandle : _velocity.Import(renderGraph, Texture2D.blackTexture, "FluidSim2D Velocity Fallback")
                : TextureHandle.nullHandle;
            TextureHandle materialAlbedoHandle = _materialAlbedo.Import(renderGraph, metaballRenderer.MaterialRenderer.materialMaps.albedoTexture, "FluidSim2D Material Albedo");
            TextureHandle materialNormalHandle = _materialNormal.Import(renderGraph, metaballRenderer.MaterialRenderer.materialMaps.normalTexture, "FluidSim2D Material Normal");
            TextureHandle materialTransportHandle = _materialTransport.Import(renderGraph, metaballRenderer.MaterialRenderer.materialMaps.transportTexture, "FluidSim2D Material Transport");
            TextureHandle gradientAtlasHandle = _gradientAtlas.Import(renderGraph, _display.gradientAtlasTexture != null ? _display.gradientAtlasTexture : Texture2D.blackTexture, "FluidSim2D Gradient Atlas");

            metaballRenderer.RecordAccumulationRenderGraph(renderGraph, combinedHandle, normalHandle, velocityHandle, renderVelocityTextures);

            metaballRenderer.RecordSurfaceBlurRenderGraph(renderGraph, combinedHandle, combinedBlurHandle, normalHandle, normalBlurHandle);

            if (useMaterialPipeline && materialAlbedoHandle.IsValid() && materialNormalHandle.IsValid() && materialTransportHandle.IsValid())
            {
                metaballRenderer.RecordMaterialMapsRenderGraph(renderGraph, combinedHandle, normalHandle, gradientAtlasHandle, materialAlbedoHandle, materialNormalHandle, materialTransportHandle);
            }

            metaballRenderer.RecordVelocityBlurRenderGraph(renderGraph, velocityHandle, velocityBlurHandle, renderVelocityTextures);

            bool renderPhaseDiffuseLight = canRenderLighting && lighting.gaussianSss.ShouldRender;
            bool renderRadianceCascadeLight = canRenderLighting && lighting.radianceCascadeGi.isActiveAndEnabled;
            LightingResourceHandles lightingResources = canRenderLighting ? ImportLightingResources(renderGraph, lighting, "FluidSim2D", renderCaustics, renderPhaseDiffuseLight, renderRadianceCascadeLight) : default;
            ParticleFluidLighting2D.FrameContext lightingContext = canRenderLighting ? metaballRenderer.CreateLightingContext(_display, camera) : default;
            LightingInputHandles lightingInputs = new()
            {
                transport = materialTransportHandle,
                materialNormal = materialNormalHandle,
                velocity = temporalVelocityHandle,
                gradientAtlas = gradientAtlasHandle
            };
            
            if (canRenderLighting)
            {
                lighting.RecordRenderGraph(renderGraph, metaballRenderer, lightingContext, lightingInputs, lightingResources, renderCaustics, renderPhaseDiffuseLight, renderRadianceCascadeLight, renderVelocityTextures);
            }

            metaballRenderer.RecordCompositeRenderGraph(
                renderGraph,
                resourceData.activeColorTexture,
                useMaterialPipeline,
                combinedHandle,
                normalHandle,
                velocityHandle,
                materialAlbedoHandle,
                materialNormalHandle,
                materialTransportHandle,
                lightingResources);
        }

        private LightingResourceHandles ImportLightingResources(RenderGraph renderGraph, ParticleFluidLighting2D lighting, string prefix, bool renderCaustics, bool renderPhaseDiffuseLight, bool renderRadianceCascadeLight)
        {
            LightingResourceHandles handles = default;

            handles.causticResolved = renderCaustics ? _causticResolved.Import(renderGraph, lighting.directLight.causticResolvedTexture, $"{prefix} Caustic Resolved") : TextureHandle.nullHandle;
            handles.causticBlur = renderCaustics ? _causticBlur.Import(renderGraph, lighting.directLight.causticBlurTexture, $"{prefix} Caustic Blur") : TextureHandle.nullHandle;
            handles.causticTemporal = renderCaustics && lighting.directLight.temporalCaustics.denoisingEnabled ? _causticTemporal.Import(renderGraph, lighting.directLight.temporalCaustics.causticTemporalTexture, $"{prefix} Caustic Temporal") : TextureHandle.nullHandle;
            handles.causticHistory = renderCaustics && lighting.directLight.temporalCaustics.denoisingEnabled ? _causticHistory.Import(renderGraph, lighting.directLight.temporalCaustics.causticHistoryTexture, $"{prefix} Caustic History") : TextureHandle.nullHandle;
            handles.gaussianSoftLight0 = renderPhaseDiffuseLight ? _softLight0.Import(renderGraph, lighting.gaussianSss.gaussianSoftLightTexture0, $"{prefix} Gaussian Soft Light 0") : TextureHandle.nullHandle;
            handles.gaussianSoftLight1 = renderPhaseDiffuseLight ? _softLight1.Import(renderGraph, lighting.gaussianSss.gaussianSoftLightTexture1, $"{prefix} Gaussian Soft Light 1") : TextureHandle.nullHandle;
            handles.radianceCascade0 = renderRadianceCascadeLight ? _radianceCascade0.Import(renderGraph, lighting.radianceCascadeGi.radianceCascadeTexture0, $"{prefix} Radiance Cascade 0") : TextureHandle.nullHandle;
            handles.radianceCascade1 = renderRadianceCascadeLight ? _radianceCascade1.Import(renderGraph, lighting.radianceCascadeGi.radianceCascadeTexture1, $"{prefix} Radiance Cascade 1") : TextureHandle.nullHandle;
            handles.radianceCascadeSdfSeedA = renderRadianceCascadeLight ? _radianceCascadeSdfSeedA.Import(renderGraph, lighting.radianceCascadeGi.radianceCascadeSdfSeedA, $"{prefix} RC SDF Seed A") : TextureHandle.nullHandle;
            handles.radianceCascadeSdfSeedB = renderRadianceCascadeLight ? _radianceCascadeSdfSeedB.Import(renderGraph, lighting.radianceCascadeGi.radianceCascadeSdfSeedB, $"{prefix} RC SDF Seed B") : TextureHandle.nullHandle;
            handles.radianceCascadeSdfPayloadA = renderRadianceCascadeLight ? _radianceCascadeSdfPayloadA.Import(renderGraph, lighting.radianceCascadeGi.radianceCascadeSdfPayloadA, $"{prefix} RC SDF Payload A") : TextureHandle.nullHandle;
            handles.radianceCascadeSdfPayloadB = renderRadianceCascadeLight ? _radianceCascadeSdfPayloadB.Import(renderGraph, lighting.radianceCascadeGi.radianceCascadeSdfPayloadB, $"{prefix} RC SDF Payload B") : TextureHandle.nullHandle;

            return handles;
        }

        private sealed class ImportedTexture
        {
            private RTHandle _handle;
            private RenderTargetInfo _info;
            private Texture _sourceTexture;

            public TextureHandle Import(RenderGraph renderGraph, RenderTexture texture, string name)
            {
                if (texture == null)
                {
                    return TextureHandle.nullHandle;
                }

                if (_handle == null || _sourceTexture != texture)
                {
                    _handle?.Release();
                    _handle = RTHandles.Alloc(texture, name);
                    _sourceTexture = texture;
                }

                _info = new RenderTargetInfo
                {
                    format = texture.graphicsFormat,
                    width = texture.width,
                    height = texture.height,
                    volumeDepth = texture.volumeDepth,
                    msaaSamples = 1,
                    bindMS = texture.bindTextureMS
                };
                return renderGraph.ImportTexture(_handle, _info);
            }

            public TextureHandle Import(RenderGraph renderGraph, Texture texture, string name)
            {
                if (texture == null)
                {
                    return TextureHandle.nullHandle;
                }

                if (texture is RenderTexture renderTexture)
                {
                    return Import(renderGraph, renderTexture, name);
                }

                if (_handle == null || _sourceTexture != texture)
                {
                    _handle?.Release();
                    _handle = RTHandles.Alloc(texture);
                    _sourceTexture = texture;
                }

                return renderGraph.ImportTexture(_handle);
            }
        }

    }

    internal struct LightingInputHandles
	{
		public TextureHandle transport;
		public TextureHandle materialNormal;
		public TextureHandle velocity;
		public TextureHandle gradientAtlas;
	}

    internal struct LightingResourceHandles
    {
        public TextureHandle causticResolved;
        public TextureHandle causticBlur;
        public TextureHandle causticTemporal;
        public TextureHandle causticHistory;
        public TextureHandle gaussianSoftLight0;
        public TextureHandle gaussianSoftLight1;
        public TextureHandle radianceCascade0;
        public TextureHandle radianceCascade1;
        public TextureHandle radianceCascadeSdfSeedA;
        public TextureHandle radianceCascadeSdfSeedB;
        public TextureHandle radianceCascadeSdfPayloadA;
        public TextureHandle radianceCascadeSdfPayloadB;
    }
}
