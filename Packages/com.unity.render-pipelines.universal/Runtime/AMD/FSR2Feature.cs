using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;

public class FSR2Feature : ScriptableRendererFeature
{
    FSR2Pass fsr2Pass;
    FSR2GenerateReactiveMaskPass fsr2GenReactiveMaskPass;
    public static bool IsSupported()
    {
        return SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Direct3D11;
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        ref var camera_data = ref renderingData.cameraData;
        var camera = renderingData.cameraData.camera;
        var fsr2PassControl = camera.GetComponent<FSR2PassControl>();
        if (fsr2PassControl != null)
        {
            fsr2Pass.Setup(fsr2PassControl, ref renderingData);
            renderer.EnqueuePass(fsr2Pass);
            if (fsr2PassControl.reactiveMaskParameter.OutputReactiveMask)
                renderer.EnqueuePass(fsr2GenReactiveMaskPass);
        }
    }

    public override void Create()
    {
        fsr2Pass = new FSR2Pass
        {
            renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing
        };

        fsr2GenReactiveMaskPass = new FSR2GenerateReactiveMaskPass { FSR2Pass = fsr2Pass, renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing };
    }
}