using System;
using System.Collections;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using UnityEngine;
using WFC;
using Random = Unity.Mathematics.Random;

public class GPU_Model : Model, IDisposable
{
    private ComputeShader _propagatorShader;

    #region ShaderResources

    private byte[] _waveCopyBuffer;
    /*
    Actual wave result
    wave(node, pattern)
    */
    private ComputeBuffer _waveBuf;

    struct Weighting
    {
        public float weight;
        public float log_weight;
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

    private Memoisation[] _memoisationCopyBuffer;
    private ComputeBuffer _memoisationBuf;

    /* propagator[uint3(pattern, otherPattern, direction)] */
    private Texture3D _propagatorTex;
    
    /* compatible[int3(nodeX, nodeY, pattern-direction] */
    private int[] compatiblCopyBuffer;
    private ComputeBuffer _compatibleBuf;
    
    /*
     * _resultBuf[0] : (bool) isPossible
     * _resultBuf[1] : (bool) openNodes
     */
    private ComputeBuffer _resultBuf; // Change this to structured buffer

    [StructLayout(LayoutKind.Sequential)]
    struct Collapse
    {
        public byte is_collapsed;
        public byte needs_collapsed;
    }
    private Collapse[] _collapseClearData;
    private Collapse[] _collapseCopyBuffer;
    
    /* Cells in which the patterns changed. */
    private ComputeBuffer _inCollapseBuf;
    private ComputeBuffer _outCollapseBuf;

    /*
    Which pattern changed.
    input_pattern_change[uint3(nodeX, nodeY, pattern)]
    */
    private byte[] _patternCollapseClearData;
    private byte[] _patternCollapseCopyBuffer;
    private ComputeBuffer _inPatternCollapsedBuf;
    private ComputeBuffer _outPatternCollapsedBuf;

    private bool openCells = true;
    #endregion

    public GPU_Model(
        ComputeShader propagatorShader,
        int width, int height, int patternSize, bool periodic) : 
        base(width, height, patternSize, periodic)
    {
        _propagatorShader = propagatorShader;

        _memoisationBuf = new ComputeBuffer(width * height, sizeof(float) * 3 + sizeof(int));
        _memoisationCopyBuffer = new Memoisation[width * height];
        _inCollapseBuf = new ComputeBuffer(width * height, sizeof(bool) * 2);
        _outCollapseBuf = new ComputeBuffer(width * height, sizeof(bool) * 2);

        _resultBuf = new ComputeBuffer(2, sizeof(bool));

        _collapseClearData = new Collapse[height * width];
        _collapseCopyBuffer = new Collapse[height * width];
    }

    public override void SetData(int nbPatterns, double[] weights, (bool[][][] dense, int[][][] standard) propagator,
        PropagatorSettings propagatorSettings)
    {
        base.SetData(nbPatterns, weights, propagator, propagatorSettings);

        _waveCopyBuffer = new byte[width * height * nbPatterns];
        _waveBuf = new ComputeBuffer(width * height * nbPatterns, sizeof(bool));
        _propagatorTex = new Texture3D(nbPatterns, nbPatterns, 4, TextureFormat.R8, false);
        _weightBuf = new ComputeBuffer(weights.Length, sizeof(float) * 2);
        _compatibleBuf = new ComputeBuffer(width * height * nbPatterns * 4, sizeof(int));
        compatiblCopyBuffer = new int[width * height * nbPatterns * 4];
        _inPatternCollapsedBuf = new ComputeBuffer(width * height * nbPatterns, sizeof(bool));
        _outPatternCollapsedBuf = new ComputeBuffer(width * height * nbPatterns, sizeof(bool));
        _patternCollapseClearData = new byte[height * width * nbPatterns];
        _patternCollapseCopyBuffer = new byte[height * width * nbPatterns];
    }

    ~GPU_Model()
    {
        Dispose();
    }

    protected override void Init()
    {
        base.Init();

        {
            float[] weightBufData = new float[weights.Length * 2];
            for (int pattern = 0; pattern < weights.Length; pattern++)
            {
                weightBufData[pattern * 2] = (float) weights[pattern];
                weightBufData[pattern * 2 + 1] = (float) weightLogWeights[pattern];
            }

            _weightBuf.SetData(weightBufData);
        }

        byte[] resultBufData = {Convert.ToByte(isPossible), Convert.ToByte(openCells)};
        _resultBuf.SetData(resultBufData);

        {
            bool[] propagatorData = new bool[nbPatterns * nbPatterns * 4];
            Parallel.For(0, 4, dir =>
            {
                for (int pattern = 0; pattern < nbPatterns; pattern++)
                {
                    int patternDirOffset = (pattern * 4 + dir) * nbPatterns;
                    for (int otherPattern = 0; otherPattern < nbPatterns; otherPattern++)
                    {
                        propagatorData[otherPattern + patternDirOffset] = densePropagator[pattern][dir][otherPattern];
                    }
                }
            });
            _propagatorTex.SetPixelData(propagatorData, 0);
            _propagatorTex.Apply();
        }
        ClearOutBuffers();
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
        _propagatorShader.SetTexture(0, "propagator", _propagatorTex, 0);
        _propagatorShader.SetBuffer(0, "compatible_data", _compatibleBuf);
        _propagatorShader.SetBuffer(0, "result", _resultBuf);
        BindInOutBuffers(false);
    }

    protected override void Clear()
    {
        base.Clear();
        {
            Parallel.For(0, nbPatterns, pattern =>
            {
                for (int y = 0; y < height; y++)
                {
                    int yOffset = y * width;
                    for (int x = 0; x < width; x++)
                    {
                        _waveCopyBuffer[(x + yOffset) * nbPatterns + pattern] = Convert.ToByte(wave[x + yOffset][pattern]);
                    }
                }
            });
            _waveBuf.SetData(_waveCopyBuffer);
        }

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

        {
            var compatibleBufData = new int[width * height * nbPatterns * 4];
            Parallel.For(0, nbPatterns * 4, patternDir =>
            {
                int pattern = patternDir / 4;
                int dir = patternDir % 4;
                int patternDirOffset = patternDir * width * height;
                for (int y = 0; y < height; y++)
                {
                    int yOffset = y * width;
                    for (int x = 0; x < width; x++)
                    {
                        compatibleBufData[x + yOffset + patternDirOffset] = compatible[x + yOffset][pattern][dir];
                    }
                }
            });
            _compatibleBuf.SetData(compatibleBufData);
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
        int node = NextUnobservedNode(objects.random);
        if (node >= 0)
        {
            Observe(node, ref objects.random);

            var propagation = Propagate(objects);
            propagation.MoveNext();
            while (propagation.MoveNext())
            {
                yield return propagation.Current;
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
        while (openCells)
        {
            _propagatorShader.Dispatch(
                0,
                (int) Math.Ceiling(width / 16.0f),
                (int) Math.Ceiling(height / 16.0f),
                1);
            
            /* Copy result of Compute operation back to CPU buffer. */
            byte[] result = new byte[2];
            _resultBuf.GetData(result);
            isPossible = Convert.ToBoolean(result[0]);
            openCells = Convert.ToBoolean(result[1]);
            
            if (propagatorSettings.debug != PropagatorSettings.DebugMode.None)
            {
                yield return DebugDrawCurrentState();
            }
            
            /* Swap the in out buffers. */
            BindInOutBuffers(true);
            ClearOutBuffers();
        }
        
        /* Copy back memoisation data. It is needed to find the next free node. */
        CopyGpuMemoisationToCpu();
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
        _waveBuf.GetData(_waveCopyBuffer);
        _memoisationBuf.GetData(_memoisationCopyBuffer);
        _compatibleBuf.GetData(compatiblCopyBuffer);
        _inCollapseBuf.GetData(_collapseCopyBuffer);
        _inPatternCollapsedBuf.GetData(_patternCollapseCopyBuffer);
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

        int nbNodes = wave.Length;
        _waveCopyBuffer[node * nbPatterns + pattern] = Convert.ToByte(false);
        
        compatiblCopyBuffer[(node * nbPatterns * 4) + pattern * 4 + 0] = 0;
        compatiblCopyBuffer[(node * nbPatterns * 4) + pattern * 4 + 1] = 0;
        compatiblCopyBuffer[(node * nbPatterns * 4) + pattern * 4 + 2] = 0;
        compatiblCopyBuffer[(node * nbPatterns * 4) + pattern * 4 + 3] = 0;

        Debug.Log($"{banCount++}");
        
        _memoisationCopyBuffer[node].sums_of_weights = (float)sumsOfWeights[node];
        _memoisationCopyBuffer[node].sums_of_weight_log_weights = (float) sumsOfWeightLogWeights[node];
        _memoisationCopyBuffer[node].entropies = (float) entropies[node];
        _memoisationCopyBuffer[node].num_possible_patterns = numPossiblePatterns[node];

        /* Update the collapse information for the compute shader. */
        _collapseCopyBuffer[node].is_collapsed = Convert.ToByte(true);

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
            _collapseCopyBuffer[node2].needs_collapsed = Convert.ToByte(true);
        }
        
        _patternCollapseCopyBuffer[node * nbPatterns + pattern] = Convert.ToByte(true);
        openCells = true;
    }

    /// <summary>
    /// This should be called if the user wants to Ban a pattern from a node and directly upload those changes to the GPU.
    /// </summary>
    /// <param name="node"></param>
    /// <param name="pattern"></param>
    public void BanAndApply(int node, int pattern)
    {
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
        _compatibleBuf.SetData(compatiblCopyBuffer);
        _inCollapseBuf.SetData(_collapseCopyBuffer);
        _inPatternCollapsedBuf.SetData(_patternCollapseCopyBuffer);
        
        byte[] resultBufData = {Convert.ToByte(isPossible), Convert.ToByte(openCells)};
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

        for (int pattern = 0; pattern < nbPatterns; pattern++)
        {
            sumsOfWeights[pattern] = _memoisationCopyBuffer[pattern].sums_of_weights;
            sumsOfWeightLogWeights[pattern] = _memoisationCopyBuffer[pattern].sums_of_weight_log_weights;
            entropies[pattern] = _memoisationCopyBuffer[pattern].entropies;
            numPossiblePatterns[pattern] = _memoisationCopyBuffer[pattern].num_possible_patterns;
        }
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
