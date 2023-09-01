using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using System.Runtime.InteropServices;
using System;

public class FSR2GenerateReactiveMaskPass : ScriptableRenderPass
{
    public FSR2GenerateReactiveMaskPass()
    {
    }
    private FSR2Pass fsr2Pass;
    public FSR2Pass FSR2Pass { set { fsr2Pass = value; } }
    public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
    {
        fsr2Pass.ExecuteForReactiveMask(context, ref renderingData);
    }
}
