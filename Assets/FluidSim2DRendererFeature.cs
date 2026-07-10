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
        readonly ImportedTexture velocity = new();
        readonly ImportedTexture velocityBlur = new();
        readonly ImportedTexture materialAlbedo = new();
        readonly ImportedTexture materialNormal = new();
        readonly ImportedTexture materialTransport = new();
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
        readonly ImportedTexture radianceCascade0 = new();
        readonly ImportedTexture radianceCascade1 = new();
        readonly ImportedTexture radianceCascadeSdfSeedA = new();
        readonly ImportedTexture radianceCascadeSdfSeedB = new();
        readonly ImportedTexture radianceCascadeSdfPayloadA = new();
        readonly ImportedTexture radianceCascadeSdfPayloadB = new();
        readonly ImportedTexture jumpFloodSeedA = new();
        readonly ImportedTexture jumpFloodSeedB = new();
        readonly ImportedTexture jumpFloodPayloadA = new();
        readonly ImportedTexture jumpFloodPayloadB = new();
        readonly ImportedTexture jumpFloodNormalPayloadA = new();
        readonly ImportedTexture jumpFloodNormalPayloadB = new();
        readonly ImportedTexture debugHeatMap = new();
        readonly ImportedTexture debugSignedHeatMap = new();

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

            ParticleFluidLighting2D lighting = display.ActiveLighting;
            TextureHandle combinedHandle = combinedAccumulation.Import(renderGraph, metaballRenderer.combinedAccumulationTexture, "FluidSim2D Combined Accumulation");
            TextureHandle combinedBlurHandle = combinedBlur.Import(renderGraph, metaballRenderer.combinedBlurTexture, "FluidSim2D Combined Blur");
            TextureHandle normalHandle = normalAccumulation.Import(renderGraph, metaballRenderer.normalAccumulationTexture, "FluidSim2D Normal Accumulation");
            TextureHandle normalBlurHandle = normalBlur.Import(renderGraph, metaballRenderer.normalBlurTexture, "FluidSim2D Normal Blur");
            bool renderCaustics = lighting != null && lighting.directLight.lightingMode == ParticleFluidDirectLight.LightingMode.Caustics;
            bool renderVelocityTextures = metaballRenderer.ShouldRenderVelocityTextures(display);
            TextureHandle velocityHandle = renderVelocityTextures ? velocity.Import(renderGraph, metaballRenderer.velocityTexture, "FluidSim2D Velocity") : TextureHandle.nullHandle;
            TextureHandle velocityBlurHandle = renderVelocityTextures ? velocityBlur.Import(renderGraph, metaballRenderer.velocityBlurTexture, "FluidSim2D Velocity Blur") : TextureHandle.nullHandle;
            TextureHandle velocityTraceHandle = renderCaustics
                ? (renderVelocityTextures ? velocityHandle : velocity.Import(renderGraph, Texture2D.blackTexture, "FluidSim2D Velocity Fallback"))
                : TextureHandle.nullHandle;
            TextureHandle materialAlbedoHandle = materialAlbedo.Import(renderGraph, metaballRenderer.materialRenderer.MaterialMaps.albedoTexture, "FluidSim2D Material Albedo");
            TextureHandle materialNormalHandle = materialNormal.Import(renderGraph, metaballRenderer.materialRenderer.MaterialMaps.normalTexture, "FluidSim2D Material Normal");
            TextureHandle materialTransportHandle = materialTransport.Import(renderGraph, metaballRenderer.materialRenderer.MaterialMaps.transportTexture, "FluidSim2D Material Transport");
            TextureHandle gradientHandle = gradient.Import(renderGraph, display.gradientTexture != null ? display.gradientTexture : Texture2D.blackTexture, "FluidSim2D Gradient");
            TextureHandle gradient2Handle = gradient2.Import(renderGraph, display.gradientTexture2 != null ? display.gradientTexture2 : Texture2D.blackTexture, "FluidSim2D Gradient 2");
            bool useMaterialPipeline = metaballRenderer.ShouldUseMaterialPipeline(display) && metaballRenderer.materialRenderer.IsReady;

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

            if (materialTransportHandle.IsValid())
            {
                using (var builder = renderGraph.AddUnsafePass<TransportMapPassData>("Fluid Sim 2D Transport Map", out var passData))
                {
                    passData.metaballRenderer = metaballRenderer;
                    passData.combined = combinedHandle;
                    passData.normal = normalHandle;
                    passData.gradient = gradientHandle;
                    passData.gradient2 = gradient2Handle;
                    passData.transport = materialTransportHandle;
                    UseIfValid(builder, passData.combined, AccessFlags.Read);
                    UseIfValid(builder, passData.normal, AccessFlags.Read);
                    UseIfValid(builder, passData.gradient, AccessFlags.Read);
                    UseIfValid(builder, passData.gradient2, AccessFlags.Read);
                    UseIfValid(builder, passData.transport, AccessFlags.Write);
                    builder.AllowPassCulling(false);
                    builder.SetRenderFunc(static (TransportMapPassData data, UnsafeGraphContext context) =>
                    {
                        CommandBuffer nativeCommandBuffer = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);
                        data.metaballRenderer.RecordMaterialMaps(nativeCommandBuffer);
                    });
                }
            }

            float effectiveMotionBlurRadius = metaballRenderer.GetEffectiveMotionBlurRadius(display, camera);
            if (renderVelocityTextures && effectiveMotionBlurRadius > 0.001f)
            {
                using (var builder = renderGraph.AddUnsafePass<MotionPyramidPassData>("Fluid Sim 2D Velocity Motion Pyramid", out var passData))
                {
                    passData.metaballRenderer = metaballRenderer;
                    passData.effectiveMotionBlurRadius = effectiveMotionBlurRadius;
                    passData.velocity = velocityHandle;
                    passData.velocityScratch = velocityBlurHandle;
                    UseIfValid(builder, passData.velocity, AccessFlags.ReadWrite);
                    UseIfValid(builder, passData.velocityScratch, AccessFlags.ReadWrite);
                    builder.AllowPassCulling(false);
                    builder.SetRenderFunc(static (MotionPyramidPassData data, UnsafeGraphContext context) =>
                    {
                        CommandBuffer nativeCommandBuffer = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);
                        data.metaballRenderer.RecordMotionPyramid(nativeCommandBuffer, data.effectiveMotionBlurRadius);
                    });
                }
            }

            if (useMaterialPipeline && materialAlbedoHandle.IsValid() && materialNormalHandle.IsValid())
            {
                using (var builder = renderGraph.AddUnsafePass<SurfaceMaterialMapPassData>("Fluid Sim 2D Surface Material Maps", out var passData))
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
                    builder.SetRenderFunc(static (SurfaceMaterialMapPassData data, UnsafeGraphContext context) =>
                    {
                        CommandBuffer nativeCommandBuffer = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);
                        data.metaballRenderer.RecordSurfaceMaterialMaps(nativeCommandBuffer);
                    });
                }
            }

            bool renderPhaseDiffuseLight = lighting != null && lighting.gaussianSss.ShouldRender();
            bool renderRadianceCascadeLight = lighting != null && lighting.radianceCascadeGi.isActiveAndEnabled;
            bool renderSoftLight = renderPhaseDiffuseLight || renderRadianceCascadeLight;
            LightingResourceHandles lightingResources = lighting != null
                ? ImportLightingResources(renderGraph, lighting, "FluidSim2D", renderCaustics, renderPhaseDiffuseLight, renderRadianceCascadeLight)
                : default;
            ParticleFluidLighting2D.FrameContext lightingContext = lighting != null
                ? metaballRenderer.CreateLightingContext(display, camera)
                : default;
            int causticsFrameIndex = renderCaustics ? lighting.directLight.causticFrameIndex++ : 0;
            LightingInputHandles lightingInputs = new()
            {
                transport = materialTransportHandle,
                materialNormal = materialNormalHandle,
                velocity = velocityTraceHandle,
                gradient = gradientHandle,
                gradient2 = gradient2Handle
            };
            if (lighting != null)
            {
                ParticleFluidLightingInputSet lightingInputSet = metaballRenderer.materialRenderer.MaterialMaps.CreateLightingInputs(
                    lightingContext.renderRegion,
                    lightingContext.sourceSize,
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
                passData.causticMotion = lightingResources.causticMotion;
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
                UseIfValid(builder, passData.causticMotion, AccessFlags.Read);
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

        void RecordJumpFloodRenderGraph(RenderGraph renderGraph, Camera camera, UniversalResourceData resourceData)
        {
            JumpFloodRenderer2D jumpFloodRenderer = display.JumpFloodRenderer;
            if (!jumpFloodRenderer.PrepareForRender(display, camera))
            {
                return;
            }

            ParticleFluidLighting2D lighting = display.ActiveLighting;

            TextureHandle seedAHandle = jumpFloodSeedA.Import(renderGraph, jumpFloodRenderer.SeedA, "FluidSim2D JFA Seed A");
            TextureHandle seedBHandle = jumpFloodSeedB.Import(renderGraph, jumpFloodRenderer.SeedB, "FluidSim2D JFA Seed B");
            TextureHandle payloadAHandle = jumpFloodPayloadA.Import(renderGraph, jumpFloodRenderer.PayloadA, "FluidSim2D JFA Payload A");
            TextureHandle payloadBHandle = jumpFloodPayloadB.Import(renderGraph, jumpFloodRenderer.PayloadB, "FluidSim2D JFA Payload B");
            TextureHandle normalPayloadAHandle = jumpFloodNormalPayloadA.Import(renderGraph, jumpFloodRenderer.NormalPayloadA, "FluidSim2D JFA Normal Payload A");
            TextureHandle normalPayloadBHandle = jumpFloodNormalPayloadB.Import(renderGraph, jumpFloodRenderer.NormalPayloadB, "FluidSim2D JFA Normal Payload B");
            TextureHandle gradientHandle = gradient.Import(renderGraph, display.gradientTexture != null ? display.gradientTexture : Texture2D.blackTexture, "FluidSim2D Gradient");
            TextureHandle gradient2Handle = gradient2.Import(renderGraph, display.gradientTexture2 != null ? display.gradientTexture2 : Texture2D.blackTexture, "FluidSim2D Gradient 2");
            TextureHandle debugHeatMapHandle = debugHeatMap.Import(renderGraph, display.debugHeatMapTexture != null ? display.debugHeatMapTexture : Texture2D.blackTexture, "FluidSim2D Debug Heat Map");
            TextureHandle debugSignedHeatMapHandle = debugSignedHeatMap.Import(renderGraph, display.debugSignedHeatMapTexture != null ? display.debugSignedHeatMapTexture : Texture2D.blackTexture, "FluidSim2D Debug Signed Heat Map");
            TextureHandle materialAlbedoHandle = materialAlbedo.Import(renderGraph, jumpFloodRenderer.MaterialMaps.albedoTexture, "FluidSim2D JFA Material Albedo");
            TextureHandle materialNormalHandle = materialNormal.Import(renderGraph, jumpFloodRenderer.MaterialMaps.normalTexture, "FluidSim2D JFA Material Normal");
            TextureHandle materialTransportHandle = materialTransport.Import(renderGraph, jumpFloodRenderer.MaterialMaps.transportTexture, "FluidSim2D JFA Material Transport");
            bool renderCaustics = lighting != null && lighting.directLight.lightingMode == ParticleFluidDirectLight.LightingMode.Caustics;
            TextureHandle velocityTraceHandle = renderCaustics
                ? velocity.Import(renderGraph, Texture2D.blackTexture, "FluidSim2D JFA Velocity Fallback")
                : TextureHandle.nullHandle;
            bool renderPhaseDiffuseLight = lighting != null && lighting.gaussianSss.ShouldRender();
            bool renderRadianceCascadeLight = lighting != null && lighting.radianceCascadeGi.isActiveAndEnabled;
            bool renderSoftLight = renderPhaseDiffuseLight || renderRadianceCascadeLight;
            LightingResourceHandles lightingResources = lighting != null
                ? ImportLightingResources(renderGraph, lighting, "FluidSim2D JFA", renderCaustics, renderPhaseDiffuseLight, renderRadianceCascadeLight)
                : default;
            ParticleFluidLighting2D.FrameContext lightingContext = default;
            int causticsFrameIndex = renderCaustics ? lighting.directLight.causticFrameIndex++ : 0;
            LightingInputHandles lightingInputs = new()
            {
                transport = materialTransportHandle,
                materialNormal = materialNormalHandle,
                velocity = velocityTraceHandle,
                gradient = gradientHandle,
                gradient2 = gradient2Handle
            };
            if (lighting != null)
            {
                ParticleFluidLightingInputSet lightingInputSet = jumpFloodRenderer.MaterialMaps.CreateLightingInputs(
                    jumpFloodRenderer.CurrentRenderRegion,
                    jumpFloodRenderer.CurrentSourceSize);
                lightingContext = lighting.PrepareLighting(camera, lightingInputSet);
                Texture causticTexture = lighting.directLight.GetCurrentDirectLightTexture();
                lighting.ApplySettings(lightingContext, renderCaustics, renderSoftLight, causticTexture);
            }

            JumpFloodResultHandles jumpFloodResult = RecordJumpFloodField(
                renderGraph,
                display,
                camera,
                jumpFloodRenderer,
                seedAHandle,
                seedBHandle,
                payloadAHandle,
                payloadBHandle,
                normalPayloadAHandle,
                normalPayloadBHandle,
                gradientHandle,
                gradient2Handle,
                debugHeatMapHandle,
                debugSignedHeatMapHandle);

            if (materialTransportHandle.IsValid() || materialAlbedoHandle.IsValid() || materialNormalHandle.IsValid())
            {
                using var materialBuilder = renderGraph.AddUnsafePass<JumpFloodMaterialMapPassData>("Fluid Sim 2D Jump Flood Material Maps", out var materialPass);
                materialPass.display = display;
                materialPass.jumpFloodRenderer = jumpFloodRenderer;
                materialPass.camera = camera;
                materialPass.result = jumpFloodResult.result;
                materialPass.payload = jumpFloodResult.payload;
                materialPass.normalPayload = jumpFloodResult.normalPayload;
                materialPass.gradient = gradientHandle;
                materialPass.gradient2 = gradient2Handle;
                materialPass.albedo = materialAlbedoHandle;
                materialPass.materialNormal = materialNormalHandle;
                materialPass.transport = materialTransportHandle;
                UseIfValid(materialBuilder, materialPass.result, AccessFlags.Read);
                UseIfValid(materialBuilder, materialPass.payload, AccessFlags.Read);
                UseIfValid(materialBuilder, materialPass.normalPayload, AccessFlags.Read);
                UseIfValid(materialBuilder, materialPass.gradient, AccessFlags.Read);
                UseIfValid(materialBuilder, materialPass.gradient2, AccessFlags.Read);
                UseIfValid(materialBuilder, materialPass.albedo, AccessFlags.Write);
                UseIfValid(materialBuilder, materialPass.materialNormal, AccessFlags.Write);
                UseIfValid(materialBuilder, materialPass.transport, AccessFlags.Write);
                materialBuilder.AllowPassCulling(false);
                materialBuilder.SetRenderFunc(static (JumpFloodMaterialMapPassData data, UnsafeGraphContext context) =>
                {
                    CommandBuffer nativeCommandBuffer = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);
                    data.jumpFloodRenderer.RecordMaterialMaps(data.display, data.camera, nativeCommandBuffer);
                });
            }

            if (lighting != null)
            {
                if (renderCaustics)
                {
                    RecordCausticsPasses(renderGraph, "Fluid Sim 2D JFA", lighting, lightingContext, lightingInputs, lightingResources, causticsFrameIndex);
                }

                if (renderSoftLight)
                {
                    RecordSoftLightPass(renderGraph, "Fluid Sim 2D JFA", lighting, lightingContext, lightingInputs, lightingResources);
                }
            }

            using var builder = renderGraph.AddUnsafePass<JumpFloodPassData>("Fluid Sim 2D", out var passData);

            passData.display = display;
            passData.jumpFloodRenderer = jumpFloodRenderer;
            passData.camera = camera;
            passData.color = resourceData.activeColorTexture;
            passData.depth = resourceData.activeDepthTexture;
            passData.result = jumpFloodResult.result;
            passData.payload = jumpFloodResult.payload;
            passData.normalPayload = jumpFloodResult.normalPayload;
            passData.materialAlbedo = materialAlbedoHandle;
            passData.materialNormal = materialNormalHandle;
            passData.materialTransport = materialTransportHandle;
            passData.causticResolved = lightingResources.causticResolved;
            passData.causticTemporal = lightingResources.causticTemporal;
            passData.causticMotion = lightingResources.causticMotion;
            passData.gaussianSoftLight0 = lightingResources.gaussianSoftLight0;
            passData.gaussianSoftLight1 = lightingResources.gaussianSoftLight1;
            passData.radianceCascade0 = lightingResources.radianceCascade0;
            passData.radianceCascade1 = lightingResources.radianceCascade1;
            UseIfValid(builder, passData.color, AccessFlags.Write);
            UseIfValid(builder, passData.depth, AccessFlags.ReadWrite);
            UseIfValid(builder, passData.result, AccessFlags.Read);
            UseIfValid(builder, passData.payload, AccessFlags.Read);
            UseIfValid(builder, passData.normalPayload, AccessFlags.Read);
            UseIfValid(builder, passData.materialAlbedo, AccessFlags.Read);
            UseIfValid(builder, passData.materialNormal, AccessFlags.Read);
            UseIfValid(builder, passData.materialTransport, AccessFlags.Read);
            UseIfValid(builder, passData.causticResolved, AccessFlags.Read);
            UseIfValid(builder, passData.causticTemporal, AccessFlags.Read);
            UseIfValid(builder, passData.causticMotion, AccessFlags.Read);
            UseIfValid(builder, passData.gaussianSoftLight0, AccessFlags.Read);
            UseIfValid(builder, passData.gaussianSoftLight1, AccessFlags.Read);
            UseIfValid(builder, passData.radianceCascade0, AccessFlags.Read);
            UseIfValid(builder, passData.radianceCascade1, AccessFlags.Read);
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
                data.jumpFloodRenderer.RecordCompositeWithPreparedLightingAndMaterialMaps(data.display, data.camera, nativeCommandBuffer, data.color);
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

        static JumpFloodResultHandles RecordJumpFloodField(
            RenderGraph renderGraph,
            ParticleDisplay2D display,
            Camera camera,
            JumpFloodRenderer2D jumpFloodRenderer,
            TextureHandle seedAHandle,
            TextureHandle seedBHandle,
            TextureHandle payloadAHandle,
            TextureHandle payloadBHandle,
            TextureHandle normalPayloadAHandle,
            TextureHandle normalPayloadBHandle,
            TextureHandle gradientHandle,
            TextureHandle gradient2Handle,
            TextureHandle debugHeatMapHandle,
            TextureHandle debugSignedHeatMapHandle)
        {
            using (var clearBuilder = renderGraph.AddComputePass<JumpFloodComputePassData>("Fluid Sim 2D Jump Flood Clear", out var clearPass))
            {
                clearPass.display = display;
                clearPass.jumpFloodRenderer = jumpFloodRenderer;
                clearPass.camera = camera;
                clearPass.result = seedAHandle;
                clearPass.payload = payloadAHandle;
                clearPass.normalPayload = normalPayloadAHandle;
                UseIfValid(clearBuilder, clearPass.result, AccessFlags.Write);
                UseIfValid(clearBuilder, clearPass.payload, AccessFlags.Write);
                UseIfValid(clearBuilder, clearPass.normalPayload, AccessFlags.Write);
                clearBuilder.AllowPassCulling(false);
                clearBuilder.SetRenderFunc(static (JumpFloodComputePassData data, ComputeGraphContext context) =>
                {
                    data.jumpFloodRenderer.RecordClear(data.display, data.camera, context.cmd, data.result, data.payload, data.normalPayload);
                });
            }

            using (var seedBuilder = renderGraph.AddComputePass<JumpFloodSeedPassData>("Fluid Sim 2D Jump Flood Seed", out var seedPass))
            {
                seedPass.display = display;
                seedPass.jumpFloodRenderer = jumpFloodRenderer;
                seedPass.camera = camera;
                seedPass.result = seedAHandle;
                seedPass.payload = payloadAHandle;
                seedPass.normalPayload = normalPayloadAHandle;
                seedPass.gradient = gradientHandle;
                seedPass.gradient2 = gradient2Handle;
                seedPass.debugHeatMap = debugHeatMapHandle;
                seedPass.debugSignedHeatMap = debugSignedHeatMapHandle;
                UseIfValid(seedBuilder, seedPass.result, AccessFlags.Write);
                UseIfValid(seedBuilder, seedPass.payload, AccessFlags.Write);
                UseIfValid(seedBuilder, seedPass.normalPayload, AccessFlags.Write);
                UseIfValid(seedBuilder, seedPass.gradient, AccessFlags.Read);
                UseIfValid(seedBuilder, seedPass.gradient2, AccessFlags.Read);
                UseIfValid(seedBuilder, seedPass.debugHeatMap, AccessFlags.Read);
                UseIfValid(seedBuilder, seedPass.debugSignedHeatMap, AccessFlags.Read);
                seedBuilder.AllowPassCulling(false);
                seedBuilder.SetRenderFunc(static (JumpFloodSeedPassData data, ComputeGraphContext context) =>
                {
                    data.jumpFloodRenderer.RecordSeed(data.display, data.camera, context.cmd, data.result, data.payload, data.normalPayload, data.gradient, data.gradient2, data.debugHeatMap, data.debugSignedHeatMap);
                });
            }

            TextureHandle currentSeed = seedAHandle;
            TextureHandle currentPayload = payloadAHandle;
            TextureHandle currentNormalPayload = normalPayloadAHandle;
            TextureHandle nextSeed = seedBHandle;
            TextureHandle nextPayload = payloadBHandle;
            TextureHandle nextNormalPayload = normalPayloadBHandle;
            RenderTexture currentSeedTexture = jumpFloodRenderer.SeedA;
            RenderTexture currentPayloadTexture = jumpFloodRenderer.PayloadA;
            RenderTexture currentNormalPayloadTexture = jumpFloodRenderer.NormalPayloadA;
            RenderTexture nextSeedTexture = jumpFloodRenderer.SeedB;
            RenderTexture nextPayloadTexture = jumpFloodRenderer.PayloadB;
            RenderTexture nextNormalPayloadTexture = jumpFloodRenderer.NormalPayloadB;

            for (int step = jumpFloodRenderer.GetInitialJumpFloodStep(); step >= 1; step >>= 1)
            {
                using var stepBuilder = renderGraph.AddComputePass<JumpFloodStepPassData>($"Fluid Sim 2D Jump Flood Step {step}", out var stepPass);
                stepPass.display = display;
                stepPass.jumpFloodRenderer = jumpFloodRenderer;
                stepPass.camera = camera;
                stepPass.step = step;
                stepPass.src = currentSeed;
                stepPass.payloadSrc = currentPayload;
                stepPass.normalPayloadSrc = currentNormalPayload;
                stepPass.dst = nextSeed;
                stepPass.payloadDst = nextPayload;
                stepPass.normalPayloadDst = nextNormalPayload;
                UseIfValid(stepBuilder, stepPass.src, AccessFlags.Read);
                UseIfValid(stepBuilder, stepPass.payloadSrc, AccessFlags.Read);
                UseIfValid(stepBuilder, stepPass.normalPayloadSrc, AccessFlags.Read);
                UseIfValid(stepBuilder, stepPass.dst, AccessFlags.Write);
                UseIfValid(stepBuilder, stepPass.payloadDst, AccessFlags.Write);
                UseIfValid(stepBuilder, stepPass.normalPayloadDst, AccessFlags.Write);
                stepBuilder.AllowPassCulling(false);
                stepBuilder.SetRenderFunc(static (JumpFloodStepPassData data, ComputeGraphContext context) =>
                {
                    data.jumpFloodRenderer.RecordStep(data.display, data.camera, context.cmd, data.step, data.src, data.payloadSrc, data.normalPayloadSrc, data.dst, data.payloadDst, data.normalPayloadDst);
                });

                (currentSeed, nextSeed) = (nextSeed, currentSeed);
                (currentPayload, nextPayload) = (nextPayload, currentPayload);
                (currentNormalPayload, nextNormalPayload) = (nextNormalPayload, currentNormalPayload);
                (currentSeedTexture, nextSeedTexture) = (nextSeedTexture, currentSeedTexture);
                (currentPayloadTexture, nextPayloadTexture) = (nextPayloadTexture, currentPayloadTexture);
                (currentNormalPayloadTexture, nextNormalPayloadTexture) = (nextNormalPayloadTexture, currentNormalPayloadTexture);
            }

            jumpFloodRenderer.SetResults(currentSeedTexture, currentPayloadTexture, currentNormalPayloadTexture);
            return new JumpFloodResultHandles
            {
                result = currentSeed,
                payload = currentPayload,
                normalPayload = currentNormalPayload
            };
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
            handles.causticMotion = renderCaustics ? causticMotion.Import(renderGraph, lighting.directLight.causticMotionTexture, $"{prefix} Caustic Motion") : TextureHandle.nullHandle;
            handles.causticMotionDilated = renderCaustics ? causticMotionDilated.Import(renderGraph, lighting.directLight.temporalCaustics.causticMotionDilatedTexture, $"{prefix} Caustic Motion Dilated") : TextureHandle.nullHandle;
            handles.causticMotionDilationScratch = renderCaustics ? causticMotionDilationScratch.Import(renderGraph, lighting.directLight.temporalCaustics.causticMotionDilationScratchTexture, $"{prefix} Caustic Motion Dilation Scratch") : TextureHandle.nullHandle;
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
                passData.causticMotion = resources.causticMotion;
                UseIfValid(builder, passData.causticResolved, AccessFlags.Write);
                UseIfValid(builder, passData.causticMotion, AccessFlags.Write);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc(static (CausticsComputePassData data, ComputeGraphContext context) =>
                {
                    data.lighting.directLight.RecordComputeClear(data.context, context.cmd, data.frameIndex, data.causticResolved, data.causticMotion);
                });
            }

            using (var builder = renderGraph.AddComputePass<CausticsComputePassData>($"{prefix} Caustics Trace", out var passData))
            {
                passData.lighting = lighting;
                passData.context = context;
				passData.frameIndex = frameIndex;
				passData.transport = inputs.transport;
				passData.materialNormal = inputs.materialNormal;
				passData.velocity = inputs.velocity;
				passData.gradient = inputs.gradient;
				passData.gradient2 = inputs.gradient2;
				UseIfValid(builder, passData.transport, AccessFlags.Read);
				UseIfValid(builder, passData.materialNormal, AccessFlags.Read);
				UseIfValid(builder, passData.velocity, AccessFlags.Read);
				UseIfValid(builder, passData.gradient, AccessFlags.Read);
				UseIfValid(builder, passData.gradient2, AccessFlags.Read);
				builder.AllowPassCulling(false);
				builder.SetRenderFunc(static (CausticsComputePassData data, ComputeGraphContext context) =>
				{
					data.lighting.directLight.RecordComputeTrace(data.context, context.cmd, data.frameIndex, data.transport, data.materialNormal, data.velocity, data.gradient, data.gradient2);
				});
			}

            using (var builder = renderGraph.AddComputePass<CausticsComputePassData>($"{prefix} Caustics Resolve", out var passData))
            {
                passData.lighting = lighting;
                passData.context = context;
                passData.frameIndex = frameIndex;
                passData.causticResolved = resources.causticResolved;
                passData.causticMotion = resources.causticMotion;
                UseIfValid(builder, passData.causticResolved, AccessFlags.Write);
                UseIfValid(builder, passData.causticMotion, AccessFlags.Write);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc(static (CausticsComputePassData data, ComputeGraphContext context) =>
                {
                    data.lighting.directLight.RecordComputeResolve(data.context, context.cmd, data.frameIndex, data.causticResolved, data.causticMotion);
                });
            }

            using (var builder = renderGraph.AddUnsafePass<CausticsBlurPassData>($"{prefix} Caustics Blur", out var passData))
            {
                passData.lighting = lighting;
                passData.context = context;
                passData.causticResolved = resources.causticResolved;
                passData.causticBlur = resources.causticBlur;
                passData.causticMotion = resources.causticMotion;
                passData.causticMotionDilated = resources.causticMotionDilated;
                passData.causticMotionDilationScratch = resources.causticMotionDilationScratch;
                UseIfValid(builder, passData.causticResolved, AccessFlags.ReadWrite);
                UseIfValid(builder, passData.causticMotion, AccessFlags.ReadWrite);
                UseIfValid(builder, passData.causticBlur, AccessFlags.Write);
                UseIfValid(builder, passData.causticMotionDilated, AccessFlags.ReadWrite);
                UseIfValid(builder, passData.causticMotionDilationScratch, AccessFlags.ReadWrite);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc(static (CausticsBlurPassData data, UnsafeGraphContext context) =>
                {
                    CommandBuffer nativeCommandBuffer = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);
                    data.lighting.directLight.temporalCaustics.RecordBlurAndMotion(data.context, nativeCommandBuffer);
                });
            }

            using (var builder = renderGraph.AddUnsafePass<CausticsTemporalPassData>($"{prefix} Caustics Temporal", out var passData))
            {
                passData.lighting = lighting;
                passData.context = context;
                passData.transport = inputs.transport;
                passData.causticResolved = resources.causticResolved;
                passData.causticTemporal = resources.causticTemporal;
                passData.causticHistory = resources.causticHistory;
                passData.causticMotion = resources.causticMotion;
                passData.causticMotionDilated = resources.causticMotionDilated;
                passData.causticMotionDilationScratch = resources.causticMotionDilationScratch;
                UseIfValid(builder, passData.causticResolved, AccessFlags.Read);
                UseIfValid(builder, passData.causticTemporal, AccessFlags.ReadWrite);
                UseIfValid(builder, passData.causticHistory, AccessFlags.ReadWrite);
                UseIfValid(builder, passData.causticMotion, AccessFlags.Read);
                UseIfValid(builder, passData.causticMotionDilated, AccessFlags.Read);
                UseIfValid(builder, passData.causticMotionDilationScratch, AccessFlags.Read);
                UseIfValid(builder, passData.transport, AccessFlags.Read);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc(static (CausticsTemporalPassData data, UnsafeGraphContext context) =>
                {
                    CommandBuffer nativeCommandBuffer = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);
                    RenderTexture temporalMotionTexture = data.lighting.directLight.temporalCaustics.GetTemporalMotionTextureAfterBlur();
                    data.lighting.directLight.temporalCaustics.RecordTemporal(data.context, nativeCommandBuffer, temporalMotionTexture);
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
			public TextureHandle velocity;
			public TextureHandle gradient;
			public TextureHandle gradient2;
            public TextureHandle causticResolved;
            public TextureHandle causticMotion;
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
            public TextureHandle causticMotion;
            public TextureHandle causticMotionDilated;
            public TextureHandle causticMotionDilationScratch;
            public TextureHandle gaussianSoftLight0;
            public TextureHandle gaussianSoftLight1;
            public TextureHandle radianceCascade0;
            public TextureHandle radianceCascade1;
            public TextureHandle radianceCascadeSdfSeedA;
            public TextureHandle radianceCascadeSdfSeedB;
            public TextureHandle radianceCascadeSdfPayloadA;
            public TextureHandle radianceCascadeSdfPayloadB;
        }

        struct JumpFloodResultHandles
        {
            public TextureHandle result;
            public TextureHandle payload;
            public TextureHandle normalPayload;
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
            public TextureHandle transport;
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

        class TransportMapPassData
        {
            public MetaballRenderer2D metaballRenderer;
            public TextureHandle combined;
            public TextureHandle normal;
            public TextureHandle gradient;
            public TextureHandle gradient2;
            public TextureHandle transport;
        }

        class SurfaceMaterialMapPassData
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
            public RenderTexture sourceTexture;
            public RenderTexture destinationTexture;
            public Material material;
            public Vector2 direction;
        }

        class MotionPyramidPassData
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
            public TextureHandle causticMotion;
            public TextureHandle gaussianSoftLight0;
            public TextureHandle gaussianSoftLight1;
            public TextureHandle radianceCascade0;
            public TextureHandle radianceCascade1;
        }

        class JumpFloodPassData
        {
            public ParticleDisplay2D display;
            public JumpFloodRenderer2D jumpFloodRenderer;
            public Camera camera;
            public TextureHandle color;
            public TextureHandle depth;
            public TextureHandle result;
            public TextureHandle payload;
            public TextureHandle normalPayload;
            public TextureHandle materialAlbedo;
            public TextureHandle materialNormal;
            public TextureHandle materialTransport;
            public TextureHandle causticResolved;
            public TextureHandle causticTemporal;
            public TextureHandle causticMotion;
            public TextureHandle gaussianSoftLight0;
            public TextureHandle gaussianSoftLight1;
            public TextureHandle radianceCascade0;
            public TextureHandle radianceCascade1;
        }

        class JumpFloodComputePassData
        {
            public ParticleDisplay2D display;
            public JumpFloodRenderer2D jumpFloodRenderer;
            public Camera camera;
            public TextureHandle result;
            public TextureHandle payload;
            public TextureHandle normalPayload;
        }

        class JumpFloodSeedPassData : JumpFloodComputePassData
        {
            public TextureHandle gradient;
            public TextureHandle gradient2;
            public TextureHandle debugHeatMap;
            public TextureHandle debugSignedHeatMap;
        }

        class JumpFloodStepPassData
        {
            public ParticleDisplay2D display;
            public JumpFloodRenderer2D jumpFloodRenderer;
            public Camera camera;
            public int step;
            public TextureHandle src;
            public TextureHandle payloadSrc;
            public TextureHandle normalPayloadSrc;
            public TextureHandle dst;
            public TextureHandle payloadDst;
            public TextureHandle normalPayloadDst;
        }

        class JumpFloodMaterialMapPassData
        {
            public ParticleDisplay2D display;
            public JumpFloodRenderer2D jumpFloodRenderer;
            public Camera camera;
            public TextureHandle result;
            public TextureHandle payload;
            public TextureHandle normalPayload;
            public TextureHandle gradient;
            public TextureHandle gradient2;
            public TextureHandle albedo;
            public TextureHandle materialNormal;
            public TextureHandle transport;
        }

    }
}
