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
                                      display.mesh != null &&
                                      display.argsBuffer != null &&
                                      display.metaballs.blurShader != null;
            if (!canRenderMetaballs)
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
        readonly ImportedTexture velocity = new();
        readonly ImportedTexture velocityBlur = new();
        readonly ImportedTexture materialAlbedo = new();
        readonly ImportedTexture materialNormal = new();
        readonly ImportedTexture materialTransport = new();
        readonly ImportedTexture causticResolved = new();
        readonly ImportedTexture causticBlur = new();
        readonly ImportedTexture causticTemporal = new();
        readonly ImportedTexture causticHistory = new();
        readonly ImportedTexture gradient = new();
        readonly ImportedTexture gradient2 = new();
        readonly ImportedTexture softLight0 = new();
        readonly ImportedTexture softLight1 = new();
        readonly ImportedTexture radianceCascade0 = new();
        readonly ImportedTexture radianceCascade1 = new();
        readonly ImportedTexture radianceCascadeSdfSeedA = new();
        readonly ImportedTexture radianceCascadeSdfSeedB = new();
        readonly ImportedTexture radianceCascadeSdfPayloadA = new();
        readonly ImportedTexture radianceCascadeSdfPayloadB = new();

        public void Setup(ParticleDisplay2D display)
        {
            this.display = display;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            if (display == null)
                return;
            if (!Application.isPlaying || display.sim == null || display.sim.resources.positionBuffer == null)
                return;

            UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
            if (!resourceData.activeColorTexture.IsValid())
                return;

            if (display.renderMode == ParticleDisplay2D.RenderMode.Metaballs)
            {
                RecordMetaballRenderGraph(renderGraph, cameraData.camera, resourceData);
            }
        }

        void RecordMetaballRenderGraph(RenderGraph renderGraph, Camera camera, UniversalResourceData resourceData)
        {
            MetaballRenderer2D metaballRenderer = display.MetaballRenderer;
            if (metaballRenderer == null || !metaballRenderer.PrepareForRender(display, camera))
            {
                return;
            }

            ParticleFluidLighting2D lighting = display.ActiveLighting;
            TextureHandle combinedHandle = combinedAccumulation.Import(renderGraph, metaballRenderer.combinedAccumulationTexture, "FluidSim2D Combined Accumulation");
            TextureHandle combinedBlurHandle = combinedBlur.Import(renderGraph, metaballRenderer.combinedBlurTexture, "FluidSim2D Combined Blur");
            TextureHandle normalHandle = normalAccumulation.Import(renderGraph, metaballRenderer.normalAccumulationTexture, "FluidSim2D Normal Accumulation");
            TextureHandle normalBlurHandle = normalBlur.Import(renderGraph, metaballRenderer.normalBlurTexture, "FluidSim2D Normal Blur");
            bool renderVelocityTextures = metaballRenderer.ShouldRenderVelocityTextures(display);
            bool useMaterialPipeline = metaballRenderer.ShouldUseMaterialPipeline(display) && metaballRenderer.materialRenderer.IsReady;
            bool canRenderLighting = useMaterialPipeline && lighting != null;
            bool renderCaustics = canRenderLighting && lighting.directLight.IsCausticsEnabled;
            TextureHandle velocityHandle = renderVelocityTextures ? velocity.Import(renderGraph, metaballRenderer.velocityTexture, "FluidSim2D Velocity") : TextureHandle.nullHandle;
            TextureHandle velocityBlurHandle = renderVelocityTextures ? velocityBlur.Import(renderGraph, metaballRenderer.velocityBlurTexture, "FluidSim2D Velocity Blur") : TextureHandle.nullHandle;
            bool temporalUsesVelocity = renderCaustics
                && lighting.directLight.temporalSettings.denoisingEnabled
                && lighting.directLight.temporalSettings.temporalMotionSource == ParticleFluidCausticsTemporal.TemporalMotionSource.ParticleMotion;
            TextureHandle temporalVelocityHandle = temporalUsesVelocity
                ? (renderVelocityTextures ? velocityHandle : velocity.Import(renderGraph, Texture2D.blackTexture, "FluidSim2D Velocity Fallback"))
                : TextureHandle.nullHandle;
            TextureHandle materialAlbedoHandle = materialAlbedo.Import(renderGraph, metaballRenderer.materialRenderer.MaterialMaps.albedoTexture, "FluidSim2D Material Albedo");
            TextureHandle materialNormalHandle = materialNormal.Import(renderGraph, metaballRenderer.materialRenderer.MaterialMaps.normalTexture, "FluidSim2D Material Normal");
            TextureHandle materialTransportHandle = materialTransport.Import(renderGraph, metaballRenderer.materialRenderer.MaterialMaps.transportTexture, "FluidSim2D Material Transport");
            TextureHandle gradientHandle = gradient.Import(renderGraph, display.gradientTexture != null ? display.gradientTexture : Texture2D.blackTexture, "FluidSim2D Gradient");
            TextureHandle gradient2Handle = gradient2.Import(renderGraph, display.gradientTexture2 != null ? display.gradientTexture2 : Texture2D.blackTexture, "FluidSim2D Gradient 2");

            RecordMetaballAccumulationPass(renderGraph, "Fluid Sim 2D Combined Accumulation", display, metaballRenderer, combinedHandle, 0);
            RecordMetaballAccumulationPass(renderGraph, "Fluid Sim 2D Normal Accumulation", display, metaballRenderer, normalHandle, 1);
            if (renderVelocityTextures)
            {
                RecordMetaballAccumulationPass(renderGraph, "Fluid Sim 2D Velocity Accumulation", display, metaballRenderer, velocityHandle, 2);
            }

            RecordBlurPass(renderGraph, "Fluid Sim 2D Combined Blur Horizontal", combinedHandle, combinedBlurHandle, metaballRenderer.combinedAccumulationTexture, metaballRenderer.combinedBlurTexture, metaballRenderer.blurMaterial, new Vector2(1f, 0f));
            RecordBlurPass(renderGraph, "Fluid Sim 2D Combined Blur Vertical", combinedBlurHandle, combinedHandle, metaballRenderer.combinedBlurTexture, metaballRenderer.combinedAccumulationTexture, metaballRenderer.blurMaterial, new Vector2(0f, 1f));
            RecordBlurPass(renderGraph, "Fluid Sim 2D Normal Blur Horizontal", normalHandle, normalBlurHandle, metaballRenderer.normalAccumulationTexture, metaballRenderer.normalBlurTexture, metaballRenderer.blurMaterial, new Vector2(1f, 0f));
            RecordBlurPass(renderGraph, "Fluid Sim 2D Normal Blur Vertical", normalBlurHandle, normalHandle, metaballRenderer.normalBlurTexture, metaballRenderer.normalAccumulationTexture, metaballRenderer.blurMaterial, new Vector2(0f, 1f));

            if (useMaterialPipeline && materialAlbedoHandle.IsValid() && materialNormalHandle.IsValid() && materialTransportHandle.IsValid())
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
                    passData.transport = materialTransportHandle;
                    UseIfValid(builder, passData.combined, AccessFlags.Read);
                    UseIfValid(builder, passData.normal, AccessFlags.Read);
                    UseIfValid(builder, passData.gradient, AccessFlags.Read);
                    UseIfValid(builder, passData.gradient2, AccessFlags.Read);
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
            }

            float effectiveMotionBlurRadius = metaballRenderer.GetEffectiveMotionBlurRadius(display, camera);
            if (renderVelocityTextures && effectiveMotionBlurRadius > 0.001f)
            {
                using (var builder = renderGraph.AddUnsafePass<VelocityBlurPassData>("Fluid Sim 2D Velocity Gaussian Blur", out var passData))
                {
                    passData.metaballRenderer = metaballRenderer;
                    passData.effectiveMotionBlurRadius = effectiveMotionBlurRadius;
                    passData.velocity = velocityHandle;
                    passData.velocityScratch = velocityBlurHandle;
                    UseIfValid(builder, passData.velocity, AccessFlags.ReadWrite);
                    UseIfValid(builder, passData.velocityScratch, AccessFlags.ReadWrite);
                    builder.AllowPassCulling(false);
                    builder.SetRenderFunc(static (VelocityBlurPassData data, UnsafeGraphContext context) =>
                    {
                        CommandBuffer nativeCommandBuffer = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);
                        data.metaballRenderer.RecordVelocityGaussianBlur(nativeCommandBuffer, data.effectiveMotionBlurRadius);
                    });
                }
            }

            bool renderPhaseDiffuseLight = canRenderLighting && lighting.gaussianSss.ShouldRender();
            bool renderRadianceCascadeLight = canRenderLighting && lighting.radianceCascadeGi.isActiveAndEnabled;
            bool renderSoftLight = renderPhaseDiffuseLight || renderRadianceCascadeLight;
            LightingResourceHandles lightingResources = canRenderLighting
                ? ImportLightingResources(renderGraph, lighting, "FluidSim2D", renderCaustics, renderPhaseDiffuseLight, renderRadianceCascadeLight)
                : default;
            ParticleFluidLighting2D.FrameContext lightingContext = canRenderLighting
                ? metaballRenderer.CreateLightingContext(display, camera)
                : default;
            int causticsFrameIndex = renderCaustics ? lighting.directLight.causticFrameIndex++ : 0;
            LightingInputHandles lightingInputs = new()
            {
                transport = materialTransportHandle,
                materialNormal = materialNormalHandle,
                velocity = temporalVelocityHandle,
                gradient = gradientHandle,
                gradient2 = gradient2Handle
            };
            if (canRenderLighting)
            {
                ParticleFluidLightingInputSet lightingInputSet = metaballRenderer.materialRenderer.MaterialMaps.CreateLightingInputs(
                    lightingContext.renderRegion,
                    lightingContext.materialSize,
                    renderVelocityTextures ? metaballRenderer.velocityTexture : null);
                lighting.ApplyLightingInputs(lightingInputSet);
                Texture causticTexture = lighting.directLight.GetCurrentDirectLightTexture();
                lighting.ApplySettings(lightingContext, renderCaustics, renderSoftLight, causticTexture);
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
                passData.display = display;
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

        LightingResourceHandles ImportLightingResources(RenderGraph renderGraph, ParticleFluidLighting2D lighting, string prefix, bool renderCaustics, bool renderPhaseDiffuseLight, bool renderRadianceCascadeLight)
        {
            LightingResourceHandles handles = default;
            bool renderSoftLight = renderPhaseDiffuseLight || renderRadianceCascadeLight;
            bool renderSdfRadianceCascade = renderSoftLight && renderRadianceCascadeLight;

            handles.causticResolved = renderCaustics ? causticResolved.Import(renderGraph, lighting.directLight.causticResolvedTexture, $"{prefix} Caustic Resolved") : TextureHandle.nullHandle;
            handles.causticBlur = renderCaustics ? causticBlur.Import(renderGraph, lighting.directLight.temporalCaustics.causticBlurTexture, $"{prefix} Caustic Blur") : TextureHandle.nullHandle;
            handles.causticTemporal = renderCaustics && lighting.directLight.temporalSettings.denoisingEnabled ? causticTemporal.Import(renderGraph, lighting.directLight.temporalCaustics.causticTemporalTexture, $"{prefix} Caustic Temporal") : TextureHandle.nullHandle;
            handles.causticHistory = renderCaustics && lighting.directLight.temporalSettings.denoisingEnabled ? causticHistory.Import(renderGraph, lighting.directLight.temporalCaustics.causticHistoryTexture, $"{prefix} Caustic History") : TextureHandle.nullHandle;
            handles.gaussianSoftLight0 = renderPhaseDiffuseLight ? softLight0.Import(renderGraph, lighting.gaussianSss.gaussianSoftLightTexture0, $"{prefix} Gaussian Soft Light 0") : TextureHandle.nullHandle;
            handles.gaussianSoftLight1 = renderPhaseDiffuseLight ? softLight1.Import(renderGraph, lighting.gaussianSss.gaussianSoftLightTexture1, $"{prefix} Gaussian Soft Light 1") : TextureHandle.nullHandle;
            handles.radianceCascade0 = renderRadianceCascadeLight ? radianceCascade0.Import(renderGraph, lighting.radianceCascadeGi.radianceCascadeTexture0, $"{prefix} Radiance Cascade 0") : TextureHandle.nullHandle;
            handles.radianceCascade1 = renderRadianceCascadeLight ? radianceCascade1.Import(renderGraph, lighting.radianceCascadeGi.radianceCascadeTexture1, $"{prefix} Radiance Cascade 1") : TextureHandle.nullHandle;
            handles.radianceCascadeSdfSeedA = renderSdfRadianceCascade ? radianceCascadeSdfSeedA.Import(renderGraph, lighting.radianceCascadeGi.radianceCascadeSdfSeedA, $"{prefix} RC SDF Seed A") : TextureHandle.nullHandle;
            handles.radianceCascadeSdfSeedB = renderSdfRadianceCascade ? radianceCascadeSdfSeedB.Import(renderGraph, lighting.radianceCascadeGi.radianceCascadeSdfSeedB, $"{prefix} RC SDF Seed B") : TextureHandle.nullHandle;
            handles.radianceCascadeSdfPayloadA = renderSdfRadianceCascade ? radianceCascadeSdfPayloadA.Import(renderGraph, lighting.radianceCascadeGi.radianceCascadeSdfPayloadA, $"{prefix} RC SDF Payload A") : TextureHandle.nullHandle;
            handles.radianceCascadeSdfPayloadB = renderSdfRadianceCascade ? radianceCascadeSdfPayloadB.Import(renderGraph, lighting.radianceCascadeGi.radianceCascadeSdfPayloadB, $"{prefix} RC SDF Payload B") : TextureHandle.nullHandle;

            return handles;
        }

        static void RecordCausticsPasses(RenderGraph renderGraph, string prefix, ParticleFluidLighting2D lighting, ParticleFluidLighting2D.FrameContext context, LightingInputHandles inputs, LightingResourceHandles resources, int frameIndex)
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
				passData.gradient = inputs.gradient;
				passData.gradient2 = inputs.gradient2;
				UseIfValid(builder, passData.transport, AccessFlags.Read);
				UseIfValid(builder, passData.materialNormal, AccessFlags.Read);
				UseIfValid(builder, passData.gradient, AccessFlags.Read);
				UseIfValid(builder, passData.gradient2, AccessFlags.Read);
				builder.AllowPassCulling(false);
				builder.SetRenderFunc(static (CausticsComputePassData data, ComputeGraphContext context) =>
				{
					data.lighting.directLight.RecordComputeTrace(data.context, context.cmd, data.frameIndex, data.transport, data.materialNormal, data.gradient, data.gradient2);
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
                    data.lighting.directLight.temporalCaustics.RecordBlur(data.context, nativeCommandBuffer);
                });
            }

            using (var builder = renderGraph.AddUnsafePass<CausticsTemporalPassData>($"{prefix} Caustics Temporal", out var passData))
            {
                passData.lighting = lighting;
                passData.context = context;
                passData.transport = inputs.transport;
                passData.velocity = inputs.velocity;
                passData.causticResolved = resources.causticResolved;
                passData.causticTemporal = resources.causticTemporal;
                passData.causticHistory = resources.causticHistory;
                UseIfValid(builder, passData.causticResolved, AccessFlags.Read);
                UseIfValid(builder, passData.causticTemporal, AccessFlags.ReadWrite);
                UseIfValid(builder, passData.causticHistory, AccessFlags.ReadWrite);
                UseIfValid(builder, passData.transport, AccessFlags.Read);
                UseIfValid(builder, passData.velocity, AccessFlags.Read);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc(static (CausticsTemporalPassData data, UnsafeGraphContext context) =>
                {
                    CommandBuffer nativeCommandBuffer = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);
                    data.lighting.directLight.temporalCaustics.RecordTemporal(data.context, nativeCommandBuffer);
                });
            }
        }

        static void RecordSoftLightPass(RenderGraph renderGraph, string prefix, ParticleFluidLighting2D lighting, ParticleFluidLighting2D.FrameContext context, LightingInputHandles inputs, LightingResourceHandles resources)
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
			public int frameIndex;
			public TextureHandle transport;
			public TextureHandle materialNormal;
			public TextureHandle gradient;
            public TextureHandle gradient2;
            public TextureHandle causticResolved;
        }

        struct LightingInputHandles
		{
			public TextureHandle transport;
			public TextureHandle materialNormal;
			public TextureHandle velocity;
			public TextureHandle gradient;
			public TextureHandle gradient2;
		}

        struct LightingResourceHandles
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

        class CausticsBlurPassData
        {
            public ParticleFluidLighting2D lighting;
            public ParticleFluidLighting2D.FrameContext context;
            public TextureHandle causticResolved;
            public TextureHandle causticBlur;
        }

        class CausticsTemporalPassData
        {
            public ParticleFluidLighting2D lighting;
            public ParticleFluidLighting2D.FrameContext context;
            public TextureHandle transport;
            public TextureHandle velocity;
            public TextureHandle causticResolved;
            public TextureHandle causticTemporal;
            public TextureHandle causticHistory;
        }

        class CausticsSoftLightPassData
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

        class MaterialMapPassData
        {
            public MetaballRenderer2D metaballRenderer;
            public TextureHandle combined;
            public TextureHandle normal;
            public TextureHandle gradient;
            public TextureHandle gradient2;
            public TextureHandle albedo;
            public TextureHandle materialNormal;
            public TextureHandle transport;
        }

        class BlurPassData
        {
            public RenderTexture sourceTexture;
            public RenderTexture destinationTexture;
            public Material material;
            public Vector2 direction;
        }

        class VelocityBlurPassData
        {
            public MetaballRenderer2D metaballRenderer;
            public float effectiveMotionBlurRadius;
            public TextureHandle velocity;
            public TextureHandle velocityScratch;
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
