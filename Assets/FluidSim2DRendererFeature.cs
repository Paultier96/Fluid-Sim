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

            ParticleDisplay2D display = Object.FindFirstObjectByType<ParticleDisplay2D>();
            if (display == null || !display.isActiveAndEnabled)
                return;
            if (display.sim == null || display.sim.positionBuffer == null)
                return;

            bool canRenderMetaballs = display.renderMode == ParticleDisplay2D.RenderMode.Metaballs &&
                                      display.mesh != null &&
                                      display.argsBuffer != null &&
                                      display.metaballs.compositeShader != null &&
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

            using var builder = renderGraph.AddUnsafePass<PassData>("Fluid Sim 2D", out var passData);

            passData.display = display;
            passData.metaballRenderer = display.MetaballRenderer;
            passData.jumpFloodRenderer = display.JumpFloodRenderer;
            passData.camera = cameraData.camera;
            passData.color = resourceData.activeColorTexture;
            passData.depth = resourceData.activeDepthTexture;
            builder.UseTexture(passData.color, AccessFlags.Write);
            if (passData.depth.IsValid())
            {
                builder.UseTexture(passData.depth, AccessFlags.ReadWrite);
            }
            builder.AllowPassCulling(false);

            builder.SetRenderFunc(static (PassData data, UnsafeGraphContext context) =>
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
                if (data.display.renderMode == ParticleDisplay2D.RenderMode.Metaballs)
                {
                    data.metaballRenderer.Record(data.display, data.camera, nativeCommandBuffer, data.color);
                }
                else if (data.display.renderMode == ParticleDisplay2D.RenderMode.JumpFlood)
                {
                    data.jumpFloodRenderer.Record(data.display, data.camera, nativeCommandBuffer, data.color);
                }
            });
        }

        class PassData
        {
            public ParticleDisplay2D display;
            public MetaballRenderer2D metaballRenderer;
            public JumpFloodRenderer2D jumpFloodRenderer;
            public Camera camera;
            public TextureHandle color;
            public TextureHandle depth;
        }
    }
}
