using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using System.Runtime.InteropServices;
using System;
using UnityEngine.Rendering.Universal;
using System.Runtime.InteropServices.WindowsRuntime;

public enum FSR2Mode
{
    Disabled = 0,
    Quality,
    Balanced,
    Performance,
    UltraPerformance,
    [InspectorName(null)]
    Max
}
public class FSR2PassControl : MonoBehaviour
{
    struct ModeData
    {
        public float renderScale;
        public float mipmapBias;
    }
    readonly ModeData[] modeData = new ModeData[(int)FSR2Mode.Max]
    {
        new(){renderScale = 1.0f, mipmapBias = 0.0f},
        new(){renderScale = 1.0f / 1.5f, mipmapBias = -1.58f},
        new(){renderScale = 1.0f / 1.7f, mipmapBias = -1.76f},
        new(){renderScale = 1.0f / 2.0f, mipmapBias= -2.0f},
        new(){renderScale = 1.0f / 3.0f , mipmapBias = -2.58f}
    };

    public FSR2Mode FSR2Quality = FSR2Mode.Disabled;

    [HideInInspector]
    public FSR2Pass.FSR2InitParam.FlagBits FSR2ContextFlags;

    [Header("Sharpening")]
    public bool EnableSharpening = true;
    [Range(0f, 1f)]
    public float Sharpness = .3f;

    [System.Serializable]
    public class ReactiveMaskParameter
    {
        public bool OutputReactiveMask = false;
        public bool EnableAutoReactive = true;
		[Range(0.0f, 1.0f)]
        public float ReactiveMaskScale = .3f;
        [Range(0.0f, 1.0f)]
        public float CutoffThreshold = .3f;
        [Range(0.0f, 1.0f)]
        public float BinaryValue = .3f;
        public FSR2Pass.FSR2GenReactiveParam.FlagBits ReactiveMaskFlags;
        [HideInInspector]
        public RenderTexture OptReactiveMaskTex;
        [HideInInspector]
        public RenderTexture OptTransparencyAndCompositionTex;

		[Range(0.0f, 1.0f)]
		public float AutoTcThreshold = 0.05f;
        [Range(0.0f, 2.0f)]
        public float AutoTcScale = 1.0f;
        [Range(0.0f, 20.0f)]
        public float AutoReactiveScale = 5.0f;
		[Range(0.0f, 1.0f)]
		public float AutoReactiveMax = 0.9f;
	};
    public ReactiveMaskParameter reactiveMaskParameter;

    UniversalRenderPipelineAsset urpAsset;

    new Camera camera;

    bool originPostProcessingEnabled = false;
    bool originMSAAEnabled = false;
    AntialiasingMode originAAMode = AntialiasingMode.None;
    UniversalAdditionalCameraData uaCameraData = null;
    void Start()
    {
        urpAsset = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
        if (urpAsset == null)
            Debug.LogError("no UniversalRenderPipelineAsset used in current pipeline");

        camera = GetComponent<Camera>();
        if (camera == null)
            Debug.LogError("No Camera component found in this GameObject");
        uaCameraData = camera.GetUniversalAdditionalCameraData();
        if (uaCameraData == null)
            Debug.LogError("No UniversalAdditionalCameraData found");
        originPostProcessingEnabled = uaCameraData.renderPostProcessing;
        originMSAAEnabled = camera.allowMSAA;
        originAAMode = uaCameraData.antialiasing;

        if (!FSR2Feature.IsSupported())
        {
            Debug.LogError("FSR2 is not supported on current platform");
            FSR2Quality = FSR2Mode.Disabled;
            this.enabled = false;
        }
    }

    FSR2Mode lastFSR2Mode = FSR2Mode.Max;

    void Update()
    {
        if (lastFSR2Mode != FSR2Quality)
        {
            if (FSR2Quality >= FSR2Mode.Disabled && FSR2Quality <= FSR2Mode.UltraPerformance)
            {
                ref var current_mode_data = ref modeData[(int)FSR2Quality];
                if (FSR2Quality != FSR2Mode.Disabled)
                {
                    Shader.EnableKeyword("_AMD_FSR2");
                    uaCameraData.renderPostProcessing = true;
                    camera.allowMSAA = false;
                    uaCameraData.antialiasing = AntialiasingMode.None;
                }
                else
                {
                    Shader.EnableKeyword("_AMD_FSR2");
                    uaCameraData.renderPostProcessing = originPostProcessingEnabled;
                    camera.allowMSAA = originMSAAEnabled;
                    uaCameraData.antialiasing = originAAMode;
                }
                urpAsset.renderScale = current_mode_data.renderScale;
                Shader.SetGlobalFloat("amd_fsr2_mipmap_bias", current_mode_data.mipmapBias);
                lastFSR2Mode = FSR2Quality;
            }
        }
    }
}