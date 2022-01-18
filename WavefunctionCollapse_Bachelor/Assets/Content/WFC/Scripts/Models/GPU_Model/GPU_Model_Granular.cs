using System;
using System.Collections;
using UnityEngine;
using Random = Unity.Mathematics.Random;

namespace Models.GPU_Model
{
    public class GPU_Model_Granular : GPU_Model
    {
        public GPU_Model_Granular(
            ComputeShader observerShader,
            ComputeShader propagatorShader, 
            ComputeShader banShader,
            int width, int height, int patternSize, bool periodic) 
            : base(observerShader, propagatorShader, banShader, width, height, patternSize, periodic) 
        {}

        ~GPU_Model_Granular()
        {
            Dispose();
        }

        private class WFC_Objects
        {
            public Random random;
        }
    
        public override IEnumerator Run(uint seed, int limit, WFC_Result result)
        {
            observerParamsCopyBuffer[0].randomState = seed;
            if (waveCopyBuffer == null) Init();
            Clear();

            WFC_Objects objects = new WFC_Objects()
            {
                random = new Random(seed)
            };
        
            while (!result.finished)
            {
                if (propagatorSettings.debug == PropagatorSettings.DebugMode.None)
                {
                    Run_Internal(objects, result).MoveNext();
                }
                else
                {
                    yield return Run_Internal(objects, result);
                }
            }
        
            /* Preparing for next run. This should be done now before the user collapses tiles by hand for the next run. */
            ClearInBuffers();
        }

        private void Observe()
        {
            /*
         * Since we want to ban nodes in the in buffers we swap in and out buffers so that the out-buffer in the shader
         * (the one written to) is actually the in-buffer. This way we can leave only one of the buffers Read-Writeable
         */
            BindInOutBuffers(true);
        
            observerParamsBuf.SetData(observerParamsCopyBuffer);
            observerShader.Dispatch(0, 1, 1, 1);
        
            /* Swap back the in- and out-buffers so that they align with the correct socket for the propagation step. */
            BindInOutBuffers(true);
        
            _resultBuf.GetData(_resultCopyBuf);
            (openNodes, isPossible) = (Convert.ToBoolean(_resultCopyBuf[0].openNodes), Convert.ToBoolean(_resultCopyBuf[0].isPossible));
        }
    
        private IEnumerator Run_Internal(WFC_Objects objects, WFC_Result result)
        {
            Observe();
            if (openNodes)
            {
                // No need to observe here as observe shader already did that
                if (propagatorSettings.debug != PropagatorSettings.DebugMode.None)
                {
                    yield return DebugDrawCurrentState();
                }

                var propagation = Propagate(objects);
                if (propagatorSettings.debug == PropagatorSettings.DebugMode.None)
                {
                    propagation.MoveNext();
                }
                else
                {
                    while (propagation.MoveNext())
                    {
                        yield return propagation.Current;
                    }
                }

                if (!isPossible)
                {
                    Debug.Log("Impossible");
                    result.output = null;
                    result.success = false;
                    result.finished = true;
                }
            }
            else
            {
                result.output = WaveToOutput();
                result.success = true;
                result.finished = true;
            }
        }

    
        private IEnumerator Propagate(WFC_Objects objects)
        {
            while (openNodes && isPossible)
            {
                Result[] resultBufData = {new Result
                {
                    isPossible = Convert.ToUInt32(isPossible),
                    openNodes = Convert.ToUInt32(openNodes = false)
                }};
                _resultBuf.SetData(resultBufData);
            
                propagatorShader.Dispatch(
                    0,
                    (int) Math.Ceiling(width / 16.0f),
                    (int) Math.Ceiling(height / 16.0f),
                    1);

                /* Copy result of Compute operation back to CPU buffer. */
                _resultBuf.GetData(resultBufData);
                isPossible = Convert.ToBoolean(resultBufData[0].isPossible);
                openNodes = Convert.ToBoolean(resultBufData[0].openNodes);
            
                Debug.Log($"Open Cells: {openNodes}");

                /* Swap the in out buffers. */
                BindInOutBuffers(true);
                ClearOutBuffers();
            

                if (propagatorSettings.debug != PropagatorSettings.DebugMode.None)
                {
                    yield return DebugDrawCurrentState();
                }
            }
        }
    }
}
