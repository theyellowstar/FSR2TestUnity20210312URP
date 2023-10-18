using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using System.Runtime.InteropServices;
using System;
using UnityEngine.XR;

public class FSR2Pass : ScriptableRenderPass
{
    public struct FSR2InitParam
    {
        [System.Flags]
        public enum FlagBits
        {
            FFX_FSR2_ENABLE_HIGH_DYNAMIC_RANGE = (1 << 0),
            FFX_FSR2_ENABLE_DISPLAY_RESOLUTION_MOTION_VECTORS = (1 << 1),
            FFX_FSR2_ENABLE_MOTION_VECTORS_JITTER_CANCELLATION = (1 << 2),
            FFX_FSR2_ENABLE_DEPTH_INVERTED = (1 << 3),
            FFX_FSR2_ENABLE_DEPTH_INFINITE = (1 << 4),
            FFX_FSR2_ENABLE_AUTO_EXPOSURE = (1 << 5),
            FFX_FSR2_ENABLE_DYNAMIC_RESOLUTION = (1 << 6),
            FFX_FSR2_ENABLE_TEXTURE1D_USAGE = (1 << 7)
        }
        public UInt32 flags;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public UInt32[] displaySize;
    }
    private FSR2InitParam initParam = new()
    {
        flags = 0,
        displaySize = new UInt32[2] { 0, 0 }
    };
    public struct FSR2GenReactiveParam
    {
        [System.Flags]
        public enum FlagBits
        {
            FFX_FSR2_AUTOREACTIVEFLAGS_APPLY_TONEMAP = (1 << 0),
            FFX_FSR2_AUTOREACTIVEFLAGS_APPLY_INVERSETONEMAP = (1 << 1),
            FFX_FSR2_AUTOREACTIVEFLAGS_APPLY_THRESHOLD = (1 << 2),
            FFX_FSR2_AUTOREACTIVEFLAGS_USE_COMPONENTS_MAX = (1 << 3)
        }
        public IntPtr colorOpaqueOnly;
        public IntPtr colorPreUpscale;
        public IntPtr outReactive;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public UInt32[] renderSize;
        public float scale;
        public float cutoffThreshold;
        public float binaryValue;
        public UInt32 flags;
    }
    private struct FSR2ExecuteParam
    {
        public IntPtr color;
        public IntPtr depth;
        public IntPtr motionVectors;
        public IntPtr reactive;
        public IntPtr transparencyAndComposition;
        public IntPtr output;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public float[] motionVectorScale;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public UInt32[] renderSize;
        [MarshalAs(UnmanagedType.I1)]
        public bool enableSharpening;
        public float sharpness;
        public float frameTimeDeltaInSec;
        public float cameraNear;
        public float cameraFar;
        public float cameraFOV;

		// EXPERIMENTAL reactive mask generation parameters
		public bool enableAutoReactive;
		public IntPtr colorOpaqueOnly;
		public float autoTcThreshold;
		public float autoTcScale;
		public float autoReactiveScale;
		public float autoReactiveMax;
	}

#if UNITY_EDITOR
    const String fsr2UnityPluginName = "fsr2-unity-plugind";
#else
    const String fsr2UnityPluginName = "fsr2-unity-plugin";
#endif

    [DllImport(fsr2UnityPluginName)]
    static extern void FSR2Initialize(FSR2InitParam p_init_param);

    [DllImport(fsr2UnityPluginName)]
    static extern void FSR2GetProjectionMatrixJitterOffset(UInt32 render_width, UInt32 render_height, UInt32 display_width, [MarshalAs(UnmanagedType.LPArray, SizeConst = 2)] float[] jitter_offset);

    [DllImport(fsr2UnityPluginName)]
    static extern void FSR2GenReactiveMask(FSR2GenReactiveParam gen_reactive_param);

    [DllImport(fsr2UnityPluginName)]
    static extern void FSR2Execute(FSR2ExecuteParam exe_param);

    public string texColorName = "_CameraColorTexture";
    public string texDepthName = "_CameraDepthTexture";
    public string texMotionVectors = "_MotionVectorTexture";
    public string texOpaque = "_CameraOpaqueTexture";

    private int texColorID;
    private int texDepthID;
    private int texMotionVectorsID;
    private int texOpaqueID;

    private FSR2PassControl fsr2PassControl;

    public FSR2Pass()
    {
        texColorID = Shader.PropertyToID(texColorName);
        texDepthID = Shader.PropertyToID(texDepthName);
        texMotionVectorsID = Shader.PropertyToID(texMotionVectors);
        texOpaqueID = Shader.PropertyToID(texOpaque);

    }
    public override void Configure(CommandBuffer cmd, RenderTextureDescriptor cameraTextureDescriptor)
    {
        ConfigureInput(ScriptableRenderPassInput.Motion | ScriptableRenderPassInput.Color | ScriptableRenderPassInput.Depth);
    }

    private RenderTexture texFSR2Output;

    private void CreateUAVRes(int width, int height, RenderTextureFormat rtFormat, bool isRGB, ref RenderTexture outTex)
    {
        if (outTex != null) { outTex.Release(); }
        outTex = RenderTexture.GetTemporary(new RenderTextureDescriptor(width, height, rtFormat) { sRGB = isRGB, enableRandomWrite = true });
        outTex.Create();
    }

    bool displaySizeChanged = true;
    bool initialized = false;
    public void Initialize(ref RenderingData renderingData)
    {
        ref var cameraData = ref renderingData.cameraData;
        var camera = cameraData.camera;
        var display_size = initParam.displaySize;
        if (display_size[0] != camera.pixelWidth || display_size[1] != camera.pixelHeight)
        {
            displaySizeChanged = true;
            display_size[0] = (UInt32)camera.pixelWidth;
            display_size[1] = (UInt32)camera.pixelHeight;
            initParam.flags = (UInt32)(
                FSR2InitParam.FlagBits.FFX_FSR2_ENABLE_AUTO_EXPOSURE
                | FSR2InitParam.FlagBits.FFX_FSR2_ENABLE_DEPTH_INVERTED
                );
            if (cameraData.isHdrEnabled)
                initParam.flags |= (UInt32)FSR2InitParam.FlagBits.FFX_FSR2_ENABLE_HIGH_DYNAMIC_RANGE;
            FSR2Initialize(initParam);
        }
        initialized = true;
    }

    private RenderTexture texReactiveOut;
    private int[] lastRenderSize = new int[2] { 0, 0 };
    public void ExecuteForReactiveMask(ScriptableRenderContext context, ref RenderingData renderingData)
    {
        if (fsr2PassControl.FSR2Quality != FSR2Mode.Disabled)
        {
            CommandBuffer cmd = CommandBufferPool.Get();

            cmd.SetRenderTarget(RenderTargetHandle.CameraTarget.id);
            context.ExecuteCommandBuffer(cmd);
            context.Submit();
            cmd.Release();

            var reactiveMaskParam = fsr2PassControl.reactiveMaskParameter;
            if (reactiveMaskParam.OutputReactiveMask)
            {
                Initialize(ref renderingData);
                ref var cameraData = ref renderingData.cameraData;
                var camera = cameraData.camera;

                var texOpaque = Shader.GetGlobalTexture(texOpaqueID);
                var texColor = Shader.GetGlobalTexture(texColorID);
                if (texColor == null || texOpaque == null)
                    return;

                // create uav for reactive
                int renderSizeWidth = (int)(camera.pixelWidth * cameraData.renderScale);
                int renderSizeHeight = (int)(camera.pixelHeight * cameraData.renderScale);
                if (lastRenderSize[0] != renderSizeWidth || lastRenderSize[1] != renderSizeHeight)
                {
                    CreateUAVRes(renderSizeWidth, renderSizeHeight, RenderTextureFormat.R8, false, ref texReactiveOut);
                    lastRenderSize[0] = renderSizeWidth;
                    lastRenderSize[1] = renderSizeHeight;
                }

                // gen reactive mask
                FSR2GenReactiveMask(new FSR2GenReactiveParam()
                {
                    colorOpaqueOnly = texOpaque.GetNativeTexturePtr(),
                    colorPreUpscale = texColor.GetNativeTexturePtr(),
                    outReactive = texReactiveOut.GetNativeTexturePtr(),
                    renderSize = new UInt32[2] { (UInt32)renderSizeWidth, (UInt32)renderSizeHeight },
                    scale = reactiveMaskParam.ReactiveMaskScale,
                    cutoffThreshold = 1.0f,
                    binaryValue = 1.0f,
                    flags = (UInt32)reactiveMaskParam.ReactiveMaskFlags
                });

            }
        }
    }

    public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
    {
        if (fsr2PassControl.FSR2Quality != FSR2Mode.Disabled)
        {
            CommandBuffer cmd = CommandBufferPool.Get();

            // texColor could still be used as render target, which will cause set resource failed later 
            cmd.SetRenderTarget(RenderTargetHandle.CameraTarget.id);
            context.ExecuteCommandBuffer(cmd);
            context.Submit();
            cmd.Release();

            if (!initialized)
            {
                Initialize(ref renderingData);
            }
            initialized = false;

            var texColor = Shader.GetGlobalTexture(texColorID) as RenderTexture;
            ref var cameraData = ref renderingData.cameraData;
            var camera = cameraData.camera;
            if (displaySizeChanged)
            {
                CreateUAVRes(camera.pixelWidth, camera.pixelHeight, RenderTextureFormat.ARGB32, texColor.sRGB, ref texFSR2Output);
                displaySizeChanged = false;
            }
            var texDepth = Shader.GetGlobalTexture(texDepthID);
            var texMV = Shader.GetGlobalTexture(texMotionVectorsID);
            var texOpaque = Shader.GetGlobalTexture(texOpaqueID);
            if (texColor == null || texDepth == null || texMV == null || texOpaque == null)
                return;

            int renderSizeWidth = (int)(camera.pixelWidth * cameraData.renderScale);
            int renderSizeHeight = (int)(camera.pixelHeight * cameraData.renderScale);
            RenderTexture reactive, transparencyAndComposition;
            var reactiveMaskParam = fsr2PassControl.reactiveMaskParameter;
			if (reactiveMaskParam.EnableAutoReactive)
			{
				reactive = null;
				transparencyAndComposition = null;
			}
			else if (reactiveMaskParam.OutputReactiveMask)
            {
                reactive = texReactiveOut;
                transparencyAndComposition = null;
            }
            else
            {
				reactive = reactiveMaskParam.OptReactiveMaskTex;
				transparencyAndComposition = reactiveMaskParam.OptTransparencyAndCompositionTex;
			}
            FSR2Execute(new FSR2ExecuteParam()
            {
                color = texColor.GetNativeTexturePtr(),
                depth = texDepth.GetNativeTexturePtr(),
                motionVectors = texMV.GetNativeTexturePtr(),
                reactive = reactive ? reactive.GetNativeTexturePtr() : IntPtr.Zero,
                transparencyAndComposition = transparencyAndComposition ? transparencyAndComposition.GetNativeTexturePtr() : IntPtr.Zero,
                output = texFSR2Output.GetNativeTexturePtr(),
                motionVectorScale = new float[2] { -1 * renderSizeWidth, 1 * renderSizeHeight },
                renderSize = new UInt32[2] { (UInt32)renderSizeWidth, (UInt32)renderSizeHeight },
                enableSharpening = fsr2PassControl.EnableSharpening,
                sharpness = fsr2PassControl.Sharpness,
                frameTimeDeltaInSec = Time.deltaTime,
                cameraNear = camera.nearClipPlane,
                cameraFar = camera.farClipPlane,
                cameraFOV = camera.fieldOfView * Mathf.Deg2Rad,

				enableAutoReactive = fsr2PassControl.reactiveMaskParameter.EnableAutoReactive,
				colorOpaqueOnly = texOpaque.GetNativeTexturePtr(),
				autoTcThreshold = fsr2PassControl.reactiveMaskParameter.AutoTcThreshold,
                autoTcScale = fsr2PassControl.reactiveMaskParameter.AutoTcScale,
				autoReactiveScale = fsr2PassControl.reactiveMaskParameter.AutoReactiveScale,
				autoReactiveMax = fsr2PassControl.reactiveMaskParameter.AutoReactiveMax,
			});
            renderingData.cameraData.fsr2Output = texFSR2Output;
        }
        else
        {
            renderingData.cameraData.fsr2Output = null;
        }
    }

    Matrix4x4 jitterMat = Matrix4x4.identity;
    private float[] jitterOffset = new float[2];
    public void Setup(FSR2PassControl newFSR2PassControl, ref RenderingData renderingData)
    {
        fsr2PassControl = newFSR2PassControl;
        ref var cameraData = ref renderingData.cameraData;
        if (fsr2PassControl.FSR2Quality != FSR2Mode.Disabled)
        {
            var camera = cameraData.camera;
            uint render_size_width = (uint)(cameraData.camera.pixelWidth * cameraData.renderScale);
            uint render_size_height = (uint)(camera.pixelHeight * cameraData.renderScale);
            FSR2GetProjectionMatrixJitterOffset(render_size_width, render_size_height, (UInt32)camera.pixelWidth, jitterOffset);
            jitterMat.m03 = jitterOffset[0];
            jitterMat.m13 = -jitterOffset[1];
            cameraData.jitterMatrix = jitterMat;
        }
        else
            cameraData.jitterMatrix = Matrix4x4.identity;
    }
}