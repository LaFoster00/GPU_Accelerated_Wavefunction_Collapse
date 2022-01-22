using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Models.CPU_Model;
using UnityEngine;
using USCSL.Utils;
using WFC;
using Random = Unity.Mathematics.Random;

namespace Models.GPU_Model
{
    public class GPU_Model_Naive : CPU_Model_Sequential, IDisposable
    {
        private readonly ComputeShader _propagatorShader;

        #region ShaderResources

        private uint[] _waveCopyBuffer;
        /*
         Actual wave result
         wave(node, pattern)
         */
        private ComputeBuffer _waveInBuf;
        private ComputeBuffer _waveOutBuf;

        struct Weighting
        {
            public float weight;
            public float logWeight;
            public float distribution;
            public float padding;
        }

        private Weighting[] _weightCopyBuffer;
        private ComputeBuffer _weightBuf;

        [StructLayout(LayoutKind.Sequential)]
        struct Memoisation
        {
            public float sums_of_weights;
            public float sums_of_weight_log_weights;
            public float entropies;
            public int num_possible_patterns;
        }

        private readonly Memoisation[] _memoisationCopyBuffer;
        private readonly ComputeBuffer _memoisationBuf;

        /* propagator[uint3(pattern, otherPattern, direction)] */
        [StructLayout(LayoutKind.Sequential)]
        struct Propagator
        {
            /* Array inside GPU struct */
            public uint propagatorDown;
            public uint propagatorLeft;
            public uint propagatorRight;
            public uint propagatorUp;

        }
        private Propagator[] _propagatorCopyBuffer;
        private ComputeBuffer _propagatorBuf;

        struct Result
        {
            public uint isPossible;
            public uint openNodes;
        }
        private readonly ComputeBuffer _resultBuf; // Change this to structured buffer

        [StructLayout(LayoutKind.Sequential)]
        struct Collapse
        {
            public uint is_collapsed;
            public uint needs_collapse;
        }
        private readonly Collapse[] _collapseClearData;
        private readonly Collapse[] _collapseCopyBuffer;

        /* Cells in which the patterns changed. */
        private ComputeBuffer _inCollapseBuf;
        private ComputeBuffer _outCollapseBuf;

        private bool _openCells = true;
        #endregion

        public GPU_Model_Naive(
            ComputeShader propagatorShader,
            int width, int height, int patternSize, bool periodic) :
            base(width, height, patternSize, periodic)
        {
            _propagatorShader = propagatorShader;

            _memoisationBuf = new ComputeBuffer(width * height, sizeof(float) * 3 + sizeof(int));
            _memoisationCopyBuffer = new Memoisation[width * height];

            _inCollapseBuf = new ComputeBuffer(width * height, sizeof(uint) * 2);
            _outCollapseBuf = new ComputeBuffer(width * height, sizeof(uint) * 2);
            _collapseClearData = new Collapse[height * width];
            _collapseCopyBuffer = new Collapse[height * width];

            _resultBuf = new ComputeBuffer(1, sizeof(int) * 2);
        }

        public override void SetData(int nbPatterns, double[] weights, (bool[][][] dense, int[][][] standard) propagator,
            PropagatorSettings propagatorSettings)
        {
            base.SetData(nbPatterns, weights, propagator, propagatorSettings);

            _waveInBuf = new ComputeBuffer(width * height * nbPatterns, sizeof(uint));
            _waveOutBuf = new ComputeBuffer(width * height * nbPatterns, sizeof(uint));
            _waveCopyBuffer = new uint[width * height * nbPatterns];
            
            _propagatorBuf = new ComputeBuffer(nbPatterns * nbPatterns, sizeof(uint) * 4);
            _weightBuf = new ComputeBuffer(weights.Length, sizeof(float) * 4);
            _weightCopyBuffer = new Weighting[weights.Length];

            ClearInBuffers();
            ClearInOutBuffers();

            {
                _propagatorCopyBuffer = new Propagator[nbPatterns * nbPatterns];
                Parallel.For(0, nbPatterns, pattern =>
                {
                    int patternOffset = pattern * nbPatterns;
                    for (int otherPattern = 0; otherPattern < nbPatterns; otherPattern++)
                    {
                        _propagatorCopyBuffer[patternOffset + otherPattern].propagatorDown = Convert.ToUInt32(densePropagator[pattern][0][otherPattern]);
                        _propagatorCopyBuffer[patternOffset + otherPattern].propagatorLeft = Convert.ToUInt32(densePropagator[pattern][1][otherPattern]);
                        _propagatorCopyBuffer[patternOffset + otherPattern].propagatorRight = Convert.ToUInt32(densePropagator[pattern][2][otherPattern]);
                        _propagatorCopyBuffer[patternOffset + otherPattern].propagatorUp = Convert.ToUInt32(densePropagator[pattern][3][otherPattern]);
                    }
                });
                _propagatorBuf.SetData(_propagatorCopyBuffer);
            }
        }

        ~GPU_Model_Naive()
        {
            Dispose();
        }

        protected override void Init()
        {
            base.Init();

            Result[] resultBufData = {new Result
            {
                isPossible = Convert.ToUInt32(isPossible),
                openNodes = Convert.ToUInt32(_openCells)
            }};
            _resultBuf.SetData(resultBufData);

            BindResources();
        }

        private void BindInOutBuffers(bool swap)
        {
            if (swap)
            {
                USCSL.Extensions.Swap(ref _inCollapseBuf, ref _outCollapseBuf);
                USCSL.Extensions.Swap(ref _waveInBuf, ref _waveOutBuf);
            }

            _propagatorShader.SetBuffer(0, "in_collapse", _inCollapseBuf);
            _propagatorShader.SetBuffer(0, "out_collapse", _outCollapseBuf);
            _propagatorShader.SetBuffer(0, "wave_in", _waveInBuf);
            _propagatorShader.SetBuffer(0, "wave_out", _waveOutBuf);
        }

        private void BindResources()
        {
            _propagatorShader.SetInt("nb_patterns", nbPatterns);
            _propagatorShader.SetInt("width", width);
            _propagatorShader.SetInt("height", height);
            _propagatorShader.SetBool("is_periodic", periodic);

            _propagatorShader.SetBuffer(0, "wave_in", _waveInBuf);
            _propagatorShader.SetBuffer(0, "wave_out", _waveOutBuf);
            _propagatorShader.SetBuffer(0, "weighting", _weightBuf);
            _propagatorShader.SetBuffer(0, "memoisation", _memoisationBuf);
            _propagatorShader.SetBuffer(0, "propagator", _propagatorBuf);
            _propagatorShader.SetBuffer(0, "result", _resultBuf);
            BindInOutBuffers(false);
        }

        protected override void Clear()
        {
            base.Clear();

            /* Clear Wave */
            {
                Parallel.For(0, nbPatterns, pattern =>
                {
                    for (int y = 0; y < height; y++)
                    {
                        int yOffset = y * width;
                        for (int x = 0; x < width; x++)
                        {
                            _waveCopyBuffer[(x + yOffset) * nbPatterns + pattern] = Convert.ToUInt32(wave[x + yOffset][pattern]);
                        }
                    }
                });
                _waveInBuf.SetData(_waveCopyBuffer);
                _waveOutBuf.SetData(_waveCopyBuffer);
            }

            /* Clear const weight data. */
            {
                for (int pattern = 0; pattern < weights.Length; pattern++)
                {
                    _weightCopyBuffer[pattern].weight = (float) weights[pattern];
                    _weightCopyBuffer[pattern].logWeight = (float) weightLogWeights[pattern];
                }

                _weightBuf.SetData(_weightCopyBuffer);
            }

            /* Clear memoisation data. */
            {
                for (int node = 0; node < wave.Length; node++)
                {
                    _memoisationCopyBuffer[node].sums_of_weights = (float)sumsOfWeights[node];
                    _memoisationCopyBuffer[node].sums_of_weight_log_weights = (float)sumsOfWeightLogWeights[node];
                    _memoisationCopyBuffer[node].entropies = (float)entropies[node];
                    _memoisationCopyBuffer[node].num_possible_patterns = numPossiblePatterns[node];
                }

                _memoisationBuf.SetData(_memoisationCopyBuffer);
            }

            ClearInOutBuffers();
        }

        private void ClearInOutBuffers()
        {
            _outCollapseBuf.SetData(_collapseClearData);
        }

        /// <summary>
        /// This should be called after the algorithm finishes as it will be the starting point for following runs!
        /// </summary>
        private void ClearInBuffers()
        {
            _inCollapseBuf.SetData(_collapseClearData);
        }

        public override IEnumerator Run(uint seed, int limit, WFC_Result result)
        {
            if (wave == null) Init();
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

        private IEnumerator Run_Internal(WFC_Objects objects, WFC_Result result)
        {
            /* Copy back memoisation data. It is needed to find the next free node. */
            CopyGpuMemoisationToCpu();
            int node = NextUnobservedNode(objects.random);
            if (node >= 0)
            {
                Observe(node, ref objects.random);
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
            while (_openCells && isPossible)
            {
                Result[] resultBufData = {new Result
                {
                    isPossible = Convert.ToUInt32(isPossible),
                    openNodes = Convert.ToUInt32(_openCells = false)
                }};
                _resultBuf.SetData(resultBufData);

                _propagatorShader.Dispatch(
                    0,
                    (int)Math.Ceiling((float)width / PropagationThreadGroupSizeX),
                    (int)Math.Ceiling((float)height / PropagationThreadGroupSizeY),
                    1);

                /* Copy result of Compute operation back to CPU buffer. */
                _resultBuf.GetData(resultBufData);
                isPossible = Convert.ToBoolean(resultBufData[0].isPossible);
                _openCells = Convert.ToBoolean(resultBufData[0].openNodes);

                /* Swap the in out buffers. */
                BindInOutBuffers(true);
                ClearInOutBuffers();

                if (propagatorSettings.debug != PropagatorSettings.DebugMode.None)
                {
                    yield return DebugDrawCurrentState();
                }
            }
            
            timer.Stop(false);
        }
        
        protected override void Observe(int node, ref Random random)
        {
            var timer = new CodeTimer_Average(true, true, true, "Observe_Naive", Debug.Log);
            
            FillBanCopyBuffers();
            base.Observe(node, ref random);
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
            base.Ban(node, pattern);

            _waveCopyBuffer[node * nbPatterns + pattern] = Convert.ToUInt32(false);

            _memoisationCopyBuffer[node].sums_of_weights = (float)sumsOfWeights[node];
            _memoisationCopyBuffer[node].sums_of_weight_log_weights = (float)sumsOfWeightLogWeights[node];
            _memoisationCopyBuffer[node].entropies = (float)entropies[node];
            _memoisationCopyBuffer[node].num_possible_patterns = numPossiblePatterns[node];

            /* Update the collapse information for the compute shader. */
            _collapseCopyBuffer[node].is_collapsed = Convert.ToUInt32(true);

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
                _collapseCopyBuffer[node2].needs_collapse = Convert.ToUInt32(true);
            }

            _openCells = true;
        }

        /// <summary>
        /// This should be called if the user wants to Ban a pattern from a node and directly upload those changes to the GPU.
        /// </summary>
        /// <param name="node"></param>
        /// <param name="pattern"></param>
        public void BanAndApply(int node, int pattern)
        {
            FillBanCopyBuffers();
            Ban(node, pattern);
            ApplyBanCopyBuffers();
        }

        /// <summary>
        /// This NEEDS to be called AFTER Ban in order to upload all texture changes to the GPU.
        /// Making this a separate call gives the option to first ban all wanted patterns and then upload the changes
        /// in one go. This should be much quicker.
        /// </summary>
        private void ApplyBanCopyBuffers()
        {
            _waveInBuf.SetData(_waveCopyBuffer);
            _memoisationBuf.SetData(_memoisationCopyBuffer);
            _inCollapseBuf.SetData(_collapseCopyBuffer);

            Result[] resultBufData = {new Result
            {
                isPossible = Convert.ToUInt32(isPossible), openNodes = Convert.ToUInt32(_openCells)
            }};
            _resultBuf.SetData(resultBufData);
        }

        private void CopyGpuWaveToCpu()
        {
            _waveInBuf.GetData(_waveCopyBuffer);

            Parallel.For(0, nbPatterns, pattern =>
            {
                for (int node = 0; node < wave.Length; node++)
                {
                    wave[node][pattern] = Convert.ToBoolean(_waveCopyBuffer[node * nbPatterns + pattern]);
                }
            });
        }

        private void CopyGpuMemoisationToCpu()
        {
            _memoisationBuf.GetData(_memoisationCopyBuffer);

            for (int node = 0; node < wave.Length; node++)
            {
                sumsOfWeights[node] = _memoisationCopyBuffer[node].sums_of_weights;
                sumsOfWeightLogWeights[node] = _memoisationCopyBuffer[node].sums_of_weight_log_weights;
                entropies[node] = _memoisationCopyBuffer[node].entropies;
                numPossiblePatterns[node] = _memoisationCopyBuffer[node].num_possible_patterns;
            }
        }

        private void CopyGpuCollapseToCpu()
        {
            _inCollapseBuf.GetData(_collapseCopyBuffer);
        }

        private IEnumerator DebugDrawCurrentState()
        {
            CopyGpuWaveToCpu();
            CopyGpuCollapseToCpu();
            List<(int, int)> propagatingCells = new List<(int, int)>();
            for (int node = 0; node < wave.Length; node++)
            {
                if (Convert.ToBoolean(_collapseCopyBuffer[node].needs_collapse))
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

        public void Dispose()
        {
            _weightBuf?.Release();
            _resultBuf?.Release();
            _waveInBuf?.Release();
            _waveOutBuf?.Release();
            _memoisationBuf?.Release();
            _propagatorBuf?.Release();
            _inCollapseBuf?.Release();
            _outCollapseBuf?.Release();
        }
    }
}
