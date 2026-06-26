using Seb.Fluid2D.Rendering;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Seb.Fluid2D.Simulation
{
    public class FluidSim2DRendererFeature : ScriptableRendererFeature
    {
        FluidSim2DRenderPass pass;

        public override void Create()
        {
            pass = new FluidSim2DRenderPass
            {
                renderPassEvent = RenderPassEvent.AfterRenderingTransparents
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
            if (display.sim == null || display.sim.positionBuffer == null)
                return;

            bool canRenderMetaballs = display.renderMode == ParticleDisplay2D.RenderMode.Metaballs &&
                                      display.mesh != null &&
                                      display.argsBuffer != null &&
                                      display.metaballs.blurShader != null;
            bool canRenderJumpFlood = display.renderMode == ParticleDisplay2D.RenderMode.JumpFlood &&
                                      display.jumpFlood.computeShader != null &&
                                      display.jumpFlood.displayShader != null;
            if (!canRenderMetaballs && !canRenderJumpFlood)
                return;

            pass.Setup(display);
            renderer.EnqueuePass(pass);
        }
    }

    public class FluidSim2DRenderPass : ScriptableRenderPass
    {
        ParticleDisplay2D display;
        readonly ImportedTexture combinedAccumulation = new();
        readonly ImportedTexture combinedBlur = new();
        readonly ImportedTexture normalAccumulation = new();
        readonly ImportedTexture normalBlur = new();
        readonly ImportedTexture velocityPhase0Accumulation = new();
        readonly ImportedTexture velocityPhase0Blur = new();
        readonly ImportedTexture velocityPhase1Accumulation = new();
        readonly ImportedTexture velocityPhase1Blur = new();
        readonly ImportedTexture materialAlbedo = new();
        readonly ImportedTexture materialNormal = new();
        readonly ImportedTexture causticResolved = new();
        readonly ImportedTexture causticBlur = new();
        readonly ImportedTexture causticTemporal = new();
        readonly ImportedTexture causticHistory = new();
        readonly ImportedTexture causticMotion = new();
        readonly ImportedTexture causticMotionDilated = new();
        readonly ImportedTexture causticMotionDilationScratch = new();
        readonly ImportedTexture gradient = new();
        readonly ImportedTexture gradient2 = new();
        readonly ImportedTexture softLight0 = new();
        readonly ImportedTexture softLight1 = new();
        readonly ImportedTexture radianceCascadeSdfSeedA = new();
        readonly ImportedTexture radianceCascadeSdfSeedB = new();
        readonly ImportedTexture radianceCascadeSdfPayloadA = new();
        readonly ImportedTexture radianceCascadeSdfPayloadB = new();
        readonly ImportedTexture radianceCascadeSdfResolved = new();
        readonly ImportedTexture radianceCascadeSdfResolvedPayload = new();

        public void Setup(ParticleDisplay2D display)
        {
            this.display = display;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            if (display == null)
                return;
            if (!Application.isPlaying || display.sim == null || display.sim.positionBuffer == null)
                return;

            UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
            if (!resourceData.activeColorTexture.IsValid())
                return;

            if (display.renderMode == ParticleDisplay2D.RenderMode.Metaballs)
            {
                RecordMetaballRenderGraph(renderGraph, cameraData.camera, resourceData);
            }
            else if (display.renderMode == ParticleDisplay2D.RenderMode.JumpFlood)
            {
                RecordJumpFloodRenderGraph(renderGraph, cameraData.camera, resourceData);
            }
        }

        void RecordMetaballRenderGraph(RenderGraph renderGraph, Camera camera, UniversalResourceData resourceData)
        {
            MetaballRenderer2D metaballRenderer = display.MetaballRenderer;
            if (metaballRenderer == null || !metaballRenderer.PrepareForRender(display, camera))
            {
                return;
            }

            ParticleFluidLighting2D lighting = display.GetComponent<ParticleFluidLighting2D>();
            if (lighting != null && !lighting.isActiveAndEnabled)
            {
                lighting = null;
            }
            TextureHandle combinedHandle = combinedAccumulation.Import(renderGraph, metaballRenderer.combinedAccumulationTexture, "FluidSim2D Combined Accumulation");
            TextureHandle combinedBlurHandle = combinedBlur.Import(renderGraph, metaballRenderer.combinedBlurTexture, "FluidSim2D Combined Blur");
            TextureHandle normalHandle = normalAccumulation.Import(renderGraph, metaballRenderer.normalAccumulationTexture, "FluidSim2D Normal Accumulation");
            TextureHandle normalBlurHandle = normalBlur.Import(renderGraph, metaballRenderer.normalBlurTexture, "FluidSim2D Normal Blur");
            bool renderCaustics = lighting != null && lighting.ShouldRenderCaustics();
            bool renderVelocityTextures = metaballRenderer.ShouldRenderVelocityTextures(display);
            TextureHandle velocity0Handle = renderVelocityTextures ? velocityPhase0Accumulation.Import(renderGraph, metaballRenderer.velocityPhase0AccumulationTexture, "FluidSim2D Velocity Phase 0") : TextureHandle.nullHandle;
            TextureHandle velocity0BlurHandle = renderVelocityTextures ? velocityPhase0Blur.Import(renderGraph, metaballRenderer.velocityPhase0BlurTexture, "FluidSim2D Velocity Phase 0 Blur") : TextureHandle.nullHandle;
            TextureHandle velocity1Handle = renderVelocityTextures ? velocityPhase1Accumulation.Import(renderGraph, metaballRenderer.velocityPhase1AccumulationTexture, "FluidSim2D Velocity Phase 1") : TextureHandle.nullHandle;
            TextureHandle velocity1BlurHandle = renderVelocityTextures ? velocityPhase1Blur.Import(renderGraph, metaballRenderer.velocityPhase1BlurTexture, "FluidSim2D Velocity Phase 1 Blur") : TextureHandle.nullHandle;
            TextureHandle velocity0TraceHandle = renderCaustics
                ? (renderVelocityTextures ? velocity0Handle : velocityPhase0Accumulation.Import(renderGraph, Texture2D.blackTexture, "FluidSim2D Velocity Phase 0 Fallback"))
                : TextureHandle.nullHandle;
            TextureHandle velocity1TraceHandle = renderCaustics
                ? (renderVelocityTextures ? velocity1Handle : velocityPhase1Accumulation.Import(renderGraph, Texture2D.blackTexture, "FluidSim2D Velocity Phase 1 Fallback"))
                : TextureHandle.nullHandle;
            TextureHandle materialAlbedoHandle = materialAlbedo.Import(renderGraph, metaballRenderer.materialRenderer.MaterialMaps.AlbedoRenderTexture, "FluidSim2D Material Albedo");
            TextureHandle materialNormalHandle = materialNormal.Import(renderGraph, metaballRenderer.materialRenderer.MaterialMaps.NormalRenderTexture, "FluidSim2D Material Normal");

            RecordMetaballAccumulationPass(renderGraph, "Fluid Sim 2D Combined Accumulation", display, metaballRenderer, combinedHandle, 0);
            RecordMetaballAccumulationPass(renderGraph, "Fluid Sim 2D Normal Accumulation", display, metaballRenderer, normalHandle, 1);
            if (renderVelocityTextures)
            {
                RecordMetaballAccumulationPass(renderGraph, "Fluid Sim 2D Velocity Phase 0 Accumulation", display, metaballRenderer, velocity0Handle, 2);
                RecordMetaballAccumulationPass(renderGraph, "Fluid Sim 2D Velocity Phase 1 Accumulation", display, metaballRenderer, velocity1Handle, 3);
            }

            RecordBlurPass(renderGraph, "Fluid Sim 2D Combined Blur Horizontal", combinedHandle, combinedBlurHandle, metaballRenderer.combinedAccumulationTexture, metaballRenderer.combinedBlurTexture, metaballRenderer.blurMaterial, new Vector2(1f, 0f));
            RecordBlurPass(renderGraph, "Fluid Sim 2D Combined Blur Vertical", combinedBlurHandle, combinedHandle, metaballRenderer.combinedBlurTexture, metaballRenderer.combinedAccumulationTexture, metaballRenderer.blurMaterial, new Vector2(0f, 1f));
            RecordBlurPass(renderGraph, "Fluid Sim 2D Normal Blur Horizontal", normalHandle, normalBlurHandle, metaballRenderer.normalAccumulationTexture, metaballRenderer.normalBlurTexture, metaballRenderer.blurMaterial, new Vector2(1f, 0f));
            RecordBlurPass(renderGraph, "Fluid Sim 2D Normal Blur Vertical", normalBlurHandle, normalHandle, metaballRenderer.normalBlurTexture, metaballRenderer.normalAccumulationTexture, metaballRenderer.blurMaterial, new Vector2(0f, 1f));

            float effectiveMotionBlurRadius = metaballRenderer.GetEffectiveMotionBlurRadius(display, camera);
            if (renderVelocityTextures && effectiveMotionBlurRadius > 0.001f)
            {
                using (var builder = renderGraph.AddUnsafePass<MotionPyramidPassData>("Fluid Sim 2D Velocity Motion Pyramid", out var passData))
                {
                    passData.metaballRenderer = metaballRenderer;
                    passData.effectiveMotionBlurRadius = effectiveMotionBlurRadius;
                    passData.velocity0 = velocity0Handle;
                    passData.velocity0Scratch = velocity0BlurHandle;
                    passData.velocity1 = velocity1Handle;
                    passData.velocity1Scratch = velocity1BlurHandle;
                    UseIfValid(builder, passData.velocity0, AccessFlags.ReadWrite);
                    UseIfValid(builder, passData.velocity0Scratch, AccessFlags.ReadWrite);
                    UseIfValid(builder, passData.velocity1, AccessFlags.ReadWrite);
                    UseIfValid(builder, passData.velocity1Scratch, AccessFlags.ReadWrite);
                    builder.AllowPassCulling(false);
                    builder.SetRenderFunc(static (MotionPyramidPassData data, UnsafeGraphContext context) =>
                    {
                        CommandBuffer nativeCommandBuffer = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);
                        data.metaballRenderer.RecordMotionPyramid(nativeCommandBuffer, data.effectiveMotionBlurRadius);
                    });
                }
            }

            TextureHandle causticResolvedHandle = renderCaustics ? causticResolved.Import(renderGraph, lighting.causticResolvedTexture, "FluidSim2D Caustic Resolved") : TextureHandle.nullHandle;
            TextureHandle causticBlurHandle = renderCaustics ? causticBlur.Import(renderGraph, lighting.causticBlurTexture, "FluidSim2D Caustic Blur") : TextureHandle.nullHandle;
            TextureHandle causticTemporalHandle = renderCaustics && lighting.denoisingEnabled ? causticTemporal.Import(renderGraph, lighting.causticTemporalTexture, "FluidSim2D Caustic Temporal") : TextureHandle.nullHandle;
            TextureHandle causticHistoryHandle = renderCaustics && lighting.denoisingEnabled ? causticHistory.Import(renderGraph, lighting.causticHistoryTexture, "FluidSim2D Caustic History") : TextureHandle.nullHandle;
            TextureHandle causticMotionHandle = renderCaustics ? causticMotion.Import(renderGraph, lighting.causticMotionTexture, "FluidSim2D Caustic Motion") : TextureHandle.nullHandle;
            TextureHandle causticMotionDilatedHandle = renderCaustics ? causticMotionDilated.Import(renderGraph, lighting.causticMotionDilatedTexture, "FluidSim2D Caustic Motion Dilated") : TextureHandle.nullHandle;
            TextureHandle causticMotionDilationScratchHandle = renderCaustics ? causticMotionDilationScratch.Import(renderGraph, lighting.causticMotionDilationScratchTexture, "FluidSim2D Caustic Motion Dilation Scratch") : TextureHandle.nullHandle;
            TextureHandle gradientHandle = gradient.Import(renderGraph, display.gradientTexture != null ? display.gradientTexture : Texture2D.blackTexture, "FluidSim2D Gradient");
            TextureHandle gradient2Handle = gradient2.Import(renderGraph, display.gradientTexture2 != null ? display.gradientTexture2 : Texture2D.blackTexture, "FluidSim2D Gradient 2");
            bool renderSoftLight = lighting.ShouldRenderPhaseDiffuseLight() || lighting.ShouldRenderRadianceCascadeLight();
            TextureHandle softLight0Handle = renderSoftLight ? softLight0.Import(renderGraph, lighting.softLightTexture0, "FluidSim2D Soft Light 0") : TextureHandle.nullHandle;
            TextureHandle softLight1Handle = renderSoftLight ? softLight1.Import(renderGraph, lighting.softLightTexture1, "FluidSim2D Soft Light 1") : TextureHandle.nullHandle;
            bool renderSdfRadianceCascade = renderSoftLight && lighting.UsesRadianceCascadeSdfField();
            TextureHandle radianceCascadeSdfSeedAHandle = renderSdfRadianceCascade ? radianceCascadeSdfSeedA.Import(renderGraph, lighting.radianceCascadeSdfSeedA, "FluidSim2D RC SDF Seed A") : TextureHandle.nullHandle;
            TextureHandle radianceCascadeSdfSeedBHandle = renderSdfRadianceCascade ? radianceCascadeSdfSeedB.Import(renderGraph, lighting.radianceCascadeSdfSeedB, "FluidSim2D RC SDF Seed B") : TextureHandle.nullHandle;
            TextureHandle radianceCascadeSdfPayloadAHandle = renderSdfRadianceCascade ? radianceCascadeSdfPayloadA.Import(renderGraph, lighting.radianceCascadeSdfPayloadA, "FluidSim2D RC SDF Payload A") : TextureHandle.nullHandle;
            TextureHandle radianceCascadeSdfPayloadBHandle = renderSdfRadianceCascade ? radianceCascadeSdfPayloadB.Import(renderGraph, lighting.radianceCascadeSdfPayloadB, "FluidSim2D RC SDF Payload B") : TextureHandle.nullHandle;
            TextureHandle radianceCascadeSdfResolvedHandle = renderSdfRadianceCascade ? radianceCascadeSdfResolved.Import(renderGraph, lighting.radianceCascadeSdfNormalA, "FluidSim2D RC SDF Resolved") : TextureHandle.nullHandle;
            TextureHandle radianceCascadeSdfResolvedPayloadHandle = renderSdfRadianceCascade ? radianceCascadeSdfResolvedPayload.Import(renderGraph, lighting.radianceCascadeSdfNormalB, "FluidSim2D RC SDF Resolved Payload") : TextureHandle.nullHandle;
            ParticleFluidLighting2D.FrameContext lightingContext = metaballRenderer.CreateLightingContext(display, camera);
            int causticsFrameIndex = renderCaustics ? lighting.ReserveCausticsFrameIndex() : 0;
            bool useMaterialPipeline = metaballRenderer.UsesMaterialPipeline(display) && metaballRenderer.materialRenderer.IsReady;

            if (renderCaustics)
            {
                using (var builder = renderGraph.AddComputePass<CausticsComputePassData>("Fluid Sim 2D Caustics Clear", out var passData))
                {
                    passData.lighting = lighting;
                    passData.context = lightingContext;
                    passData.combinedTexture = metaballRenderer.combinedAccumulationTexture;
                    passData.frameIndex = causticsFrameIndex;
                    passData.causticResolved = causticResolvedHandle;
                    passData.causticMotion = causticMotionHandle;
                    UseIfValid(builder, passData.causticResolved, AccessFlags.Write);
                    UseIfValid(builder, passData.causticMotion, AccessFlags.Write);
                    builder.AllowPassCulling(false);
                    builder.SetRenderFunc(static (CausticsComputePassData data, ComputeGraphContext context) =>
                    {
                        data.lighting.traceCaustics.RecordComputeClear(data.context, context.cmd, data.combinedTexture, data.frameIndex, data.causticResolved, data.causticMotion);
                    });
                }

                using (var builder = renderGraph.AddComputePass<CausticsComputePassData>("Fluid Sim 2D Caustics Trace", out var passData))
                {
                    passData.lighting = lighting;
                    passData.context = lightingContext;
                    passData.combinedTexture = metaballRenderer.combinedAccumulationTexture;
                    passData.frameIndex = causticsFrameIndex;
                    passData.combined = combinedHandle;
                    passData.velocity0 = velocity0TraceHandle;
                    passData.velocity1 = velocity1TraceHandle;
                    passData.gradient = gradientHandle;
                    passData.gradient2 = gradient2Handle;
                    UseIfValid(builder, passData.combined, AccessFlags.Read);
                    UseIfValid(builder, passData.velocity0, AccessFlags.Read);
                    UseIfValid(builder, passData.velocity1, AccessFlags.Read);
                    UseIfValid(builder, passData.gradient, AccessFlags.Read);
                    UseIfValid(builder, passData.gradient2, AccessFlags.Read);
                    builder.AllowPassCulling(false);
                    builder.SetRenderFunc(static (CausticsComputePassData data, ComputeGraphContext context) =>
                    {
                        data.lighting.traceCaustics.RecordComputeTrace(data.context, context.cmd, data.combinedTexture, data.frameIndex, data.combined, data.velocity0, data.velocity1, data.gradient, data.gradient2);
                    });
                }

                using (var builder = renderGraph.AddComputePass<CausticsComputePassData>("Fluid Sim 2D Caustics Resolve", out var passData))
                {
                    passData.lighting = lighting;
                    passData.context = lightingContext;
                    passData.combinedTexture = metaballRenderer.combinedAccumulationTexture;
                    passData.frameIndex = causticsFrameIndex;
                    passData.causticResolved = causticResolvedHandle;
                    passData.causticMotion = causticMotionHandle;
                    UseIfValid(builder, passData.causticResolved, AccessFlags.Write);
                    UseIfValid(builder, passData.causticMotion, AccessFlags.Write);
                    builder.AllowPassCulling(false);
                    builder.SetRenderFunc(static (CausticsComputePassData data, ComputeGraphContext context) =>
                    {
                        data.lighting.traceCaustics.RecordComputeResolve(data.context, context.cmd, data.combinedTexture, data.frameIndex, data.causticResolved, data.causticMotion);
                    });
                }

                using (var builder = renderGraph.AddUnsafePass<CausticsBlurPassData>("Fluid Sim 2D Caustics Blur", out var passData))
                {
                    passData.lighting = lighting;
                    passData.context = lightingContext;
                    passData.causticResolved = causticResolvedHandle;
                    passData.causticBlur = causticBlurHandle;
                    passData.causticMotion = causticMotionHandle;
                    passData.causticMotionDilated = causticMotionDilatedHandle;
                    passData.causticMotionDilationScratch = causticMotionDilationScratchHandle;
                    UseIfValid(builder, passData.causticResolved, AccessFlags.ReadWrite);
                    UseIfValid(builder, passData.causticMotion, AccessFlags.ReadWrite);
                    UseIfValid(builder, passData.causticBlur, AccessFlags.Write);
                    UseIfValid(builder, passData.causticMotionDilated, AccessFlags.ReadWrite);
                    UseIfValid(builder, passData.causticMotionDilationScratch, AccessFlags.ReadWrite);
                    builder.AllowPassCulling(false);
                    builder.SetRenderFunc(static (CausticsBlurPassData data, UnsafeGraphContext context) =>
                    {
                        CommandBuffer nativeCommandBuffer = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);
                        data.lighting.temporalCaustics.RecordBlurAndMotion(data.context, nativeCommandBuffer);
                    });
                }

                using (var builder = renderGraph.AddUnsafePass<CausticsTemporalPassData>("Fluid Sim 2D Caustics Temporal", out var passData))
                {
                    passData.lighting = lighting;
                    passData.context = lightingContext;
                    passData.causticResolved = causticResolvedHandle;
                    passData.causticTemporal = causticTemporalHandle;
                    passData.causticHistory = causticHistoryHandle;
                    passData.causticMotion = causticMotionHandle;
                    passData.causticMotionDilated = causticMotionDilatedHandle;
                    passData.causticMotionDilationScratch = causticMotionDilationScratchHandle;
                    UseIfValid(builder, passData.causticResolved, AccessFlags.Read);
                    UseIfValid(builder, passData.causticTemporal, AccessFlags.ReadWrite);
                    UseIfValid(builder, passData.causticHistory, AccessFlags.ReadWrite);
                    UseIfValid(builder, passData.causticMotion, AccessFlags.Read);
                    UseIfValid(builder, passData.causticMotionDilated, AccessFlags.Read);
                    UseIfValid(builder, passData.causticMotionDilationScratch, AccessFlags.Read);
                    builder.AllowPassCulling(false);
                    builder.SetRenderFunc(static (CausticsTemporalPassData data, UnsafeGraphContext context) =>
                    {
                        CommandBuffer nativeCommandBuffer = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);
                        data.lighting.temporalCaustics.RecordTemporal(data.context, nativeCommandBuffer, data.lighting.temporalCaustics.GetTemporalMotionTextureAfterBlur());
                    });
                }

            }

            if (renderSoftLight)
            {
                using (var builder = renderGraph.AddUnsafePass<CausticsSoftLightPassData>("Fluid Sim 2D Caustics Soft Light", out var passData))
                {
                    passData.lighting = lighting;
                    passData.context = lightingContext;
                    passData.combinedTexture = metaballRenderer.combinedAccumulationTexture;
                    passData.combined = combinedHandle;
                    passData.causticResolved = causticResolvedHandle;
                    passData.causticTemporal = causticTemporalHandle;
                    passData.softLight0 = softLight0Handle;
                    passData.softLight1 = softLight1Handle;
                    passData.radianceCascadeSdfSeedA = radianceCascadeSdfSeedAHandle;
                    passData.radianceCascadeSdfSeedB = radianceCascadeSdfSeedBHandle;
                    passData.radianceCascadeSdfPayloadA = radianceCascadeSdfPayloadAHandle;
                    passData.radianceCascadeSdfPayloadB = radianceCascadeSdfPayloadBHandle;
                    passData.radianceCascadeSdfResolved = radianceCascadeSdfResolvedHandle;
                    passData.radianceCascadeSdfResolvedPayload = radianceCascadeSdfResolvedPayloadHandle;
                    UseIfValid(builder, passData.combined, AccessFlags.Read);
                    UseIfValid(builder, passData.causticResolved, AccessFlags.Read);
                    UseIfValid(builder, passData.causticTemporal, AccessFlags.Read);
                    UseIfValid(builder, passData.softLight0, AccessFlags.ReadWrite);
                    UseIfValid(builder, passData.softLight1, AccessFlags.ReadWrite);
                    UseIfValid(builder, passData.radianceCascadeSdfSeedA, AccessFlags.ReadWrite);
                    UseIfValid(builder, passData.radianceCascadeSdfSeedB, AccessFlags.ReadWrite);
                    UseIfValid(builder, passData.radianceCascadeSdfPayloadA, AccessFlags.ReadWrite);
                    UseIfValid(builder, passData.radianceCascadeSdfPayloadB, AccessFlags.ReadWrite);
                    UseIfValid(builder, passData.radianceCascadeSdfResolved, AccessFlags.ReadWrite);
                    UseIfValid(builder, passData.radianceCascadeSdfResolvedPayload, AccessFlags.ReadWrite);
                    builder.AllowPassCulling(false);
                    builder.SetRenderFunc(static (CausticsSoftLightPassData data, UnsafeGraphContext context) =>
                    {
                        CommandBuffer nativeCommandBuffer = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);
                        data.lighting.softLightCaustics.RecordSoftLight(data.context, nativeCommandBuffer, data.lighting.GetSharpCausticsTexture(), data.combinedTexture);
                    });
                }
            }

            if (useMaterialPipeline && materialAlbedoHandle.IsValid() && materialNormalHandle.IsValid())
            {
                using (var builder = renderGraph.AddUnsafePass<MaterialMapPassData>("Fluid Sim 2D Material Maps", out var passData))
                {
                    passData.metaballRenderer = metaballRenderer;
                    passData.combined = combinedHandle;
                    passData.normal = normalHandle;
                    passData.gradient = gradientHandle;
                    passData.gradient2 = gradient2Handle;
                    passData.albedo = materialAlbedoHandle;
                    passData.materialNormal = materialNormalHandle;
                    UseIfValid(builder, passData.combined, AccessFlags.Read);
                    UseIfValid(builder, passData.normal, AccessFlags.Read);
                    UseIfValid(builder, passData.gradient, AccessFlags.Read);
                    UseIfValid(builder, passData.gradient2, AccessFlags.Read);
                    UseIfValid(builder, passData.albedo, AccessFlags.Write);
                    UseIfValid(builder, passData.materialNormal, AccessFlags.Write);
                    builder.AllowPassCulling(false);
                    builder.SetRenderFunc(static (MaterialMapPassData data, UnsafeGraphContext context) =>
                    {
                        CommandBuffer nativeCommandBuffer = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);
                        data.metaballRenderer.RecordMaterialMaps(nativeCommandBuffer);
                    });
                }
            }

            using (var builder = renderGraph.AddUnsafePass<CompositePassData>("Fluid Sim 2D Composite", out var passData))
            {
                passData.display = display;
                passData.metaballRenderer = metaballRenderer;
                passData.camera = camera;
                passData.useMaterialPipeline = useMaterialPipeline;
                passData.color = resourceData.activeColorTexture;
                passData.depth = resourceData.activeDepthTexture;
                passData.combined = combinedHandle;
                passData.normal = normalHandle;
                passData.velocity0 = velocity0Handle;
                passData.velocity1 = velocity1Handle;
                passData.materialAlbedo = materialAlbedoHandle;
                passData.materialNormal = materialNormalHandle;
                passData.causticResolved = causticResolvedHandle;
                passData.causticTemporal = causticTemporalHandle;
                passData.causticMotion = causticMotionHandle;
                passData.softLight0 = softLight0Handle;
                passData.softLight1 = softLight1Handle;
                UseIfValid(builder, passData.color, AccessFlags.Write);
                UseIfValid(builder, passData.depth, AccessFlags.ReadWrite);
                UseIfValid(builder, passData.combined, AccessFlags.Read);
                UseIfValid(builder, passData.normal, AccessFlags.Read);
                UseIfValid(builder, passData.velocity0, AccessFlags.Read);
                UseIfValid(builder, passData.velocity1, AccessFlags.Read);
                UseIfValid(builder, passData.materialAlbedo, AccessFlags.Read);
                UseIfValid(builder, passData.materialNormal, AccessFlags.Read);
                UseIfValid(builder, passData.causticResolved, AccessFlags.Read);
                UseIfValid(builder, passData.causticTemporal, AccessFlags.Read);
                UseIfValid(builder, passData.causticMotion, AccessFlags.Read);
                UseIfValid(builder, passData.softLight0, AccessFlags.Read);
                UseIfValid(builder, passData.softLight1, AccessFlags.Read);
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

        void RecordJumpFloodRenderGraph(RenderGraph renderGraph, Camera camera, UniversalResourceData resourceData)
        {
            if (!display.JumpFloodRenderer.PrepareForRender(display, camera))
            {
                return;
            }

            using var builder = renderGraph.AddUnsafePass<JumpFloodPassData>("Fluid Sim 2D", out var passData);

            passData.display = display;
            passData.jumpFloodRenderer = display.JumpFloodRenderer;
            passData.camera = camera;
            passData.color = resourceData.activeColorTexture;
            passData.depth = resourceData.activeDepthTexture;
            UseIfValid(builder, passData.color, AccessFlags.Write);
            UseIfValid(builder, passData.depth, AccessFlags.ReadWrite);
            builder.AllowPassCulling(false);
            builder.SetRenderFunc(static (JumpFloodPassData data, UnsafeGraphContext context) =>
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
                data.jumpFloodRenderer.RecordComposite(data.display, data.camera, nativeCommandBuffer, data.color);
            });
        }

        static void RecordMetaballAccumulationPass(RenderGraph renderGraph, string passName, ParticleDisplay2D display, MetaballRenderer2D metaballRenderer, TextureHandle target, int shaderPass)
        {
            using var builder = renderGraph.AddRasterRenderPass<MetaballAccumulationPassData>(passName, out var passData);

            passData.display = display;
            passData.metaballRenderer = metaballRenderer;
            passData.shaderPass = shaderPass;
            passData.target = target;
            builder.SetRenderAttachment(target, 0, AccessFlags.Write);
            builder.AllowPassCulling(false);
            builder.SetRenderFunc(static (MetaballAccumulationPassData data, RasterGraphContext context) =>
            {
                data.metaballRenderer.RecordAccumulationTarget(data.display, context.cmd, data.shaderPass);
            });
        }

        static void RecordBlurPass(RenderGraph renderGraph, string passName, TextureHandle source, TextureHandle destination, RenderTexture sourceTexture, RenderTexture destinationTexture, Material material, Vector2 direction)
        {
            if (!source.IsValid() || !destination.IsValid() || sourceTexture == null || destinationTexture == null || material == null)
            {
                return;
            }

            using var builder = renderGraph.AddUnsafePass<BlurPassData>(passName, out var passData);

            passData.source = source;
            passData.destination = destination;
            passData.sourceTexture = sourceTexture;
            passData.destinationTexture = destinationTexture;
            passData.material = material;
            passData.direction = direction;
            builder.UseTexture(source, AccessFlags.Read);
            builder.UseTexture(destination, AccessFlags.Write);
            builder.AllowPassCulling(false);
            builder.SetRenderFunc(static (BlurPassData data, UnsafeGraphContext context) =>
            {
                CommandBuffer nativeCommandBuffer = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);
                nativeCommandBuffer.SetGlobalVector("blurDirection", data.direction);
                nativeCommandBuffer.Blit(data.sourceTexture, data.destinationTexture, data.material);
            });
        }

        static void UseIfValid(IBaseRenderGraphBuilder builder, TextureHandle texture, AccessFlags accessFlags)
        {
            if (texture.IsValid())
            {
                builder.UseTexture(texture, accessFlags);
            }
        }

        sealed class ImportedTexture
        {
            RTHandle handle;
            RenderTargetInfo info;
            Texture sourceTexture;

            public TextureHandle Import(RenderGraph renderGraph, RenderTexture texture, string name)
            {
                if (texture == null)
                {
                    return TextureHandle.nullHandle;
                }

                if (handle == null || sourceTexture != texture)
                {
                    handle?.Release();
                    handle = RTHandles.Alloc(texture, name);
                    sourceTexture = texture;
                }

                info = new RenderTargetInfo
                {
                    format = texture.graphicsFormat,
                    width = texture.width,
                    height = texture.height,
                    volumeDepth = texture.volumeDepth,
                    msaaSamples = 1,
                    bindMS = texture.bindTextureMS
                };
                return renderGraph.ImportTexture(handle, info);
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

                if (handle == null || sourceTexture != texture)
                {
                    handle?.Release();
                    handle = RTHandles.Alloc(texture);
                    sourceTexture = texture;
                }

                return renderGraph.ImportTexture(handle);
            }
        }

        class MetaballAccumulationPassData
        {
            public ParticleDisplay2D display;
            public MetaballRenderer2D metaballRenderer;
            public TextureHandle target;
            public int shaderPass;
        }

        class CausticsComputePassData
        {
            public ParticleFluidLighting2D lighting;
            public ParticleFluidLighting2D.FrameContext context;
            public RenderTexture combinedTexture;
            public int frameIndex;
            public TextureHandle combined;
            public TextureHandle velocity0;
            public TextureHandle velocity1;
            public TextureHandle gradient;
            public TextureHandle gradient2;
            public TextureHandle causticResolved;
            public TextureHandle causticMotion;
        }

        class CausticsBlurPassData
        {
            public ParticleFluidLighting2D lighting;
            public ParticleFluidLighting2D.FrameContext context;
            public TextureHandle causticResolved;
            public TextureHandle causticBlur;
            public TextureHandle causticMotion;
            public TextureHandle causticMotionDilated;
            public TextureHandle causticMotionDilationScratch;
        }

        class CausticsTemporalPassData
        {
            public ParticleFluidLighting2D lighting;
            public ParticleFluidLighting2D.FrameContext context;
            public TextureHandle causticResolved;
            public TextureHandle causticTemporal;
            public TextureHandle causticHistory;
            public TextureHandle causticMotion;
            public TextureHandle causticMotionDilated;
            public TextureHandle causticMotionDilationScratch;
        }

        class CausticsSoftLightPassData
        {
            public ParticleFluidLighting2D lighting;
            public ParticleFluidLighting2D.FrameContext context;
            public RenderTexture combinedTexture;
            public TextureHandle combined;
            public TextureHandle causticResolved;
            public TextureHandle causticTemporal;
            public TextureHandle softLight0;
            public TextureHandle softLight1;
            public TextureHandle radianceCascadeSdfSeedA;
            public TextureHandle radianceCascadeSdfSeedB;
            public TextureHandle radianceCascadeSdfPayloadA;
            public TextureHandle radianceCascadeSdfPayloadB;
            public TextureHandle radianceCascadeSdfResolved;
            public TextureHandle radianceCascadeSdfResolvedPayload;
        }

        class MaterialMapPassData
        {
            public MetaballRenderer2D metaballRenderer;
            public TextureHandle combined;
            public TextureHandle normal;
            public TextureHandle gradient;
            public TextureHandle gradient2;
            public TextureHandle albedo;
            public TextureHandle materialNormal;
        }

        class BlurPassData
        {
            public TextureHandle source;
            public TextureHandle destination;
            public RenderTexture sourceTexture;
            public RenderTexture destinationTexture;
            public Material material;
            public Vector2 direction;
        }

        class MotionPyramidPassData
        {
            public MetaballRenderer2D metaballRenderer;
            public float effectiveMotionBlurRadius;
            public TextureHandle velocity0;
            public TextureHandle velocity0Scratch;
            public TextureHandle velocity1;
            public TextureHandle velocity1Scratch;
        }

        class CompositePassData
        {
            public ParticleDisplay2D display;
            public MetaballRenderer2D metaballRenderer;
            public Camera camera;
            public bool useMaterialPipeline;
            public TextureHandle color;
            public TextureHandle depth;
            public TextureHandle combined;
            public TextureHandle normal;
            public TextureHandle velocity0;
            public TextureHandle velocity1;
            public TextureHandle materialAlbedo;
            public TextureHandle materialNormal;
            public TextureHandle causticResolved;
            public TextureHandle causticTemporal;
            public TextureHandle causticMotion;
            public TextureHandle softLight0;
            public TextureHandle softLight1;
        }

        class JumpFloodPassData
        {
            public ParticleDisplay2D display;
            public JumpFloodRenderer2D jumpFloodRenderer;
            public Camera camera;
            public TextureHandle color;
            public TextureHandle depth;
        }
    }
}
