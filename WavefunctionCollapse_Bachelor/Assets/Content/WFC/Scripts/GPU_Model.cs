using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using UnityEngine;
using WFC;
using Random = Unity.Mathematics.Random;

public class GPU_Model : Model, IDisposable
{
    private readonly ComputeShader _propagatorShader;

    #region ShaderResources

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
    }
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

    [StructLayout(LayoutKind.Sequential)]
    struct Compatible
    {
        /* Array inside GPU struct */
        public int compatibleDown;
        public int compatibleLeft;
        public int compatibleRight;
        public int compatibleUp;

    }
    /* compatible[int3(nodeX, nodeY, pattern-direction] */
    private Compatible[] _compatibleCopyBuffer;
    private ComputeBuffer _compatibleBuf;

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

    /*
    Which pattern changed.
    input_pattern_change[uint3(nodeX, nodeY, pattern)]
    */
    private uint[] _patternCollapseClearData;
    private uint[] _patternCollapseCopyBuffer;
    private ComputeBuffer _inPatternCollapsedBuf;
    private ComputeBuffer _outPatternCollapsedBuf;

    private bool _openCells = true;
    #endregion

    public GPU_Model(
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
        
        _waveBuf = new ComputeBuffer(width * height * nbPatterns, sizeof(uint));
        _waveCopyBuffer = new uint[width * height * nbPatterns];
        
        _propagatorBuf = new ComputeBuffer(nbPatterns * nbPatterns, sizeof(uint) * 4);
        
        _weightBuf = new ComputeBuffer(weights.Length, sizeof(float) * 2);
        
        _compatibleBuf = new ComputeBuffer(width * height * nbPatterns, sizeof(int) * 4);
        _compatibleCopyBuffer = new Compatible[width * height * nbPatterns];
        
        _inPatternCollapsedBuf = new ComputeBuffer(width * height * nbPatterns, sizeof(uint));
        _outPatternCollapsedBuf = new ComputeBuffer(width * height * nbPatterns, sizeof(uint));
        
        _patternCollapseClearData = new uint[height * width * nbPatterns];
        _patternCollapseCopyBuffer = new uint[height * width * nbPatterns];
        
        _compatibleBuf.SetData(_compatibleCopyBuffer);
        ClearInBuffers();
        ClearOutBuffers();
        
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
            USCSL.Extensions.Swap(ref _inPatternCollapsedBuf, ref _outPatternCollapsedBuf);
        }
        
        _propagatorShader.SetBuffer(0, "in_collapse", _inCollapseBuf);
        _propagatorShader.SetBuffer(0, "out_collapse", _outCollapseBuf);
        
        _propagatorShader.SetBuffer(0, "in_pattern_collapsed_data", _inPatternCollapsedBuf);
        _propagatorShader.SetBuffer(0, "out_pattern_collapsed_data", _outPatternCollapsedBuf);
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
        _propagatorShader.SetBuffer(0, "compatible", _compatibleBuf);
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
            _waveBuf.SetData(_waveCopyBuffer);
        }
        
        /* Clear const weight data. */
        {
            Weighting[] weightBufData = new Weighting[weights.Length];
            for (int pattern = 0; pattern < weights.Length; pattern++)
            {
                weightBufData[pattern].weight = (float) weights[pattern];
                weightBufData[pattern].logWeight = (float) weightLogWeights[pattern];
            }

            _weightBuf.SetData(weightBufData);
        }

        /* Clear memoisation data. */
        {
            for (int node = 0; node < wave.Length; node++)
            {
                _memoisationCopyBuffer[node].sums_of_weights = (float) sumsOfWeights[node];
                _memoisationCopyBuffer[node].sums_of_weight_log_weights = (float) sumsOfWeightLogWeights[node];
                _memoisationCopyBuffer[node].entropies = (float) entropies[node];
                _memoisationCopyBuffer[node].num_possible_patterns = numPossiblePatterns[node];
            }
            
            _memoisationBuf.SetData(_memoisationCopyBuffer);
        }
        
        /* Clear compatibleBuf data. */
        {
            Parallel.For(0,  wave.Length, node =>
            {
                for (int pattern = 0; pattern < nbPatterns; pattern++)
                {
                    _compatibleCopyBuffer[node * nbPatterns + pattern].compatibleDown = compatible[node][pattern][0];
                    _compatibleCopyBuffer[node * nbPatterns + pattern].compatibleLeft = compatible[node][pattern][1];
                    _compatibleCopyBuffer[node * nbPatterns + pattern].compatibleRight = compatible[node][pattern][2];
                    _compatibleCopyBuffer[node * nbPatterns + pattern].compatibleUp = compatible[node][pattern][3];
                }
            });
            _compatibleBuf.SetData(_compatibleCopyBuffer);
        }
        ClearOutBuffers();
    }

    private void ClearOutBuffers()
    {
        _outCollapseBuf.SetData(_collapseClearData);
        _outPatternCollapsedBuf.SetData(_patternCollapseClearData);
    }

     /// <summary>
     /// This should be called after the algorithm finishes as it will be the starting point for following runs!
     /// </summary>
    private void ClearInBuffers()
    {
        _inCollapseBuf.SetData(_collapseClearData);
        _inPatternCollapsedBuf.SetData(_patternCollapseClearData);
    }

    private class WFC_Objects
    {
        public Random random;
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
                (int) Math.Ceiling(width / 16.0f),
                (int) Math.Ceiling(height / 16.0f),
                1);

            /* Copy result of Compute operation back to CPU buffer. */
            _resultBuf.GetData(resultBufData);
            isPossible = Convert.ToBoolean(resultBufData[0].isPossible);
            _openCells = Convert.ToBoolean(resultBufData[0].openNodes);
            Debug.Log(_openCells);
            /* Swap the in out buffers. */
            BindInOutBuffers(true);
            ClearOutBuffers();
            
            if (propagatorSettings.debug != PropagatorSettings.DebugMode.None)
            {
                yield return DebugDrawCurrentState();
            }
        }
    }

    private int NextUnobservedNode(Random random)
    {
        double min = Double.MaxValue;
        int argmin = -1;
        for (int node = 0; node < wave.Length; node++)
        {
            if (!periodic && (node % width + patternSize > width || node / width + patternSize > height)) continue;
            int remainingValues = numPossiblePatterns[node];
            double entropy = entropies[node];
            if (remainingValues > 1 && entropy <= min)
            {
                double noise = 1E-6 * random.NextDouble();
                if (entropy + noise < min)
                {
                    min = entropy + noise;
                    argmin = node;
                }
            }
        }

        return argmin;
    }

    private int oberseCount = 0;
    protected override void Observe(int node, ref Random random)
    {
        Debug.Log($"Observe : {oberseCount++}");
        FillBanCopyBuffers();
        base.Observe(node, ref random);
        ApplyBanCopyBuffers();
    }

    private void FillBanCopyBuffers()
    {
        CopyGpuWaveToCpu();
        CopyGpuMemoisationToCpu();
        CopyGpuCollapseToCpu();
        CopyGpuCompatibleToCpu();
    }

    private int banCount = 0;
    //TODO: Check if accessing the PixelData over the span of multiple function calls causes problems https://docs.unity3d.com/2020.1/Documentation/ScriptReference/Texture2D.GetPixelData.html
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

        ref var comp = ref _compatibleCopyBuffer[(node * nbPatterns) + pattern];
        comp.compatibleDown = 0;
        comp.compatibleLeft = 0;
        comp.compatibleRight = 0;
        comp.compatibleUp = 0;

        _memoisationCopyBuffer[node].sums_of_weights = (float)sumsOfWeights[node];
        _memoisationCopyBuffer[node].sums_of_weight_log_weights = (float) sumsOfWeightLogWeights[node];
        _memoisationCopyBuffer[node].entropies = (float) entropies[node];
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
        
        _patternCollapseCopyBuffer[node * nbPatterns + pattern] = Convert.ToUInt32(true);
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
        _waveBuf.SetData(_waveCopyBuffer); 
        _memoisationBuf.SetData(_memoisationCopyBuffer);
        _compatibleBuf.SetData(_compatibleCopyBuffer);
        _inCollapseBuf.SetData(_collapseCopyBuffer);
        _inPatternCollapsedBuf.SetData(_patternCollapseCopyBuffer);
        
        Result[] resultBufData = {new Result
        {
            isPossible = Convert.ToUInt32(isPossible), openNodes = Convert.ToUInt32(_openCells)
        }};
        _resultBuf.SetData(resultBufData);
    }

    private void CopyGpuWaveToCpu()
    { 
        _waveBuf.GetData(_waveCopyBuffer);
        
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
    
    private void CopyGpuCompatibleToCpu()
    {
        _compatibleBuf.GetData(_compatibleCopyBuffer);

        Parallel.For(0, wave.Length, node =>
        {
            for (int pattern = 0; pattern < nbPatterns; pattern++)
            {
                compatible[node][pattern][0] = _compatibleCopyBuffer[node * nbPatterns + pattern].compatibleDown;
                compatible[node][pattern][1] = _compatibleCopyBuffer[node * nbPatterns + pattern].compatibleLeft;
                compatible[node][pattern][2] = _compatibleCopyBuffer[node * nbPatterns + pattern].compatibleRight;
                compatible[node][pattern][3] = _compatibleCopyBuffer[node * nbPatterns + pattern].compatibleUp;
            }
        });
    }
    
    private void CopyGpuCollapseToCpu()
    {
        _inCollapseBuf.GetData(_collapseCopyBuffer);
        _inPatternCollapsedBuf.GetData(_patternCollapseCopyBuffer);
    }

    /*
     Transform the wave to a valid output (a 2d array of patterns that aren't in
     contradiction). This function should be used only when all cell of the wave
     are defined.
    */
    private int[,] WaveToOutput()
    {
        CopyGpuWaveToCpu();
        int[,] outputPatterns = new int[height, width];
        Parallel.For(0, wave.Length, node =>
        {
            for (int pattern = 0; pattern < nbPatterns; pattern++)
            {
                if (wave[node][pattern])
                {
                    observed[node] = pattern;
                    int x = node % width;
                    int y = node / width;
                    outputPatterns[y, x] = observed[node];
                    break;
                }
            }
        });

        return outputPatterns;
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
        _waveBuf?.Release();
        _memoisationBuf?.Release();
        _compatibleBuf?.Release();
        _inCollapseBuf?.Release();
        _outCollapseBuf?.Release();
        _inPatternCollapsedBuf?.Release();
        _outPatternCollapsedBuf?.Release();
    }
}
