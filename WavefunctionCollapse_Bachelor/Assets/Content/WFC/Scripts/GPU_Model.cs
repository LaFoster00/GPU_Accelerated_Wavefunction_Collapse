using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

public class GPU_Model : MonoBehaviour
{
    private ComputeShader computeShader;
    
    
    [StructLayout(LayoutKind.Sequential)]
    private struct PropagatorResults
    {
        public uint openCells;
        public bool isPossible;
    }

    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        Texture2DArray texture2DArray =
            new Texture2DArray(10, 10, 10, GraphicsFormat.R8_SInt ,TextureCreationFlags.None, 0)
            {
                filterMode = FilterMode.Point,
                
            };

        Texture2D texture2D = new Texture2D(10, 10, GraphicsFormat.R32_SFloat, TextureCreationFlags.None);

        Texture3D texture3D = new Texture3D(10, 10, 10, GraphicsFormat.R32_UInt, TextureCreationFlags.None, 0); 

        PropagatorResults[] propagatorResults = {new PropagatorResults()};
        ComputeBuffer propagatorResultBuffer = new ComputeBuffer(1, 4, ComputeBufferType.Structured);
        propagatorResultBuffer.SetData(propagatorResults);
        computeShader.SetBuffer(0, "propagator_result", propagatorResultBuffer);

        propagatorResultBuffer.GetData(propagatorResults);
    }
}
