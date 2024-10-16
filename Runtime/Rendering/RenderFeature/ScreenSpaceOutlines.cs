using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace TripleA.ToonShader.RenderFeature
{
    public class ScreenSpaceOutlines : ScriptableRendererFeature {

        [Serializable]
        private class ScreenSpaceOutlineSettings {

            [Header("General Outline Settings")]
            public Color outlineColor = Color.black;
            [Range(0.0f, 20.0f)]
            public float outlineScale = 1.0f;
        
            [Header("Depth Settings")]
            [Range(0.0f, 100.0f)]
            public float depthThreshold = 1.5f;
            [Range(0.0f, 500.0f)]
            public float robertsCrossMultiplier = 100.0f;

            [Header("Normal Settings")]
            [Range(0.0f, 1.0f)]
            public float normalThreshold = 0.4f;

            [Header("Depth Normal Relation Settings")]
            [Range(0.0f, 2.0f)]
            public float steepAngleThreshold = 0.2f;
            [Range(0.0f, 500.0f)]
            public float steepAngleMultiplier = 25.0f;
        
            [Header("General Scene View Space Normal Texture Settings")]
            public RenderTextureFormat colorFormat;
            public int depthBufferBits;
            public FilterMode filterMode;
            public Color backgroundColor = Color.clear;

            [Header("View Space Normal Texture Object Draw Settings")]
            public PerObjectData perObjectData;
            public bool enableDynamicBatching;
            public bool enableInstancing;

        }

        private class ScreenSpaceOutlinePass : ScriptableRenderPass {
        
            private readonly Material m_screenSpaceOutlineMaterial;
            private ScreenSpaceOutlineSettings m_settings;

            private FilteringSettings m_filteringSettings;

            private readonly List<ShaderTagId> m_shaderTagIdList;
            private readonly Material m_normalsMaterial;

            private RTHandle m_normals;
            private RendererList m_normalsRenderersList;

            private RTHandle m_temporaryBuffer;

            public ScreenSpaceOutlinePass(RenderPassEvent renderPassEvent, LayerMask layerMask,
                ScreenSpaceOutlineSettings settings) {
                this.m_settings = settings;
                this.renderPassEvent = renderPassEvent;

                m_screenSpaceOutlineMaterial = new Material(Shader.Find("Hidden/Outlines"));
                m_screenSpaceOutlineMaterial.SetColor(m_S_OutlineColor, settings.outlineColor);
                m_screenSpaceOutlineMaterial.SetFloat(m_S_OutlineScale, settings.outlineScale);

                m_screenSpaceOutlineMaterial.SetFloat(m_S_DepthThreshold, settings.depthThreshold);
                m_screenSpaceOutlineMaterial.SetFloat(m_S_RobertsCrossMultiplier, settings.robertsCrossMultiplier);

                m_screenSpaceOutlineMaterial.SetFloat(m_S_NormalThreshold, settings.normalThreshold);

                m_screenSpaceOutlineMaterial.SetFloat(m_S_SteepAngleThreshold, settings.steepAngleThreshold);
                m_screenSpaceOutlineMaterial.SetFloat(m_S_SteepAngleMultiplier, settings.steepAngleMultiplier);
            
                m_filteringSettings = new FilteringSettings(RenderQueueRange.opaque, layerMask);

                m_shaderTagIdList = new List<ShaderTagId> {
                    new ShaderTagId("UniversalForward"),
                    new ShaderTagId("UniversalForwardOnly"),
                    new ShaderTagId("LightweightForward"),
                    new ShaderTagId("SRPDefaultUnlit")
                };

                m_normalsMaterial = new Material(Shader.Find("Hidden/ViewSpaceNormals"));
            }

            [Obsolete("Obsolete")]
            public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData) {
                // Normals
                RenderTextureDescriptor textureDescriptor = renderingData.cameraData.cameraTargetDescriptor;
                textureDescriptor.colorFormat = m_settings.colorFormat;
                textureDescriptor.depthBufferBits = m_settings.depthBufferBits;
                RenderingUtils.ReAllocateIfNeeded(ref m_normals, textureDescriptor, m_settings.filterMode);
            
                // Color Buffer
                textureDescriptor.depthBufferBits = 0;
                RenderingUtils.ReAllocateIfNeeded(ref m_temporaryBuffer, textureDescriptor, FilterMode.Bilinear);

                ConfigureTarget(m_normals, renderingData.cameraData.renderer.cameraDepthTargetHandle);
                ConfigureClear(ClearFlag.Color, m_settings.backgroundColor);
            }

            [Obsolete("Obsolete")]
            public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData) {
                if (!m_screenSpaceOutlineMaterial || !m_normalsMaterial || 
                    renderingData.cameraData.renderer.cameraColorTargetHandle.rt == null ||
                    m_temporaryBuffer.rt == null)
                    return;

                CommandBuffer cmd = CommandBufferPool.Get();
                context.ExecuteCommandBuffer(cmd);
                cmd.Clear();
                
                // Normals
                DrawingSettings drawSettings = CreateDrawingSettings(m_shaderTagIdList, ref renderingData,
                    renderingData.cameraData.defaultOpaqueSortFlags);
                drawSettings.perObjectData = m_settings.perObjectData;
                drawSettings.enableDynamicBatching = m_settings.enableDynamicBatching;
                drawSettings.enableInstancing = m_settings.enableInstancing;
                drawSettings.overrideMaterial = m_normalsMaterial;
            
                RendererListParams normalsRenderersParams =
                    new RendererListParams(renderingData.cullResults, drawSettings, m_filteringSettings);
                m_normalsRenderersList = context.CreateRendererList(ref normalsRenderersParams);
                cmd.DrawRendererList(m_normalsRenderersList);
            
                // Pass in RT for Outlines shader
                cmd.SetGlobalTexture(Shader.PropertyToID("_SceneViewSpaceNormals"), m_normals.rt);
            
                using (new ProfilingScope(cmd, new ProfilingSampler("ScreenSpaceOutlines"))) {

                    Blitter.BlitCameraTexture(cmd, renderingData.cameraData.renderer.cameraColorTargetHandle,
                        m_temporaryBuffer, m_screenSpaceOutlineMaterial, 0);
                    Blitter.BlitCameraTexture(cmd, m_temporaryBuffer,
                        renderingData.cameraData.renderer.cameraColorTargetHandle);
                }

                context.ExecuteCommandBuffer(cmd);
                CommandBufferPool.Release(cmd);
            }

            public void Release(){
                CoreUtils.Destroy(m_screenSpaceOutlineMaterial);
                CoreUtils.Destroy(m_normalsMaterial);
                m_normals?.Release();
                m_temporaryBuffer?.Release();
            }

        }

        [SerializeField] private RenderPassEvent m_renderPassEvent = RenderPassEvent.BeforeRenderingSkybox;
        [SerializeField] private LayerMask m_outlinesLayerMask;
    
        [SerializeField] private ScreenSpaceOutlineSettings m_outlineSettings = new ScreenSpaceOutlineSettings();

        private ScreenSpaceOutlinePass m_screenSpaceOutlinePass;
        
        private static readonly int m_S_OutlineColor = Shader.PropertyToID("_OutlineColor");
        private static readonly int m_S_OutlineScale = Shader.PropertyToID("_OutlineScale");
        private static readonly int m_S_DepthThreshold = Shader.PropertyToID("_DepthThreshold");
        private static readonly int m_S_RobertsCrossMultiplier = Shader.PropertyToID("_RobertsCrossMultiplier");
        private static readonly int m_S_NormalThreshold = Shader.PropertyToID("_NormalThreshold");
        private static readonly int m_S_SteepAngleThreshold = Shader.PropertyToID("_SteepAngleThreshold");
        private static readonly int m_S_SteepAngleMultiplier = Shader.PropertyToID("_SteepAngleMultiplier");

        public override void Create() {
            if (m_renderPassEvent < RenderPassEvent.BeforeRenderingPrePasses)
                m_renderPassEvent = RenderPassEvent.BeforeRenderingPrePasses;

            m_screenSpaceOutlinePass = new ScreenSpaceOutlinePass(m_renderPassEvent, m_outlinesLayerMask, m_outlineSettings);
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData) {
            renderer.EnqueuePass(m_screenSpaceOutlinePass);
        }

        protected override void Dispose(bool disposing){
            if (disposing)
            {
                m_screenSpaceOutlinePass?.Release();
            }
        }

    }
}