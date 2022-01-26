using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Models.CPU_Model;
using Unity.Mathematics;
using UnityEngine;
using USCSL.Utils;
using USCSL.Utils.Shader;
using WFC;
using Random = Unity.Mathematics.Random;

namespace Models.GPU_Model
{
    public class GPU_Model_Naive : GPU_Model
    {
        public GPU_Model_Naive(
            ComputeShader observeShader,
            ComputeShader propagatorShader,
            ComputeShader banShader,
            int width, int height, int patternSize, bool periodic) :
            base(observeShader, propagatorShader, banShader, width, height, patternSize, periodic)
        {

        }

        ~GPU_Model_Naive()
        {
            Dispose();
        }

        protected override void BindResources()
        {
            propagatorShader.SetInt("nb_patterns", nbPatterns);
            propagatorShader.SetInt("width", width);
            propagatorShader.SetInt("height", height);
            propagatorShader.SetBool("is_periodic", periodic);

            propagatorShader.SetBuffer(0, "weighting", weightBuf);
            propagatorShader.SetBuffer(0, "memoisation", memoisationBuf);
            propagatorShader.SetBuffer(0, "propagator", propagatorBuf);
            propagatorShader.SetBuffer(0, "result", _resultBuf);

            BindInOutBuffers(false);
        }

        public class WFC_Objects
        {
            public Random random;
        }

        public override IEnumerator Run(uint seed, int limit, WFC_Result result)
        {
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

            if (!isPossible)
            {
                yield return DebugDrawCurrentState();
            }

            /* Preparing for next run. This should be done now before the user collapses tiles by hand for the next run. */
            ClearInBuffers();
        }

        private int NextUnobservedNode(Random random)
        {
            float min = float.MaxValue;
            int argmin = -1;
            for (int node = 0; node < nbNodes; node++)
            {
                if (!periodic && (node % width + patternSize > width || node / width + patternSize > height)) continue;
                int numPossiblePatterns = memoisationCopyBuffer[node].num_possible_patterns;
                float entropy = memoisationCopyBuffer[node].entropies;
                if (numPossiblePatterns > 1 && entropy <= min)
                {
                    float noise = (float)1E-6 * random.NextFloat();
                    if (entropy + noise < min)
                    {
                        min = entropy + noise;
                        argmin = node;
                    }
                }
            }

            return argmin;
        }
        
        private IEnumerator Run_Internal(WFC_Objects objects, WFC_Result result)
        {
            /* Copy back memoisation data. It is needed to find the next free node. */
            CopyGpuMemoisationToCpu();
            int node = NextUnobservedNode(objects.random);
            if (node >= 0)
            {
                Observe(node, ref objects.random);
                if (!isPossible) Debug.Log("Failed after observe.");
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
                    yield return propagation;
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


        private IEnumerator Propagate(WFC_Objects objects)
        {
            var timer = new CodeTimer_Average(true, true, true, "Propagate_Naive", Debug.Log);
            while (openNodes && isPossible)
            {
                Result[] resultBufData =
                {
                    new Result
                    {
                        isPossible = isPossible,
                        openNodes = openNodes = false
                    }
                };
                _resultBuf.SetData(resultBufData);

                propagatorShader.Dispatch(
                    0,
                    (int) Math.Ceiling((float) width / PropagationThreadGroupSizeX),
                    (int) Math.Ceiling((float) height / PropagationThreadGroupSizeY),
                    1);

                /* Copy result of Compute operation back to CPU buffer. */
                _resultBuf.GetData(resultBufData);
                isPossible = resultBufData[0].isPossible;
                openNodes = resultBufData[0].openNodes;

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
        
        int RandomFromDistribution(float[] distribution, float threshold)
        {
            float sum = distribution.Sum();
            
            for (int p2 = 0; p2 < nbPatterns; p2++)
            {
                distribution[p2] /= sum;
            }
        
            float x = 0;
        
            for (int p3 = 0; p3 < distribution.Length; p3++)
            {
                x += distribution[p3];
                if (threshold <= x) return p3;
            }
        
            return 0;
        }
        
        protected void Observe(int node, ref Random random)
        {
            var timer = new CodeTimer_Average(true, true, true, "Observe_Naive", Debug.Log);

            FillBanCopyBuffers();

            float[] distribution = new float[nbPatterns];
            for (int p1 = 0; p1 < nbPatterns; p1++)
            {
                distribution[p1] = waveCopyBuffer[node * nbPatterns + p1] == true ? weightingCopyBuffer[p1].weight : 0.0f;
            }

            int r = RandomFromDistribution(distribution, random.NextFloat());
            for (int p2 = 0; p2 < nbPatterns; p2++)
            {
                if ((waveCopyBuffer[node * nbPatterns + p2] == true) != (p2 == r))
                {
                    Ban(node, p2);
                }
            }
            
            ApplyBanCopyBuffers();

            timer.Stop(false);
        }

        private void FillBanCopyBuffers()
        {
            CopyGpuWaveToCpu();
            CopyGpuMemoisationToCpu();
            CopyGpuCollapseToCpu();
        }

        /// <summary>
        /// Call FillBanCopyBuffers() before using this function first time after a propagation iteration.
        /// This call will Ban a pattern from the specified node, but WONT upload those changes to the GPU.
        /// See BanAndApply() or ApplyBanTextures()
        /// </summary>
        /// <param name="node"></param>
        /// <param name="pattern"></param>
        public override void Ban(int node, int pattern)
        {
            waveCopyBuffer[node * nbPatterns + pattern] = false;

            memoisationCopyBuffer[node].num_possible_patterns  -= 1;
            memoisationCopyBuffer[node].sums_of_weights -= weightingCopyBuffer[pattern].weight;
            memoisationCopyBuffer[node].sums_of_weight_log_weights -= weightingCopyBuffer[pattern].logWeight;

            float sum = memoisationCopyBuffer[node].sums_of_weights;
            float sumLog = math.log(sum);
            float sumsLogDivideSum = memoisationCopyBuffer[node].sums_of_weight_log_weights / sum;
            memoisationCopyBuffer[node].entropies = sumLog - sumsLogDivideSum;
            if (memoisationCopyBuffer[node].num_possible_patterns <= 0)
                isPossible = false;

            /* Update the collapse information for the compute shader. */
            collapseCopyBuffer[node].is_collapsed = true;

            int x = node % width;
            int y = node / width;
            for (int dir = 0; dir < 4; dir++)
            {
                int x2 = x + Directions.DirectionsX[dir];
                int y2 = y + Directions.DirectionsY[dir];
                if (periodic)
                {
                    x2 = (x2 + width) % width;
                    y2 = (y2 + height) % height;
                }
                else if (!periodic && (x2 < 0
                                       || y2 < 0
                                       || x2 + patternSize > width
                                       || y2 + patternSize > height))
                {
                    continue;
                }

                int node2 = x2 + y2 * width;
                collapseCopyBuffer[node2].needs_collapse = true;
            }

            openNodes = true;
        }

        /// <summary>
        /// This NEEDS to be called AFTER Ban in order to upload all texture changes to the GPU.
        /// Making this a separate call gives the option to first ban all wanted patterns and then upload the changes
        /// in one go. This should be much quicker.
        /// </summary>
        private void ApplyBanCopyBuffers()
        {
            waveInBuf.SetData(waveCopyBuffer);
            memoisationBuf.SetData(memoisationCopyBuffer);
            inCollapseBuf.SetData(collapseCopyBuffer);

            Result[] resultBufData =
            {
                new Result
                {
                    isPossible = isPossible, openNodes = openNodes
                }
            };
            _resultBuf.SetData(resultBufData);
        }

        private void CopyGpuMemoisationToCpu()
        {
            memoisationBuf.GetData(memoisationCopyBuffer);
        }
    }
}
