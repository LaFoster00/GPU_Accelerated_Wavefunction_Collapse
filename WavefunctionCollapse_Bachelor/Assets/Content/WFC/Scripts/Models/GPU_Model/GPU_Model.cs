using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using UnityEngine;
using WFC;

namespace Models.GPU_Model
{
    public abstract class GPU_Model : Model, IDisposable
    {
        protected readonly ComputeShader observerShader;
        protected readonly ComputeShader propagatorShader;
        protected readonly ComputeShader banShader;
    
        #region CommonShaderResources

        protected uint[] waveCopyBuffer;
        /*
    Actual wave result
    wave(node, pattern)
    */
        protected ComputeBuffer waveBuf;

        protected struct Weighting
        {
            public float weight;
            public float logWeight;
            public float distribution;
            public float padding;
        }
        protected  Weighting[] weightingCopyBuffer;
        protected  ComputeBuffer weightBuf;

        [StructLayout(LayoutKind.Sequential)]
        protected struct Memoisation
        {
            public float sums_of_weights;
            public float sums_of_weight_log_weights;
            public float entropies;
            public int num_possible_patterns;
        }

        protected  Memoisation[] memoisationCopyBuffer;
        protected  readonly ComputeBuffer memoisationBuf;

        /* propagator[uint3(pattern, otherPattern, direction)] */
        [StructLayout(LayoutKind.Sequential)]
        protected struct Propagator
        {
            /* Array inside GPU struct */
            public uint propagatorDown;
            public uint propagatorLeft;
            public uint propagatorRight;
            public uint propagatorUp;

        }
        protected  Propagator[] propagatorCopyBuffer;
        protected  ComputeBuffer propagatorBuf;

        protected struct Result
        {
            public uint isPossible;
            public uint openNodes;
            public uint finished;
            public uint padding; 
        }
        protected  readonly Result[] _resultCopyBuf = new Result[1];
        protected  readonly ComputeBuffer _resultBuf = new ComputeBuffer(1, sizeof(uint) * 4); // Change this to structured buffer

        [StructLayout(LayoutKind.Sequential)]
        protected struct Collapse
        {
            public uint is_collapsed;
            public uint needs_collapse;
        }
        protected  Collapse[] collapseClearData;
        protected  Collapse[] collapseCopyBuffer;
    
        /* Cells in which the patterns changed. */
        protected  ComputeBuffer inCollapseBuf;
        protected  ComputeBuffer outCollapseBuf;

        protected  bool openNodes = true;
        #endregion

        #region ObserverShaderResources

        [StructLayout(LayoutKind.Sequential)]
        protected struct ObserverParams
        {
            public uint randomState;
        }
        protected  ObserverParams[] observerParamsCopyBuffer = new ObserverParams[1];
        protected  ComputeBuffer observerParamsBuf = new ComputeBuffer(1, sizeof(uint));

        #endregion

        #region BanShaderResources

        protected struct BanParams
        {
            public int node;
            public int pattern;
        }
        protected  BanParams[] banParamsCopyBuffer = new BanParams[1];
        protected  ComputeBuffer banParamsBuf = new ComputeBuffer(1, sizeof(int) * 2);

        #endregion

        public GPU_Model(   
            ComputeShader observerShader,
            ComputeShader propagatorShader,
            ComputeShader banShader, 
            int width, int height, int patternSize, bool periodic) 
            : base(width, height, patternSize, periodic)
        {
            this.observerShader = observerShader;
            this.propagatorShader = propagatorShader;
            this.banShader = banShader;

            memoisationBuf = new ComputeBuffer(width * height, sizeof(float) * 3 + sizeof(int));
            inCollapseBuf = new ComputeBuffer(width * height, sizeof(uint) * 2);
            outCollapseBuf = new ComputeBuffer(width * height, sizeof(uint) * 2);
        }

        public override void SetData(int nbPatterns, double[] weights, (bool[][][] dense, int[][][] standard) propagator,
            PropagatorSettings propagatorSettings)
        {
            base.SetData(nbPatterns, weights, propagator, propagatorSettings);
        
            waveBuf = new ComputeBuffer(width * height * nbPatterns, sizeof(uint));
            propagatorBuf = new ComputeBuffer(nbPatterns * nbPatterns, sizeof(uint) * 4);
            weightBuf = new ComputeBuffer(weights.Length, sizeof(float) * 4);

            {
                propagatorCopyBuffer = new Propagator[nbPatterns * nbPatterns];
                Parallel.For(0, nbPatterns, pattern =>
                {
                    int patternOffset = pattern * nbPatterns;
                    for (int otherPattern = 0; otherPattern < nbPatterns; otherPattern++)
                    {
                        propagatorCopyBuffer[patternOffset + otherPattern].propagatorDown = Convert.ToUInt32(densePropagator[pattern][0][otherPattern]);
                        propagatorCopyBuffer[patternOffset + otherPattern].propagatorLeft = Convert.ToUInt32(densePropagator[pattern][1][otherPattern]);
                        propagatorCopyBuffer[patternOffset + otherPattern].propagatorRight = Convert.ToUInt32(densePropagator[pattern][2][otherPattern]);
                        propagatorCopyBuffer[patternOffset + otherPattern].propagatorUp = Convert.ToUInt32(densePropagator[pattern][3][otherPattern]);
                    }
                });
                propagatorBuf.SetData(propagatorCopyBuffer);
            }
        }
    
        protected override void Init()
        {
            waveCopyBuffer = new uint[width * height * nbPatterns];
            weightingCopyBuffer = new Weighting[nbPatterns];

            for (int pattern = 0; pattern < nbPatterns; pattern++)
            {
                weightingCopyBuffer[pattern].weight = (float) weights[pattern];
                weightingCopyBuffer[pattern].logWeight = (float) (weights[pattern] * Math.Log(weights[pattern]));
                totalSumOfWeights += weights[pattern];
                totalSumOfWeightLogWeights += weightingCopyBuffer[pattern].logWeight;
            }
            weightBuf.SetData(weightingCopyBuffer);

            startingEntropy = Math.Log(totalSumOfWeights) - totalSumOfWeightLogWeights / totalSumOfWeights;

            collapseClearData = new Collapse[height * width];
            collapseCopyBuffer = new Collapse[height * width];
        
            memoisationCopyBuffer = new Memoisation[width * height];
        
            ClearInBuffers();
            BindResources();
        }

        protected virtual void Swap()
        {
            USCSL.Extensions.Swap(ref inCollapseBuf, ref outCollapseBuf);
        }
        
        protected virtual void BindInOutBuffers(bool swap)
        {
            if (swap)
            {
                Swap();
            }

            propagatorShader.SetBuffer(0, "in_collapse", inCollapseBuf);
            propagatorShader.SetBuffer(0, "out_collapse", outCollapseBuf);

            observerShader.SetBuffer(0, "in_collapse", inCollapseBuf);
            observerShader.SetBuffer(0, "out_collapse", outCollapseBuf);
        
            banShader.SetBuffer(0, "in_collapse", inCollapseBuf);
            banShader.SetBuffer(0, "out_collapse", outCollapseBuf);
        }
    
        protected virtual void BindResources()
        {
            propagatorShader.SetInt("nb_patterns", nbPatterns);
            propagatorShader.SetInt("width", width);
            propagatorShader.SetInt("height", height);
            propagatorShader.SetBool("is_periodic", periodic);
        
            propagatorShader.SetBuffer(0, "wave_data", waveBuf);
            propagatorShader.SetBuffer(0, "weighting", weightBuf);
            propagatorShader.SetBuffer(0, "memoisation", memoisationBuf);
            propagatorShader.SetBuffer(0, "propagator", propagatorBuf);
            propagatorShader.SetBuffer(0, "result", _resultBuf);
        
            observerShader.SetInt("nb_patterns", nbPatterns);
            observerShader.SetInt("width", width);
            observerShader.SetInt("height", height);
            observerShader.SetBool("is_periodic", periodic);
        
            observerShader.SetBuffer(0, "wave_data", waveBuf);
            observerShader.SetBuffer(0, "weighting", weightBuf);
            observerShader.SetBuffer(0, "memoisation", memoisationBuf);
            observerShader.SetBuffer(0, "propagator", propagatorBuf);
            observerShader.SetBuffer(0, "result", _resultBuf);
            observerShader.SetBuffer(0, "observer_params", observerParamsBuf);
        
            banShader.SetInt("nb_patterns", nbPatterns);
            banShader.SetInt("width", width);
            banShader.SetInt("height", height);
            banShader.SetBool("is_periodic", periodic);
        
            banShader.SetBuffer(0, "wave_data", waveBuf);
            banShader.SetBuffer(0, "weighting", weightBuf);
            banShader.SetBuffer(0, "memoisation", memoisationBuf);
            banShader.SetBuffer(0, "propagator", propagatorBuf);
            banShader.SetBuffer(0, "result", _resultBuf);
            banShader.SetBuffer(0, "ban_params", _resultBuf);
        
            BindInOutBuffers(false);
        }
    
        protected override void Clear()
        {
            base.Clear();
        
            Result[] resultBufData = {new Result
            {
                isPossible = Convert.ToUInt32(isPossible),
                openNodes = Convert.ToUInt32(openNodes)
            }};
            _resultBuf.SetData(resultBufData);

            Parallel.For(0, nbNodes, node =>
            {
                for (int pattern = 0; pattern < nbPatterns; pattern++)
                {
                    waveCopyBuffer[node * nbPatterns + pattern] = Convert.ToUInt32(true);
                }

                memoisationCopyBuffer[node].num_possible_patterns = nbPatterns;
                memoisationCopyBuffer[node].sums_of_weights = (float) totalSumOfWeights;
                memoisationCopyBuffer[node].sums_of_weight_log_weights = (float) totalSumOfWeightLogWeights;
                memoisationCopyBuffer[node].entropies = (float) startingEntropy;
            });

            waveBuf.SetData(waveCopyBuffer);
            memoisationBuf.SetData(memoisationCopyBuffer);

            ClearOutBuffers();
        }

        protected void ClearOutBuffers()
        {
            outCollapseBuf.SetData(collapseClearData);
        }

        /// <summary>
        /// This should be called after the algorithm finishes as it will be the starting point for following runs!
        /// </summary>
        protected void ClearInBuffers()
        {
            inCollapseBuf.SetData(collapseClearData);
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
            (openNodes, isPossible) = (Convert.ToBoolean(_resultCopyBuf[0].openNodes), Convert.ToBoolean(_resultCopyBuf[0].isPossible));
        }
    
        protected virtual bool[][] CopyGpuWaveToCpu()
        { 
            waveBuf.GetData(waveCopyBuffer);
            bool[][] wave = new bool[nbNodes][];
        
            Parallel.For(0, nbNodes, node =>
            {
                wave[node] = new bool[nbPatterns];
                for (int pattern = 0; pattern < nbPatterns; pattern++)
                {
                    wave[node][pattern] = Convert.ToBoolean(waveCopyBuffer[node * nbPatterns + pattern]);
                }
            });
            return wave;
        }

        protected void CopyGpuCollapseToCpu()
        {
            inCollapseBuf.GetData(collapseCopyBuffer);
        }

        /*
         Transform the wave to a valid output (a 2d array of patterns that aren't in
         contradiction). This function should be used only when all cell of the wave
         are defined.
        */
        protected virtual int[,] WaveToOutput()
        {
            bool[][] wave = CopyGpuWaveToCpu();
            int[,] outputPatterns = new int[height, width];
            Parallel.For(0, wave.Length, node =>
            {
                for (int pattern = 0; pattern < nbPatterns; pattern++)
                {
                    if (wave[node][pattern])
                    {
                        int x = node % width;
                        int y = node / width;
                        outputPatterns[y, x] = pattern;
                        break;
                    }
                }
            });

            return outputPatterns;
        }

        protected virtual IEnumerator DebugDrawCurrentState()
        {
            bool[][] wave = CopyGpuWaveToCpu();
            CopyGpuCollapseToCpu();
            List<(int, int)> propagatingCells = new List<(int, int)>();
            for (int node = 0; node < nbNodes; node++)
            {
                if (Convert.ToBoolean(collapseCopyBuffer[node].needs_collapse))
                {
                    propagatingCells.Add((node, 0));
                }
            }

            stepInfo.numPropagatingCells = propagatingCells.Count;
            stepInfo.propagatingCells = propagatingCells.ToArray();
            propagatorSettings.debugToOutput(stepInfo, wave, propagatorSettings.orientedToTileId);
            yield return propagatorSettings.stepInterval == 0
                ? null
                : new WaitForSeconds(propagatorSettings.stepInterval);
        }
    
        public virtual void Dispose()
        {
            weightBuf?.Release();
            _resultBuf?.Release();
            waveBuf?.Release();
            memoisationBuf?.Release();
            propagatorBuf?.Release();
            inCollapseBuf?.Release();
            outCollapseBuf?.Release();
            banParamsBuf?.Release();
            observerParamsBuf?.Release();
        }
    }
}
