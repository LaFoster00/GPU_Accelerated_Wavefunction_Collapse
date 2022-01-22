using System;
using System.Collections;
using UnityEngine;
using USCSL.Utils;
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
            observerParamsBuf.SetData(observerParamsCopyBuffer);
            
            if (waveCopyBuffer == null) Init();
            Clear();
            
            while (!result.finished)
            {
                if (propagatorSettings.debug == PropagatorSettings.DebugMode.None)
                {
                    Run_Internal(result).MoveNext();
                }
                else
                {
                    yield return Run_Internal(result);
                }
            }
        
            /* Preparing for next run. This should be done now before the user collapses tiles by hand for the next run. */
            ClearInBuffers();
        }

        private void Observe()
        {
            var timer = new CodeTimer_Average(true, true, true, "Observe_Granular", Debug.Log);
            /*
             * Since we want to ban nodes in the in buffers we swap in and out buffers so that the out-buffer in the shader
             * (the one written to) is actually the in-buffer. This way we can leave only one of the buffers Read-Writeable
             */
            BindInOutBuffers(true);
            
            observerShader.Dispatch(0, 1, 1, 1);
        
            /* Swap back the in- and out-buffers so that they align with the correct socket for the propagation step. */
            BindInOutBuffers(true);

            _resultBuf.GetData(_resultCopyBuf);
            (openNodes, isPossible) = (Convert.ToBoolean(_resultCopyBuf[0].openNodes), Convert.ToBoolean(_resultCopyBuf[0].isPossible));
            
            timer.Stop(false);
        }
    
        private IEnumerator Run_Internal(WFC_Result result)
        {
            Observe();
            if (openNodes)
            {
                // No need to observe here as observe shader already did that
                if (propagatorSettings.debug != PropagatorSettings.DebugMode.None)
                {
                    yield return DebugDrawCurrentState();
                }

                var propagation = Propagate();
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

    
        private IEnumerator Propagate()
        {
            var timer = new CodeTimer_Average(true, true, true, "Propagate_Granular", Debug.Log);
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
                    (int) Math.Ceiling((float)width / PropagationThreadGroupSizeX),
                    (int) Math.Ceiling((float)height / PropagationThreadGroupSizeY),
                    1);

                /* Copy result of Compute operation back to CPU buffer. */
                _resultBuf.GetData(resultBufData);
                isPossible = Convert.ToBoolean(resultBufData[0].isPossible);
                openNodes = Convert.ToBoolean(resultBufData[0].openNodes);

                /* Swap the in out buffers. */
                BindInOutBuffers(true);
                ClearOutBuffers();


                if (propagatorSettings.debug != PropagatorSettings.DebugMode.None)
                {
                    yield return DebugDrawCurrentState();
                }
            }
            
            timer.Stop(false);
        }
    }
}
