using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;
using WFC;
using Random = Unity.Mathematics.Random;

public class GPU_Model : Model, IDisposable
{
    private readonly ComputeShader _observerShader;
    private readonly ComputeShader _propagatorShader;
    private readonly ComputeShader _banShader;
    private readonly ComputeShader _finishIterationShader;
    private readonly ComputeShader _clearOutBuffersShader;
    private readonly ComputeShader _resetOpenNodesShader;

    private readonly int _propagationIterations;
    private readonly int _totalIterations;
    
    #region CommonShaderResources

    private uint[] _waveCopyBuffer;
    /*
    Actual wave result
    wave(node, pattern)
    */
    private ComputeBuffer _waveBuf;

    struct Weighting
    {
        public float weight;
        public float logWeight;
        public float distribution;
        public float padding;
    }
    private Weighting[] _weightingCopyBuffer;
    private ComputeBuffer _weightBuf;

    [StructLayout(LayoutKind.Sequential)]
    struct Memoisation
    {
        public float sums_of_weights;
        public float sums_of_weight_log_weights;
        public float entropies;
        public int num_possible_patterns;
    }

    private Memoisation[] _memoisationCopyBuffer;
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
        public uint openNodes; // Needed internally, do not remove
        public uint finished;
        public uint padding;
    }
    private readonly Result[] _resultCopyBuf = new Result[1];
    private readonly ComputeBuffer _resultBuf = new ComputeBuffer(1, sizeof(uint) * 4); // Change this to structured buffer

    [StructLayout(LayoutKind.Sequential)]
    struct Collapse
    {
        public uint is_collapsed;
        public uint needs_collapse;
    }
    private Collapse[] _collapseClearData;
    private Collapse[] _collapseCopyBuffer;
    
    /* Cells in which the patterns changed. */
    private ComputeBuffer _inCollapseBuf;
    private ComputeBuffer _outCollapseBuf;
    
    #endregion

    #region ObserverShaderResources

    [StructLayout(LayoutKind.Sequential)]
    struct ObserverParams
    {
        public uint randomState;
    }
    private ObserverParams[] _observerParamsCopyBuffer = new ObserverParams[1];
    private ComputeBuffer _observerParamsBuf = new ComputeBuffer(1, sizeof(uint));

    #endregion

    #region BanShaderResources

    struct BanParams
    {
        public int node;
        public int pattern;
    }
    private BanParams[] _banParamsCopyBuffer = new BanParams[1];
    private ComputeBuffer _banParamsBuf = new ComputeBuffer(1, sizeof(int) * 2);

    #endregion
    
    public GPU_Model(
        ComputeShader observerShader,
        ComputeShader propagatorShader,
        ComputeShader banShader,
        ComputeShader finishIterationShader,
        ComputeShader clearOutBuffersShader,
        ComputeShader resetOpenNodesShader,
        int propagationIterations, int totalIterations,
        int width, int height, int patternSize, bool periodic) : 
        base(width, height, patternSize, periodic)
    {
        _observerShader = observerShader;
        _propagatorShader = propagatorShader;
        _banShader = banShader;
        _finishIterationShader = finishIterationShader;
        _clearOutBuffersShader = clearOutBuffersShader;
        _resetOpenNodesShader = resetOpenNodesShader;
        
        _propagationIterations = propagationIterations;
        _totalIterations = totalIterations;

        _memoisationBuf = new ComputeBuffer(width * height, sizeof(float) * 3 + sizeof(int));
        _inCollapseBuf = new ComputeBuffer(width * height, sizeof(uint) * 2);
        _outCollapseBuf = new ComputeBuffer(width * height, sizeof(uint) * 2);
    }

    public override void SetData(int nbPatterns, double[] weights, (bool[][][] dense, int[][][] standard) propagator,
        PropagatorSettings propagatorSettings)
    {
        base.SetData(nbPatterns, weights, propagator, propagatorSettings);
        
        _waveBuf = new ComputeBuffer(width * height * nbPatterns, sizeof(uint));
        _propagatorBuf = new ComputeBuffer(nbPatterns * nbPatterns, sizeof(uint) * 4);
        _weightBuf = new ComputeBuffer(weights.Length, sizeof(float) * 4);

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

    ~GPU_Model()
    {
        Dispose();
    }

    protected override void Init()
    {
        _waveCopyBuffer = new uint[width * height * nbPatterns];
        _weightingCopyBuffer = new Weighting[nbPatterns];

        for (int pattern = 0; pattern < nbPatterns; pattern++)
        {
            _weightingCopyBuffer[pattern].weight = (float) weights[pattern];
            _weightingCopyBuffer[pattern].logWeight = (float) (weights[pattern] * Math.Log(weights[pattern]));
            totalSumOfWeights += weights[pattern];
            totalSumOfWeightLogWeights += _weightingCopyBuffer[pattern].logWeight;
        }
        _weightBuf.SetData(_weightingCopyBuffer);

        startingEntropy = Math.Log(totalSumOfWeights) - totalSumOfWeightLogWeights / totalSumOfWeights;

        _collapseClearData = new Collapse[height * width];
        _collapseCopyBuffer = new Collapse[height * width];
        
        _memoisationCopyBuffer = new Memoisation[width * height];
        
        ClearInBuffers();
        BindResources();
    }

    private void BindInOutBuffers(bool swap, CommandBuffer buffer)
    {
        if (swap)
        {
            USCSL.Extensions.Swap(ref _inCollapseBuf, ref _outCollapseBuf);
        }
        
        buffer.SetComputeBufferParam(_propagatorShader, 0, "in_collapse", _inCollapseBuf);
        buffer.SetComputeBufferParam(_propagatorShader, 0, "in_collapse", _outCollapseBuf);
        
        buffer.SetComputeBufferParam(_observerShader, 0, "in_collapse", _inCollapseBuf);
        buffer.SetComputeBufferParam(_observerShader, 0, "in_collapse", _outCollapseBuf);
        
        buffer.SetComputeBufferParam(_banShader, 0, "in_collapse", _inCollapseBuf);
        buffer.SetComputeBufferParam(_banShader, 0, "in_collapse", _outCollapseBuf);
        
        buffer.SetComputeBufferParam(_clearOutBuffersShader, 0, "in_collapse", _inCollapseBuf);
        buffer.SetComputeBufferParam(_clearOutBuffersShader, 0, "in_collapse", _outCollapseBuf);
    }
    
    private void BindInOutBuffers(bool swap)
    {
        if (swap)
        {
            USCSL.Extensions.Swap(ref _inCollapseBuf, ref _outCollapseBuf);
        }
        
        _propagatorShader.SetBuffer(0, "in_collapse", _inCollapseBuf);
        _propagatorShader.SetBuffer(0, "out_collapse", _outCollapseBuf);

        _observerShader.SetBuffer(0, "in_collapse", _inCollapseBuf);
        _observerShader.SetBuffer(0, "out_collapse", _outCollapseBuf);
        
        _banShader.SetBuffer(0, "in_collapse", _inCollapseBuf);
        _banShader.SetBuffer(0, "out_collapse", _outCollapseBuf);
        
        _clearOutBuffersShader.SetBuffer(0, "in_collapse", _inCollapseBuf);
        _clearOutBuffersShader.SetBuffer(0, "out_collapse", _outCollapseBuf);
    }
    
    private void BindResources()
    {
        _propagatorShader.SetInt("nb_patterns", nbPatterns);
        _propagatorShader.SetInt("width", width);
        _propagatorShader.SetInt("height", height);
        _propagatorShader.SetBool("is_periodic", periodic);
        
        _propagatorShader.SetBuffer(0, "wave_data", _waveBuf);
        _propagatorShader.SetBuffer(0, "weighting", _weightBuf);
        _propagatorShader.SetBuffer(0, "memoisation", _memoisationBuf);
        _propagatorShader.SetBuffer(0, "propagator", _propagatorBuf);
        _propagatorShader.SetBuffer(0, "result", _resultBuf);
        
        _observerShader.SetInt("nb_patterns", nbPatterns);
        _observerShader.SetInt("width", width);
        _observerShader.SetInt("height", height);
        _observerShader.SetBool("is_periodic", periodic);
        
        _observerShader.SetBuffer(0, "wave_data", _waveBuf);
        _observerShader.SetBuffer(0, "weighting", _weightBuf);
        _observerShader.SetBuffer(0, "memoisation", _memoisationBuf);
        _observerShader.SetBuffer(0, "propagator", _propagatorBuf);
        _observerShader.SetBuffer(0, "result", _resultBuf);
        _observerShader.SetBuffer(0, "observer_params", _observerParamsBuf);
        
        _banShader.SetInt("nb_patterns", nbPatterns);
        _banShader.SetInt("width", width);
        _banShader.SetInt("height", height);
        _banShader.SetBool("is_periodic", periodic);
        
        _banShader.SetBuffer(0, "wave_data", _waveBuf);
        _banShader.SetBuffer(0, "weighting", _weightBuf);
        _banShader.SetBuffer(0, "memoisation", _memoisationBuf);
        _banShader.SetBuffer(0, "propagator", _propagatorBuf);
        _banShader.SetBuffer(0, "result", _resultBuf);
        _banShader.SetBuffer(0, "ban_params", _resultBuf);
        
        _finishIterationShader.SetInt("width", width);
        _finishIterationShader.SetInt("height", height);
        _finishIterationShader.SetBuffer(0, "memoisation", _memoisationBuf);
        _finishIterationShader.SetBuffer(0, "result", _resultBuf);
        
        _clearOutBuffersShader.SetInt("width", width);
        _clearOutBuffersShader.SetInt("height", height);
        _clearOutBuffersShader.SetBuffer(0, "out_collapse", _outCollapseBuf);
        
        _resetOpenNodesShader.SetBuffer(0, "result", _resultBuf);
        
        BindInOutBuffers(false);
    }

    private double _clearTotalTime = 0;
    protected override void Clear()
    {
        double startTime = Time.realtimeSinceStartupAsDouble;
        base.Clear();
        
        Result[] resultBufData = {new Result
        {
            isPossible = Convert.ToUInt32(isPossible),
            openNodes = Convert.ToUInt32(false),
            finished = Convert.ToUInt32(false),
        }};
        _resultBuf.SetData(resultBufData);

        Parallel.For(0, nbNodes, node =>
        {
            for (int pattern = 0; pattern < nbPatterns; pattern++)
            {
                _waveCopyBuffer[node * nbPatterns + pattern] = Convert.ToUInt32(true);
            }

            _memoisationCopyBuffer[node].num_possible_patterns = nbPatterns;
            _memoisationCopyBuffer[node].sums_of_weights = (float) totalSumOfWeights;
            _memoisationCopyBuffer[node].sums_of_weight_log_weights = (float) totalSumOfWeightLogWeights;
            _memoisationCopyBuffer[node].entropies = (float) startingEntropy;
        });

        _waveBuf.SetData(_waveCopyBuffer);
        _memoisationBuf.SetData(_memoisationCopyBuffer);

        ClearOutBuffers();
        double executionTime = Time.realtimeSinceStartupAsDouble - startTime;
        _clearTotalTime += executionTime;
        Debug.Log($"Clear step took {executionTime} sec and {_clearTotalTime} sec in total.");
    }

    private void ClearOutBuffers(CommandBuffer buffer)
    {
        buffer.DispatchCompute(_clearOutBuffersShader,
            0,
            (int) Math.Ceiling(width / 32.0f),
            (int) Math.Ceiling(height / 32.0f),
            1);
    }
    
    private void ClearOutBuffers()
    {
        _clearOutBuffersShader.Dispatch(
            0,
            (int) Math.Ceiling(width / 32.0f),
            (int) Math.Ceiling(height / 32.0f),
            1);
    }

     /// <summary>
     /// This should be called after the algorithm finishes as it will be the starting point for following runs!
     /// </summary>
    private void ClearInBuffers()
    {
        _inCollapseBuf.SetData(_collapseClearData);
    }

    private class WFC_Objects
    {
        public Random random;
    }

    private double _totalRunTime = 0;
    public override IEnumerator Run(uint seed, int limit, WFC_Result result)
    {
        double startTime = Time.realtimeSinceStartupAsDouble;

        if (_waveCopyBuffer == null) Init();
        Clear();
        
        _observerParamsCopyBuffer[0].randomState = seed;
        _observerParamsBuf.SetData(_observerParamsCopyBuffer);
        
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

    private double _totalObserveTime = 0;
    private void Observe(CommandBuffer buffer)
    {
       // double startTime = Time.realtimeSinceStartupAsDouble;
        /*
         * Since we want to ban nodes in the in buffers we swap in and out buffers so that the out-buffer in the shader
         * (the one written to) is actually the in-buffer. This way we can leave only one of the buffers Read-Writeable
         */
        BindInOutBuffers(true, buffer);
        
        buffer.DispatchCompute(_observerShader, 0, 1, 1, 1);
        
        /* Swap back the in- and out-buffers so that they align with the correct socket for the propagation step. */
        BindInOutBuffers(true, buffer);
        
        /*double executionTime = Time.realtimeSinceStartupAsDouble - startTime;
        _totalObserveTime += executionTime;
        Debug.Log($"Observe step took {executionTime} sec and {_totalObserveTime} sec in total.");*/
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
        
        CommandBuffer observeBuffer = new CommandBuffer();
        #region Fill Observe Buffer
        
        Observe(observeBuffer);
            
        #endregion
        
        CommandBuffer propagationBuffer = new CommandBuffer();
        #region Fill PropagationBuffer

        // Resets OpenNodes to false.
        propagationBuffer.DispatchCompute(_resetOpenNodesShader,
            0,
            1,
            1,
            1);
        
        // Propagates node collapse, will set OpenNodes to true if it collapses further nodes
        propagationBuffer.DispatchCompute(_propagatorShader, 
            0, 
            (int) Math.Ceiling(width / 4.0f),
            (int) Math.Ceiling(height / 4.0f),
            1);

        /* Swap the in out buffers. */
        BindInOutBuffers(true, propagationBuffer);
        ClearOutBuffers(propagationBuffer);
                
        propagationBuffer.DispatchCompute(_finishIterationShader, 
            0,
            (int) Math.Ceiling(width / 4.0f),
            (int) Math.Ceiling(height / 4.0f),
            1);
        
        #endregion

        while (!finished && isPossible)
        {
            for (int i = 0; i < _totalIterations; i++)
            {
                Graphics.ExecuteCommandBuffer(observeBuffer);
                for (int y = 0; y < _propagationIterations; y++)
                {
                    Graphics.ExecuteCommandBuffer(propagationBuffer);
                    if (propagatorSettings.debug == PropagatorSettings.DebugMode.OnSet)
                    {
                        yield return DebugDrawCurrentState();
                    }
                }
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

        _banParamsCopyBuffer[0].node = node;
        _banParamsCopyBuffer[0].pattern = pattern;
        _banParamsBuf.SetData(_banParamsCopyBuffer);
        _observerShader.Dispatch(0, 1, 1, 1);
        
        /* Swap back the in- and out-buffers so that they align with the correct socket for the propagation step. */
        BindInOutBuffers(true);
        
        _resultBuf.GetData(_resultCopyBuf);
        (isPossible) = Convert.ToBoolean(_resultCopyBuf[0].isPossible);
    }

    private bool[][] CopyGpuWaveToCpu()
    {
        _waveBuf.GetData(_waveCopyBuffer);
        bool[][] wave = new bool[nbNodes][];
        
        Parallel.For(0, nbNodes, node =>
        {
            wave[node] = new bool[nbPatterns];
            for (int pattern = 0; pattern < nbPatterns; pattern++)
            {
                wave[node][pattern] = Convert.ToBoolean(_waveCopyBuffer[node * nbPatterns + pattern]);
            }
        });
        return wave;
    }

    private void CopyGpuCollapseToCpu()
    {
        _inCollapseBuf.GetData(_collapseCopyBuffer);
    }

    /*
     Transform the wave to a valid output (a 2d array of patterns that aren't in
     contradiction). This function should be used only when all cell of the wave
     are defined.
    */
    private int[,] WaveToOutput()
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

    private IEnumerator DebugDrawCurrentState()
    {
        bool[][] wave = CopyGpuWaveToCpu();
        CopyGpuCollapseToCpu();
        _memoisationBuf.GetData(_memoisationCopyBuffer);
        _resultBuf.GetData(_resultCopyBuf);
        List<(int, int)> propagatingCells = new List<(int, int)>();
        for (int node = 0; node < nbNodes; node++)
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
        _waveBuf?.Release();
        _memoisationBuf?.Release();
        _propagatorBuf?.Release();
        _inCollapseBuf?.Release();
        _outCollapseBuf?.Release();
        _observerParamsBuf?.Release();
        _banParamsBuf?.Release();
    }
}
