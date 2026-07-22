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
        private static readonly int BlurRadius = Shader.PropertyToID("blurRadius");
        private static readonly int BlurDirection = Shader.PropertyToID("blurDirection");
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
            this._display = display;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            if (_display == null)
                return;
            if (!Application.isPlaying || _display.sim == null || _display.sim.resources.positionBuffer == null)
                return;

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
            metaballRenderer.PrepareForRender(_display, camera);
            ParticleFluidLighting2D lighting = _display.ActiveLighting;
            TextureHandle combinedHandle = _combinedAccumulation.Import(renderGraph, metaballRenderer.combinedAccumulationTexture, "FluidSim2D Combined Accumulation");
            TextureHandle combinedBlurHandle = _combinedBlur.Import(renderGraph, metaballRenderer.combinedBlurTexture, "FluidSim2D Combined Blur");
            TextureHandle normalHandle = _normalAccumulation.Import(renderGraph, metaballRenderer.normalAccumulationTexture, "FluidSim2D Normal Accumulation");
            TextureHandle normalBlurHandle = _normalBlur.Import(renderGraph, metaballRenderer.normalBlurTexture, "FluidSim2D Normal Blur");
            bool renderVelocityTextures = lighting.directLight.temporalCaustics.ShouldRenderVelocityTextures(_display);
            bool useMaterialPipeline = metaballRenderer.ShouldUseMaterialPipeline(_display) && metaballRenderer.MaterialRenderer.materialMaps.IsAllocated;
            bool canRenderLighting = useMaterialPipeline && lighting != null;
            bool renderCaustics = canRenderLighting && lighting.directLight.isActiveAndEnabled;
            TextureHandle velocityHandle = renderVelocityTextures ? _velocity.Import(renderGraph, metaballRenderer.velocityTexture, "FluidSim2D Velocity") : TextureHandle.nullHandle;
            TextureHandle velocityBlurHandle = renderVelocityTextures ? _velocityBlur.Import(renderGraph, metaballRenderer.velocityBlurTexture, "FluidSim2D Velocity Blur") : TextureHandle.nullHandle;
            bool temporalUsesVelocity = renderCaustics && lighting.directLight.temporalCaustics.denoisingEnabled && lighting.directLight.temporalCaustics.temporalMotionSource == ParticleFluidCausticsTemporal.TemporalMotionSource.ParticleMotion;
            TextureHandle temporalVelocityHandle = temporalUsesVelocity
                ? (renderVelocityTextures ? velocityHandle : _velocity.Import(renderGraph, Texture2D.blackTexture, "FluidSim2D Velocity Fallback"))
                : TextureHandle.nullHandle;
            TextureHandle materialAlbedoHandle = _materialAlbedo.Import(renderGraph, metaballRenderer.MaterialRenderer.materialMaps.albedoTexture, "FluidSim2D Material Albedo");
            TextureHandle materialNormalHandle = _materialNormal.Import(renderGraph, metaballRenderer.MaterialRenderer.materialMaps.normalTexture, "FluidSim2D Material Normal");
            TextureHandle materialTransportHandle = _materialTransport.Import(renderGraph, metaballRenderer.MaterialRenderer.materialMaps.transportTexture, "FluidSim2D Material Transport");
            TextureHandle gradientAtlasHandle = _gradientAtlas.Import(renderGraph, _display.gradientAtlasTexture != null ? _display.gradientAtlasTexture : Texture2D.blackTexture, "FluidSim2D Gradient Atlas");

            RecordMetaballAccumulationPass(renderGraph, "Fluid Sim 2D Combined Accumulation", _display, metaballRenderer, combinedHandle, 0);
            RecordMetaballAccumulationPass(renderGraph, "Fluid Sim 2D Normal Accumulation", _display, metaballRenderer, normalHandle, 1);
            if (renderVelocityTextures)
            {
                RecordMetaballAccumulationPass(renderGraph, "Fluid Sim 2D Velocity Accumulation", _display, metaballRenderer, velocityHandle, 2);
            }

            float surfaceBlurRadius = _display.EffectiveConfiguredBlurRadius * _display.GetZoomScale(camera) * metaballRenderer.renderTextureScale;
            RecordBlurPass(renderGraph, "Fluid Sim 2D Combined Blur Horizontal", combinedHandle, combinedBlurHandle, metaballRenderer.combinedAccumulationTexture, metaballRenderer.combinedBlurTexture, metaballRenderer.blurMaterial, surfaceBlurRadius, new Vector2(1f, 0f));
            RecordBlurPass(renderGraph, "Fluid Sim 2D Combined Blur Vertical", combinedBlurHandle, combinedHandle, metaballRenderer.combinedBlurTexture, metaballRenderer.combinedAccumulationTexture, metaballRenderer.blurMaterial, surfaceBlurRadius, new Vector2(0f, 1f));
            RecordBlurPass(renderGraph, "Fluid Sim 2D Normal Blur Horizontal", normalHandle, normalBlurHandle, metaballRenderer.normalAccumulationTexture, metaballRenderer.normalBlurTexture, metaballRenderer.blurMaterial, surfaceBlurRadius, new Vector2(1f, 0f));
            RecordBlurPass(renderGraph, "Fluid Sim 2D Normal Blur Vertical", normalBlurHandle, normalHandle, metaballRenderer.normalBlurTexture, metaballRenderer.normalAccumulationTexture, metaballRenderer.blurMaterial, surfaceBlurRadius, new Vector2(0f, 1f));

            if (useMaterialPipeline && materialAlbedoHandle.IsValid() && materialNormalHandle.IsValid() && materialTransportHandle.IsValid())
            {
                using var builder = renderGraph.AddUnsafePass<MaterialMapPassData>("Fluid Sim 2D Material Maps", out var passData);
                passData.metaballRenderer = metaballRenderer;
                passData.combined = combinedHandle;
                passData.normal = normalHandle;
                passData.gradientAtlas = gradientAtlasHandle;
                passData.albedo = materialAlbedoHandle;
                passData.materialNormal = materialNormalHandle;
                passData.transport = materialTransportHandle;
                UseIfValid(builder, passData.combined, AccessFlags.Read);
                UseIfValid(builder, passData.normal, AccessFlags.Read);
                UseIfValid(builder, passData.gradientAtlas, AccessFlags.Read);
                UseIfValid(builder, passData.albedo, AccessFlags.Write);
                UseIfValid(builder, passData.materialNormal, AccessFlags.Write);
                UseIfValid(builder, passData.transport, AccessFlags.Write);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc(static (MaterialMapPassData data, UnsafeGraphContext context) =>
                {
                    CommandBuffer nativeCommandBuffer = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);
                    data.metaballRenderer.RecordMaterialMaps(nativeCommandBuffer);
                });
            }

            if (renderVelocityTextures && metaballRenderer.EffectiveVelocityBlurRadius > 0.001f)
            {
                using var builder = renderGraph.AddUnsafePass<VelocityBlurPassData>("Fluid Sim 2D Velocity Gaussian Blur", out var passData);
                passData.metaballRenderer = metaballRenderer;
                passData.velocity = velocityHandle;
                passData.velocityScratch = velocityBlurHandle;
                UseIfValid(builder, passData.velocity, AccessFlags.ReadWrite);
                UseIfValid(builder, passData.velocityScratch, AccessFlags.ReadWrite);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc(static (VelocityBlurPassData data, UnsafeGraphContext context) =>
                {
                    CommandBuffer nativeCommandBuffer = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);
                    data.metaballRenderer.RecordVelocityGaussianBlur(nativeCommandBuffer);
                });
            }

            bool renderPhaseDiffuseLight = canRenderLighting && lighting.gaussianSss.ShouldRender();
            bool renderRadianceCascadeLight = canRenderLighting && lighting.radianceCascadeGi.isActiveAndEnabled;
            bool renderSoftLight = renderPhaseDiffuseLight || renderRadianceCascadeLight;
            LightingResourceHandles lightingResources = canRenderLighting
                ? ImportLightingResources(renderGraph, lighting, "FluidSim2D", renderCaustics, renderPhaseDiffuseLight, renderRadianceCascadeLight)
                : default;
            ParticleFluidLighting2D.FrameContext lightingContext = canRenderLighting
                ? metaballRenderer.CreateLightingContext(_display, camera)
                : default;
            int causticsFrameIndex = renderCaustics ? lighting.directLight.causticFrameIndex++ : 0;
            LightingInputHandles lightingInputs = new()
            {
                transport = materialTransportHandle,
                materialNormal = materialNormalHandle,
                velocity = temporalVelocityHandle,
                gradientAtlas = gradientAtlasHandle
            };
            if (canRenderLighting)
            {
                lighting.ApplyMaterialMaps(metaballRenderer.MaterialRenderer.materialMaps);
                Texture causticTexture = lighting.directLight.GetCurrentDirectLightTexture();
                lighting.ApplySettings(lightingContext, renderCaustics, renderSoftLight, causticTexture);
                lighting.directLight.temporalCaustics.ApplyTemporalSettings(
                    lightingContext,
                    renderVelocityTextures ? metaballRenderer.velocityTexture : null);
            }

            if (renderCaustics)
            {
                RecordCausticsPasses(renderGraph, "Fluid Sim 2D", lighting, lightingContext, lightingInputs, lightingResources, causticsFrameIndex);
            }

            if (renderSoftLight)
            {
                RecordSoftLightPass(renderGraph, "Fluid Sim 2D", lighting, lightingContext, lightingInputs, lightingResources);
            }

            using (var builder = renderGraph.AddUnsafePass<CompositePassData>("Fluid Sim 2D Composite", out var passData))
            {
                passData.display = _display;
                passData.metaballRenderer = metaballRenderer;
                passData.camera = camera;
                passData.useMaterialPipeline = useMaterialPipeline;
                passData.color = resourceData.activeColorTexture;
                passData.depth = resourceData.activeDepthTexture;
                passData.combined = combinedHandle;
                passData.normal = normalHandle;
                passData.velocity = velocityHandle;
                passData.materialAlbedo = materialAlbedoHandle;
                passData.materialNormal = materialNormalHandle;
                passData.materialTransport = materialTransportHandle;
                passData.causticResolved = lightingResources.causticResolved;
                passData.causticTemporal = lightingResources.causticTemporal;
                passData.gaussianSoftLight0 = lightingResources.gaussianSoftLight0;
                passData.gaussianSoftLight1 = lightingResources.gaussianSoftLight1;
                passData.radianceCascade0 = lightingResources.radianceCascade0;
                passData.radianceCascade1 = lightingResources.radianceCascade1;
                UseIfValid(builder, passData.color, AccessFlags.Write);
                UseIfValid(builder, passData.depth, AccessFlags.ReadWrite);
                UseIfValid(builder, passData.combined, AccessFlags.Read);
                UseIfValid(builder, passData.normal, AccessFlags.Read);
                UseIfValid(builder, passData.velocity, AccessFlags.Read);
                UseIfValid(builder, passData.materialAlbedo, AccessFlags.Read);
                UseIfValid(builder, passData.materialNormal, AccessFlags.Read);
                UseIfValid(builder, passData.materialTransport, AccessFlags.Read);
                UseIfValid(builder, passData.causticResolved, AccessFlags.Read);
                UseIfValid(builder, passData.causticTemporal, AccessFlags.Read);
                UseIfValid(builder, passData.gaussianSoftLight0, AccessFlags.Read);
                UseIfValid(builder, passData.gaussianSoftLight1, AccessFlags.Read);
                UseIfValid(builder, passData.radianceCascade0, AccessFlags.Read);
                UseIfValid(builder, passData.radianceCascade1, AccessFlags.Read);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc(static (CompositePassData data, UnsafeGraphContext context) =>
                {
                    if (data.depth.IsValid())
                    {
                        context.cmd.SetRenderTarget(data.color, data.depth);
                    }
                    else
                    {
                        context.cmd.SetRenderTarget(data.color);
                    }

                    CommandBuffer nativeCommandBuffer = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);
                    if (data.useMaterialPipeline)
                    {
                        data.metaballRenderer.RecordCompositeWithPreparedMaterialMaps(data.display, data.camera, nativeCommandBuffer, data.color);
                    }
                    else
                    {
                        data.metaballRenderer.RecordComposite(data.display, data.camera, nativeCommandBuffer, data.color);
                    }
                });
            }
        }

        private static void RecordMetaballAccumulationPass(RenderGraph renderGraph, string passName, ParticleDisplay2D display, MetaballRenderer2D metaballRenderer, TextureHandle target, int shaderPass)
        {
            using var builder = renderGraph.AddRasterRenderPass<MetaballAccumulationPassData>(passName, out var passData);

            passData.display = display;
            passData.metaballRenderer = metaballRenderer;
            passData.shaderPass = shaderPass;
            builder.SetRenderAttachment(target, 0);
            builder.AllowGlobalStateModification(true);
            builder.AllowPassCulling(false);
            builder.SetRenderFunc(static (MetaballAccumulationPassData data, RasterGraphContext context) =>
            {
                data.metaballRenderer.RecordAccumulationTarget(data.display, context.cmd, data.shaderPass);
            });
        }

        private static void RecordBlurPass(RenderGraph renderGraph, string passName, TextureHandle source, TextureHandle destination, RenderTexture sourceTexture, RenderTexture destinationTexture, Material material, float blurRadius, Vector2 direction)
        {
            if (!source.IsValid() || !destination.IsValid() || sourceTexture == null || destinationTexture == null || material == null)
            {
                return;
            }

            using var builder = renderGraph.AddUnsafePass<BlurPassData>(passName, out var passData);

            passData.sourceTexture = sourceTexture;
            passData.destinationTexture = destinationTexture;
            passData.material = material;
            passData.blurRadius = blurRadius;
            passData.direction = direction;
            builder.UseTexture(source);
            builder.UseTexture(destination, AccessFlags.Write);
            builder.AllowPassCulling(false);
            builder.SetRenderFunc(static (BlurPassData data, UnsafeGraphContext context) =>
            {
                CommandBuffer nativeCommandBuffer = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);
                nativeCommandBuffer.SetGlobalFloat(BlurRadius, data.blurRadius);
                nativeCommandBuffer.SetGlobalVector(BlurDirection, data.direction);
                nativeCommandBuffer.Blit(data.sourceTexture, data.destinationTexture, data.material);
            });
        }

        private static void UseIfValid(IBaseRenderGraphBuilder builder, TextureHandle texture, AccessFlags accessFlags)
        {
            if (texture.IsValid())
            {
                builder.UseTexture(texture, accessFlags);
            }
        }

        private LightingResourceHandles ImportLightingResources(RenderGraph renderGraph, ParticleFluidLighting2D lighting, string prefix, bool renderCaustics, bool renderPhaseDiffuseLight, bool renderRadianceCascadeLight)
        {
            LightingResourceHandles handles = default;
            bool renderSoftLight = renderPhaseDiffuseLight || renderRadianceCascadeLight;
            bool renderSdfRadianceCascade = renderSoftLight && renderRadianceCascadeLight;

            handles.causticResolved = renderCaustics ? _causticResolved.Import(renderGraph, lighting.directLight.causticResolvedTexture, $"{prefix} Caustic Resolved") : TextureHandle.nullHandle;
            handles.causticBlur = renderCaustics ? _causticBlur.Import(renderGraph, lighting.directLight.causticBlurTexture, $"{prefix} Caustic Blur") : TextureHandle.nullHandle;
            handles.causticTemporal = renderCaustics && lighting.directLight.temporalCaustics.denoisingEnabled ? _causticTemporal.Import(renderGraph, lighting.directLight.temporalCaustics.causticTemporalTexture, $"{prefix} Caustic Temporal") : TextureHandle.nullHandle;
            handles.causticHistory = renderCaustics && lighting.directLight.temporalCaustics.denoisingEnabled ? _causticHistory.Import(renderGraph, lighting.directLight.temporalCaustics.causticHistoryTexture, $"{prefix} Caustic History") : TextureHandle.nullHandle;
            handles.gaussianSoftLight0 = renderPhaseDiffuseLight ? _softLight0.Import(renderGraph, lighting.gaussianSss.gaussianSoftLightTexture0, $"{prefix} Gaussian Soft Light 0") : TextureHandle.nullHandle;
            handles.gaussianSoftLight1 = renderPhaseDiffuseLight ? _softLight1.Import(renderGraph, lighting.gaussianSss.gaussianSoftLightTexture1, $"{prefix} Gaussian Soft Light 1") : TextureHandle.nullHandle;
            handles.radianceCascade0 = renderRadianceCascadeLight ? _radianceCascade0.Import(renderGraph, lighting.radianceCascadeGi.radianceCascadeTexture0, $"{prefix} Radiance Cascade 0") : TextureHandle.nullHandle;
            handles.radianceCascade1 = renderRadianceCascadeLight ? _radianceCascade1.Import(renderGraph, lighting.radianceCascadeGi.radianceCascadeTexture1, $"{prefix} Radiance Cascade 1") : TextureHandle.nullHandle;
            handles.radianceCascadeSdfSeedA = renderSdfRadianceCascade ? _radianceCascadeSdfSeedA.Import(renderGraph, lighting.radianceCascadeGi.radianceCascadeSdfSeedA, $"{prefix} RC SDF Seed A") : TextureHandle.nullHandle;
            handles.radianceCascadeSdfSeedB = renderSdfRadianceCascade ? _radianceCascadeSdfSeedB.Import(renderGraph, lighting.radianceCascadeGi.radianceCascadeSdfSeedB, $"{prefix} RC SDF Seed B") : TextureHandle.nullHandle;
            handles.radianceCascadeSdfPayloadA = renderSdfRadianceCascade ? _radianceCascadeSdfPayloadA.Import(renderGraph, lighting.radianceCascadeGi.radianceCascadeSdfPayloadA, $"{prefix} RC SDF Payload A") : TextureHandle.nullHandle;
            handles.radianceCascadeSdfPayloadB = renderSdfRadianceCascade ? _radianceCascadeSdfPayloadB.Import(renderGraph, lighting.radianceCascadeGi.radianceCascadeSdfPayloadB, $"{prefix} RC SDF Payload B") : TextureHandle.nullHandle;

            return handles;
        }

        private static void RecordCausticsPasses(RenderGraph renderGraph, string prefix, ParticleFluidLighting2D lighting, ParticleFluidLighting2D.FrameContext context, LightingInputHandles inputs, LightingResourceHandles resources, int frameIndex)
        {
            using (var builder = renderGraph.AddComputePass<CausticsComputePassData>($"{prefix} Caustics Clear", out var passData))
            {
                passData.lighting = lighting;
                passData.context = context;
                passData.frameIndex = frameIndex;
                passData.causticResolved = resources.causticResolved;
                UseIfValid(builder, passData.causticResolved, AccessFlags.Write);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc(static (CausticsComputePassData data, ComputeGraphContext context) =>
                {
                    data.lighting.directLight.RecordComputeClear(data.context, context.cmd, data.frameIndex, data.causticResolved);
                });
            }

            using (var builder = renderGraph.AddComputePass<CausticsComputePassData>($"{prefix} Caustics Trace", out var passData))
            {
                passData.lighting = lighting;
                passData.context = context;
				passData.frameIndex = frameIndex;
				passData.transport = inputs.transport;
				passData.materialNormal = inputs.materialNormal;
				passData.gradientAtlas = inputs.gradientAtlas;
				UseIfValid(builder, passData.transport, AccessFlags.Read);
				UseIfValid(builder, passData.materialNormal, AccessFlags.Read);
				UseIfValid(builder, passData.gradientAtlas, AccessFlags.Read);
				builder.AllowPassCulling(false);
				builder.SetRenderFunc(static (CausticsComputePassData data, ComputeGraphContext context) =>
				{
					data.lighting.directLight.RecordComputeTrace(data.context, context.cmd, data.frameIndex, data.transport, data.materialNormal, data.gradientAtlas);
				});
			}

            using (var builder = renderGraph.AddComputePass<CausticsComputePassData>($"{prefix} Caustics Resolve", out var passData))
            {
                passData.lighting = lighting;
                passData.context = context;
                passData.frameIndex = frameIndex;
                passData.causticResolved = resources.causticResolved;
                UseIfValid(builder, passData.causticResolved, AccessFlags.Write);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc(static (CausticsComputePassData data, ComputeGraphContext context) =>
                {
                    data.lighting.directLight.RecordComputeResolve(data.context, context.cmd, data.frameIndex, data.causticResolved);
                });
            }

            using (var builder = renderGraph.AddUnsafePass<CausticsBlurPassData>($"{prefix} Caustics Blur", out var passData))
            {
                passData.lighting = lighting;
                passData.context = context;
                passData.causticResolved = resources.causticResolved;
                passData.causticBlur = resources.causticBlur;
                UseIfValid(builder, passData.causticResolved, AccessFlags.ReadWrite);
                UseIfValid(builder, passData.causticBlur, AccessFlags.Write);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc(static (CausticsBlurPassData data, UnsafeGraphContext context) =>
                {
                    CommandBuffer nativeCommandBuffer = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);
                    data.lighting.directLight.RecordBlur(data.context.display.metaballs.renderTextureScale, nativeCommandBuffer);
                });
            }

            using (var builder = renderGraph.AddUnsafePass<CausticsTemporalPassData>($"{prefix} Caustics Temporal", out var passData))
            {
                passData.lighting = lighting;
                passData.context = context;
                passData.velocity = inputs.velocity;
                passData.causticResolved = resources.causticResolved;
                passData.causticTemporal = resources.causticTemporal;
                passData.causticHistory = resources.causticHistory;
                UseIfValid(builder, passData.causticResolved, AccessFlags.Read);
                UseIfValid(builder, passData.causticTemporal, AccessFlags.ReadWrite);
                UseIfValid(builder, passData.causticHistory, AccessFlags.ReadWrite);
                UseIfValid(builder, passData.velocity, AccessFlags.Read);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc(static (CausticsTemporalPassData data, UnsafeGraphContext context) =>
                {
                    CommandBuffer nativeCommandBuffer = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);
                    data.lighting.directLight.temporalCaustics.RecordTemporal(data.context, nativeCommandBuffer, data.lighting.directLight.causticResolvedTexture);
                });
            }
        }

        private static void RecordSoftLightPass(RenderGraph renderGraph, string prefix, ParticleFluidLighting2D lighting, ParticleFluidLighting2D.FrameContext context, LightingInputHandles inputs, LightingResourceHandles resources)
        {
            using var builder = renderGraph.AddUnsafePass<CausticsSoftLightPassData>($"{prefix} Caustics Soft Light", out var passData);
            passData.lighting = lighting;
            passData.context = context;
            passData.transport = inputs.transport;
            passData.causticResolved = resources.causticResolved;
            passData.causticTemporal = resources.causticTemporal;
            passData.gaussianSoftLight0 = resources.gaussianSoftLight0;
            passData.gaussianSoftLight1 = resources.gaussianSoftLight1;
            passData.radianceCascade0 = resources.radianceCascade0;
            passData.radianceCascade1 = resources.radianceCascade1;
            passData.radianceCascadeSdfSeedA = resources.radianceCascadeSdfSeedA;
            passData.radianceCascadeSdfSeedB = resources.radianceCascadeSdfSeedB;
            passData.radianceCascadeSdfPayloadA = resources.radianceCascadeSdfPayloadA;
            passData.radianceCascadeSdfPayloadB = resources.radianceCascadeSdfPayloadB;
            UseIfValid(builder, passData.transport, AccessFlags.Read);
            UseIfValid(builder, passData.causticResolved, AccessFlags.Read);
            UseIfValid(builder, passData.causticTemporal, AccessFlags.Read);
            UseIfValid(builder, passData.gaussianSoftLight0, AccessFlags.ReadWrite);
            UseIfValid(builder, passData.gaussianSoftLight1, AccessFlags.ReadWrite);
            UseIfValid(builder, passData.radianceCascade0, AccessFlags.ReadWrite);
            UseIfValid(builder, passData.radianceCascade1, AccessFlags.ReadWrite);
            UseIfValid(builder, passData.radianceCascadeSdfSeedA, AccessFlags.ReadWrite);
            UseIfValid(builder, passData.radianceCascadeSdfSeedB, AccessFlags.ReadWrite);
            UseIfValid(builder, passData.radianceCascadeSdfPayloadA, AccessFlags.ReadWrite);
            UseIfValid(builder, passData.radianceCascadeSdfPayloadB, AccessFlags.ReadWrite);
            builder.AllowPassCulling(false);
            builder.SetRenderFunc(static (CausticsSoftLightPassData data, UnsafeGraphContext context) =>
            {
                CommandBuffer nativeCommandBuffer = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);
                data.lighting.RecordSoftLight(data.context, nativeCommandBuffer, data.lighting.directLight.GetSharpCausticsTexture());
            });
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

        private class MetaballAccumulationPassData
        {
            public ParticleDisplay2D display;
            public MetaballRenderer2D metaballRenderer;
            public int shaderPass;
        }

        private class CausticsComputePassData
        {
            public ParticleFluidLighting2D lighting;
            public ParticleFluidLighting2D.FrameContext context;
			public int frameIndex;
			public TextureHandle transport;
			public TextureHandle materialNormal;
			public TextureHandle gradientAtlas;
            public TextureHandle causticResolved;
        }

        private struct LightingInputHandles
		{
			public TextureHandle transport;
			public TextureHandle materialNormal;
			public TextureHandle velocity;
			public TextureHandle gradientAtlas;
		}

        private struct LightingResourceHandles
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

        private class CausticsBlurPassData
        {
            public ParticleFluidLighting2D lighting;
            public ParticleFluidLighting2D.FrameContext context;
            public TextureHandle causticResolved;
            public TextureHandle causticBlur;
        }

        private class CausticsTemporalPassData
        {
            public ParticleFluidLighting2D lighting;
            public ParticleFluidLighting2D.FrameContext context;
            public TextureHandle velocity;
            public TextureHandle causticResolved;
            public TextureHandle causticTemporal;
            public TextureHandle causticHistory;
        }

        private class CausticsSoftLightPassData
        {
            public ParticleFluidLighting2D lighting;
            public ParticleFluidLighting2D.FrameContext context;
            public TextureHandle transport;
            public TextureHandle causticResolved;
            public TextureHandle causticTemporal;
            public TextureHandle gaussianSoftLight0;
            public TextureHandle gaussianSoftLight1;
            public TextureHandle radianceCascade0;
            public TextureHandle radianceCascade1;
            public TextureHandle radianceCascadeSdfSeedA;
            public TextureHandle radianceCascadeSdfSeedB;
            public TextureHandle radianceCascadeSdfPayloadA;
            public TextureHandle radianceCascadeSdfPayloadB;
        }

        private class MaterialMapPassData
        {
            public MetaballRenderer2D metaballRenderer;
            public TextureHandle combined;
            public TextureHandle normal;
            public TextureHandle gradientAtlas;
            public TextureHandle albedo;
            public TextureHandle materialNormal;
            public TextureHandle transport;
        }

        private class BlurPassData
        {
            public RenderTexture sourceTexture;
            public RenderTexture destinationTexture;
            public Material material;
            public float blurRadius;
            public Vector2 direction;
        }

        private class VelocityBlurPassData
        {
            public MetaballRenderer2D metaballRenderer;
            public TextureHandle velocity;
            public TextureHandle velocityScratch;
        }

        private class CompositePassData
        {
            public ParticleDisplay2D display;
            public MetaballRenderer2D metaballRenderer;
            public Camera camera;
            public bool useMaterialPipeline;
            public TextureHandle color;
            public TextureHandle depth;
            public TextureHandle combined;
            public TextureHandle normal;
            public TextureHandle velocity;
            public TextureHandle materialAlbedo;
            public TextureHandle materialNormal;
            public TextureHandle materialTransport;
            public TextureHandle causticResolved;
            public TextureHandle causticTemporal;
            public TextureHandle gaussianSoftLight0;
            public TextureHandle gaussianSoftLight1;
            public TextureHandle radianceCascade0;
            public TextureHandle radianceCascade1;
        }

    }
}
