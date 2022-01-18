using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace Models.GPU_Model
{
    public class GPU_Model_ComputeBuffer : GPU_Model
    {
        private readonly ComputeShader _clearOutBuffersShader;
        private readonly ComputeShader _resetOpenNodesShader;

        private readonly int _propagationIterations;
        private readonly int _totalIterations;

        private Action<CommandBuffer> scheduleObservationIteration;
        private Action<CommandBuffer> schedulePropagationDoubleIteration;

        public GPU_Model_ComputeBuffer(
            ComputeShader observerShader,
            ComputeShader propagatorShader,
            ComputeShader banShader,
            ComputeShader clearOutBuffersShader,
            ComputeShader resetOpenNodesShader,
            int propagationIterations, int totalIterations,
            int width, int height, int patternSize, bool periodic) : base(observerShader, propagatorShader, banShader,
            width, height, patternSize, periodic)
        {
            _clearOutBuffersShader = clearOutBuffersShader;
            _resetOpenNodesShader = resetOpenNodesShader;

            _propagationIterations = propagationIterations;
            _totalIterations = totalIterations;

            /*
             *  Prepare command buffer creation for later
             */
            scheduleObservationIteration = (commandBuffer) =>
            {
                BindInOutBuffers(commandBuffer, true);
                commandBuffer.DispatchCompute(observerShader, 0, 1, 1, 1);
                BindInOutBuffers(commandBuffer, true);
            };

            schedulePropagationDoubleIteration = (commandBuffer) =>
            {
                //Fill command buffer two times so that the in- and out-buffers are the same as before (double swap)
                for (int i = 0; i < 2; i++)
                {
                    commandBuffer.DispatchCompute(_resetOpenNodesShader,
                        0,
                        1,
                        1,
                        1);

                    commandBuffer.DispatchCompute(propagatorShader,
                        0,
                        (int) Math.Ceiling(width / 4.0f),
                        (int) Math.Ceiling(height / 4.0f),
                        1);

                    BindInOutBuffers(commandBuffer, true);
                    ClearOutBuffers(commandBuffer);
                }
            };
        }

        ~GPU_Model_ComputeBuffer()
        {
            Dispose();
        }

        protected override void BindInOutBuffers(bool swap)
        {
            base.BindInOutBuffers(swap);
        
            _clearOutBuffersShader.SetBuffer(0, "in_collapse", inCollapseBuf);
            _clearOutBuffersShader.SetBuffer(0, "out_collapse", outCollapseBuf);
        }

        private void BindInOutBuffers(CommandBuffer commandBuffer, bool swap)
        {
            if (swap)
            {
                Swap();
            }
            
            commandBuffer.SetComputeBufferParam(propagatorShader, 0, "in_collapse", inCollapseBuf);
            commandBuffer.SetComputeBufferParam(propagatorShader, 0, "out_collapse", outCollapseBuf);
            
            commandBuffer.SetComputeBufferParam(observerShader, 0, "in_collapse", inCollapseBuf);
            commandBuffer.SetComputeBufferParam(observerShader, 0, "out_collapse", outCollapseBuf);
            
            commandBuffer.SetComputeBufferParam(banShader, 0, "in_collapse", inCollapseBuf);
            commandBuffer.SetComputeBufferParam(banShader, 0, "out_collapse", outCollapseBuf);
            
            commandBuffer.SetComputeBufferParam(_clearOutBuffersShader, 0, "in_collapse", inCollapseBuf);
            commandBuffer.SetComputeBufferParam(_clearOutBuffersShader, 0, "out_collapse", outCollapseBuf);
        }

        protected override void BindResources()
        {
            base.BindResources();

            _clearOutBuffersShader.SetInt("width", width);
            _clearOutBuffersShader.SetInt("height", height);
            _clearOutBuffersShader.SetBuffer(0, "out_collapse", outCollapseBuf);
        
            _resetOpenNodesShader.SetBuffer(0, "result", _resultBuf);
        
            BindInOutBuffers(false);
        }

        private void ClearOutBuffers(CommandBuffer commandBuffer)
        {
            commandBuffer.DispatchCompute(_clearOutBuffersShader,
                0,
                (int) Math.Ceiling(width / 32.0f),
                (int) Math.Ceiling(height / 32.0f),
                1);
        }

        private double _totalRunTime = 0;
        public override IEnumerator Run(uint seed, int limit, WFC_Result result)
        {
            double startTime = Time.realtimeSinceStartupAsDouble;

            if (waveCopyBuffer == null) Init();
            Clear();
        
            observerParamsCopyBuffer[0].randomState = seed;
            observerParamsBuf.SetData(observerParamsCopyBuffer);
        
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
        
            double executionTime = Time.realtimeSinceStartupAsDouble - startTime;
            _totalRunTime += executionTime;
            Debug.Log($"Run took {executionTime} sec and {_totalRunTime} sec in total.");
        }

        private IEnumerator Run_Internal(WFC_Result result)
        {
            var propagation = Propagate(result);
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
        }

    
        private IEnumerator Propagate(WFC_Result result)
        {
            /*
         Get data acts as a sort of memory barrier ensuring all previous computation executed
         before this step.
         */
            _resultBuf.GetData(_resultCopyBuf);
        
            bool finished = false;
            Result[] resultBufData =
            {
                new Result
                {
                    isPossible = Convert.ToUInt32(isPossible),
                    openNodes = Convert.ToUInt32(false),
                    finished =  Convert.ToUInt32(false)
                }
            };
            _resultBuf.SetData(resultBufData);

            while (!finished && isPossible)
            {
                if (propagatorSettings.debug == PropagatorSettings.DebugMode.OnSet)
                {
                    for (int i = 0; i < _totalIterations; i++)
                    {
                        {
                            CommandBuffer observationBuffer = new CommandBuffer();
                            scheduleObservationIteration(observationBuffer);
                            Graphics.ExecuteCommandBuffer(observationBuffer);
                        }

                        yield return DebugDrawCurrentState();

                        for (int y = 0; y < _propagationIterations; y++)
                        {
                            CommandBuffer propagationBuffer = new CommandBuffer();
                            schedulePropagationDoubleIteration(propagationBuffer);
                            Graphics.ExecuteCommandBuffer(propagationBuffer);
                            yield return DebugDrawCurrentState();
                        }
                    }
                }
                else
                {
                    CommandBuffer wholeStepIterationBuffer = new CommandBuffer();
                    for (int i = 0; i < _totalIterations; i++)
                    {
                        scheduleObservationIteration(wholeStepIterationBuffer);
                        for (int y = 0; y < _propagationIterations; y++)
                        {
                            schedulePropagationDoubleIteration(wholeStepIterationBuffer);
                        }
                    }
                    Graphics.ExecuteCommandBuffer(wholeStepIterationBuffer);
                }

                if (propagatorSettings.debug == PropagatorSettings.DebugMode.OnChange)
                {
                    yield return DebugDrawCurrentState();
                }
            
                _resultBuf.GetData(_resultCopyBuf);
                isPossible = Convert.ToBoolean(_resultCopyBuf[0].isPossible);
                finished = Convert.ToBoolean(_resultCopyBuf[0].finished);
            }
        
            if (isPossible)
            {
                result.output = WaveToOutput();
                result.success = true;
                result.finished = true;
            }
            else
            {
            
                Debug.Log("Impossible");
                result.output = null;
                result.success = false;
                result.finished = true;
            }
        }

        public override void Ban(int node, int pattern)
        {
            /*
             * Since we want to ban nodes in the in buffers we swap in and out buffers so that the out-buffer in the shader
             * (the one written to) is actually the in-buffer. This way we can leave only one of the buffers Read-Writeable
             */
            BindInOutBuffers(true);

            banParamsCopyBuffer[0].node = node;
            banParamsCopyBuffer[0].pattern = pattern;
            banParamsBuf.SetData(banParamsCopyBuffer);
            observerShader.Dispatch(0, 1, 1, 1);
        
            /* Swap back the in- and out-buffers so that they align with the correct socket for the propagation step. */
            BindInOutBuffers(true);
        
            _resultBuf.GetData(_resultCopyBuf);
            (isPossible) = Convert.ToBoolean(_resultCopyBuf[0].isPossible);
        }

        public override void Dispose()
        {
            base.Dispose();
            
        }
    }
}
