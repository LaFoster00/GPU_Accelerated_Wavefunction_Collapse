using System;
using System.Collections;
using System.Threading.Tasks;
using UnityEngine;
using WFC;
using Random = Unity.Mathematics.Random;

public class GPU_Model : Model, IDisposable
{
    private ComputeShader _propagatorShader;

    #region ShaderResources

    private bool[] _waveCopyBuffer;
    /*
    Actual wave result
    wave[int3(nodeX, nodeY, pattern)]
    */
    private RenderTexture _waveTex;

    /*
     * weightBufData[pattern * 2] : weight
     * weightBufData[pattern * 2 + 1] : log_weight
     */
    private ComputeBuffer _weightBuf;

    private float[] _sumOfWeightsFloat;
    private float[] _sumsOfWeightLogWeightsFloat;
    private float[] _entropiesFloat;
    /*
    Packed textures holding
    depth 0 : sumOfWeights
    depth 1 : sumsOfWeightLogWeights
    depth 2 : entropies
    */
    private RenderTexture _memoisationTex;

    private RenderTexture _numPossiblePatternsTex;
    
    /* propagator[uint3(pattern, otherPattern, direction)] */
    private Texture3D _propagatorTex;
    
    /* compatible[int3(nodeX, nodeY, pattern-direction] */
    private RenderTexture _compatibleTex;
    
    /*
     * _resultBuf[0] : (bool) isPossible
     * _resultBuf[1] : (bool) openNodes
     */
    private ComputeBuffer _resultBuf;
    
    /* Neighbours of cells that changed. */
    RenderTexture _inNeedsCollapseTex;
    RenderTexture _outNeedsCollapseTex;

    /* Cells in which the patterns changed. */
    RenderTexture _inIsCollapsedTex;
    RenderTexture _outIsCollapsedTex;

    /*
    Which pattern changed.
    input_pattern_change[uint3(nodeX, nodeY, pattern)]
    */
    RenderTexture _inPatternCollapsedTex;
    RenderTexture _outPatternCollapsedTex;


    private bool[] _collapseClearData;
    private bool[] _patternCollapseClearData;

    private bool openCells = true;
    #endregion

    public GPU_Model(
        ComputeShader propagatorShader,
        int width, int height, int patternSize, bool periodic) : 
        base(width, height, patternSize, periodic)
    {
        _propagatorShader = propagatorShader;

        _sumOfWeightsFloat = new float[width * height];
        _sumsOfWeightLogWeightsFloat = new float[width * height];
        _entropiesFloat = new float[width * height];
        //_memoisationTex = new Texture2DArray(width, height, 3, TextureFormat.RFloat, false);
        _memoisationTex =
            new RenderTexture(width, height, 1, RenderTextureFormat.RFloat, RenderTextureReadWrite.Linear)
            {
                useMipMap = false,
                enableRandomWrite = true
            };
        _numPossiblePatternsTex =
            new RenderTexture(width, height, 1, RenderTextureFormat.RFloat, RenderTextureReadWrite.Linear)
            {
                useMipMap = false,
                enableRandomWrite = true
            };

        _inIsCollapsedTex =
            new RenderTexture(width, height, 1, RenderTextureFormat.R8, RenderTextureReadWrite.Linear)
            {
                useMipMap = false,
                enableRandomWrite = true
            };
        _outIsCollapsedTex =
            new RenderTexture(width, height, 1, RenderTextureFormat.R8, RenderTextureReadWrite.Linear)
            {
                useMipMap = false,
                enableRandomWrite = true
            };
        _inNeedsCollapseTex = 
            new RenderTexture(width, height, 1, RenderTextureFormat.R8, RenderTextureReadWrite.Linear)
            {
                useMipMap = false,
                enableRandomWrite = true
            };
        _outNeedsCollapseTex =
            new RenderTexture(width, height, 1, RenderTextureFormat.R8, RenderTextureReadWrite.Linear)
            {
                useMipMap = false,
                enableRandomWrite = true
            };

        _resultBuf = new ComputeBuffer(2, sizeof(bool), ComputeBufferType.Structured);

        _collapseClearData = new bool[height * width];
    }

    public override void SetData(int nbPatterns, double[] weights, (bool[][][] dense, int[][][] standard) propagator,
        PropagatorSettings propagatorSettings)
    {
        base.SetData(nbPatterns, weights, propagator, propagatorSettings);

        _waveCopyBuffer = new bool[width * height];
        _waveTex = 
            new RenderTexture(width, height, nbPatterns, RenderTextureFormat.R8, RenderTextureReadWrite.Linear)
            {
                useMipMap = false,
                enableRandomWrite = true
            };

        _weightBuf = new ComputeBuffer(weights.Length * 2, sizeof(float), ComputeBufferType.Structured);
        
        _propagatorTex = new Texture3D(nbPatterns, nbPatterns, 4, TextureFormat.R8, false);

        _compatibleTex =
            new RenderTexture(width, height, nbPatterns * 4, RenderTextureFormat.RFloat, RenderTextureReadWrite.Linear)
            {
                useMipMap = false,
                enableRandomWrite = true
            };

        _inPatternCollapsedTex =
            new RenderTexture(width, height, nbPatterns, RenderTextureFormat.R8, RenderTextureReadWrite.Linear)
            {
                useMipMap = false,
                enableRandomWrite = true
            };
        _outPatternCollapsedTex =
            new RenderTexture(width, height, nbPatterns, RenderTextureFormat.R8, RenderTextureReadWrite.Linear)
            {
                useMipMap = false,
                enableRandomWrite = true
            };
        
        _patternCollapseClearData = new bool[height * width * nbPatterns];
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

        var pixel = _inIsCollapsedTex.GetNativeTexturePtr();
        _inIsCollapsedTex.get
        _inIsCollapsedTex.SetPixelData(_collapseClearData, 0);
        _inNeedsCollapseTex.SetPixelData(_collapseClearData, 0);
        _inPatternCollapsedTex.SetPixelData(_patternCollapseClearData, 0);

        _inIsCollapsedTex.Apply();
        _inNeedsCollapseTex.Apply();
        _inPatternCollapsedTex.Apply();
        ClearOutBuffers();

        BindResources();
    }

    private void BindInOutBuffers(bool swap)
    {
        if (swap)
        {
            USCSL.Extensions.Swap(ref _inNeedsCollapseTex, ref _outNeedsCollapseTex);
            USCSL.Extensions.Swap(ref _inIsCollapsedTex, ref _outIsCollapsedTex);
            USCSL.Extensions.Swap(ref _inPatternCollapsedTex, ref _outPatternCollapsedTex);
        }
        
        _propagatorShader.SetTexture(0, "in_needs_collapse", _inNeedsCollapseTex, 0);
        _propagatorShader.SetTexture(0, "out_needs_collapse", _outNeedsCollapseTex, 0);
        
        _propagatorShader.SetTexture(0, "in_is_collapsed", _inIsCollapsedTex, 0);
        _propagatorShader.SetTexture(0, "out_is_collapsed", _outIsCollapsedTex, 0);
        
        _propagatorShader.SetTexture(0, "in_pattern_collapsed", _inPatternCollapsedTex, 0);
        _propagatorShader.SetTexture(0, "out_pattern_collapsed", _outPatternCollapsedTex, 0);
    }
    
    private void BindResources()
    {
        _propagatorShader.SetInt("nb_patterns", nbPatterns);
        _propagatorShader.SetInt("width", width);
        _propagatorShader.SetInt("height", height);
        _propagatorShader.SetBool("is_periodic", periodic);
        
        _propagatorShader.SetBuffer(0, "weight", _weightBuf);
        _propagatorShader.SetTexture(0, "memoisation", _memoisationTex, 0);
        _propagatorShader.SetTexture(0, "num_possible_patterns", _numPossiblePatternsTex, 0);
        _propagatorShader.SetTexture(0, "propagator", _propagatorTex, 0);
        _propagatorShader.SetTexture(0, "compatible", _compatibleTex);
        _propagatorShader.SetBuffer(0, "result", _resultBuf);
        BindInOutBuffers(false);
    }

    protected override void Clear()
    {
        base.Clear();
        {
            var waveTexData = _waveTex.GetPixelData<bool>(0);
            Parallel.For(0, nbPatterns, pattern =>
            {
                int patternOffset = pattern * width * height;
                for (int y = 0; y < height; y++)
                {
                    int yOffset = y * width;
                    for (int x = 0; x < width; x++)
                    {
                        waveTexData[x + yOffset + patternOffset] = wave[x + yOffset][pattern];
                    }
                }
            });
            _waveTex.SetPixelData(waveTexData, 0);
            _waveTex.Apply();
        }

        {
            var sumOfWeightsTexData = _memoisationTex.GetPixelData<float>(0, 0);
            var sumsOfWeightLogWeightsTexData = _memoisationTex.GetPixelData<float>(0, 1);
            var entropiesTexData = _memoisationTex.GetPixelData<float>(0, 2);

            for (int node = 0; node < wave.Length; node++)
            {
                sumOfWeightsTexData[node] = (float) sumsOfWeights[node];
                sumsOfWeightLogWeightsTexData[node] = (float) sumsOfWeightLogWeights[node];
                entropiesTexData[node] = (float) entropies[node];
            }
            
            _memoisationTex.Apply();
        }
        
        {
            _numPossiblePatternsTex.SetPixelData(numPossiblePatterns, 0);
        }

        {
            var compatibleTexData = _compatibleTex.GetPixelData<int>(0);
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
                        compatibleTexData[x + yOffset + patternDirOffset] = compatible[x + yOffset][pattern][dir];
                    }
                }
            });
            _compatibleTex.Apply();
        }
        
        ClearOutBuffers();
    }

    private void ClearOutBuffers()
    {
        _outIsCollapsedTex.SetPixelData(_collapseClearData, 0);
        _outNeedsCollapseTex.SetPixelData(_collapseClearData, 0);
        _outPatternCollapsedTex.SetPixelData(_patternCollapseClearData, 0);

        _outIsCollapsedTex.Apply();
        _outNeedsCollapseTex.Apply();
        _outPatternCollapsedTex.Apply();
    }

     /// <summary>
     /// This should be called after the algorithm finishes as it will be the starting point for following runs!
     /// </summary>
    private void ClearInBuffers()
    {
        _inIsCollapsedTex.SetPixelData(_collapseClearData, 0);
        _inNeedsCollapseTex.SetPixelData(_collapseClearData, 0);
        _inPatternCollapsedTex.SetPixelData(_patternCollapseClearData, 0);

        _inIsCollapsedTex.Apply();
        _inNeedsCollapseTex.Apply();
        _inPatternCollapsedTex.Apply();
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
        base.Observe(node, ref random);
        ApplyBanTextures();
    }

    private int banCount = 0;
    //TODO: Check if accessing the PixelData over the span of multiple function calls causes problems https://docs.unity3d.com/2020.1/Documentation/ScriptReference/Texture2D.GetPixelData.html
    /// <summary>
    /// This call will Ban a pattern from the specified node, but WONT upload those changes to the GPU.
    /// See BanAndApply() or ApplyBanTextures()
    /// </summary>
    /// <param name="node"></param>
    /// <param name="pattern"></param>
    public override void Ban(int node, int pattern)
    {
        base.Ban(node, pattern);

        int nbNodes = wave.Length;
        var waveTexData = _waveTex.GetPixelData<bool>( 0);
        waveTexData[pattern * nbNodes + node] = false;
        
        var compatibleTexData = _compatibleTex.GetPixelData<int>(0);
        compatibleTexData[(pattern * 4 + 0) * nbNodes + node] = 0;
        compatibleTexData[(pattern * 4 + 1) * nbNodes + node] = 0;
        compatibleTexData[(pattern * 4 + 2) * nbNodes + node] = 0;
        compatibleTexData[(pattern * 4 + 3) * nbNodes + node] = 0;
        
        Debug.Log($"{banCount++}");
        
        var numPossiblePatternTexData = _numPossiblePatternsTex.GetPixelData<int>(0);
        int doesThisHelp = numPossiblePatterns[node];
        numPossiblePatternTexData[node] = doesThisHelp;
        
        var memoisationTexData = _memoisationTex.GetPixelData<float>(0, 0);
        memoisationTexData[node] = (float)sumsOfWeights[node];
        memoisationTexData = _memoisationTex.GetPixelData<float>(0, 1);
        memoisationTexData[node] = (float) sumsOfWeightLogWeights[node];
        memoisationTexData = _memoisationTex.GetPixelData<float>( 0, 2);
        memoisationTexData[node] = (float) entropies[node];
        
        /* Update the collapse information for the compute shader. */
        var inIsCollapsedTexData = _inIsCollapsedTex.GetPixelData<bool>(0);
        inIsCollapsedTexData[node] = true;

        var inNeedsCollapseTexData = _inNeedsCollapseTex.GetPixelData<bool>( 0);
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
            inNeedsCollapseTexData[node2] = true;
        }

        var inPatternCollapsedTex = _inPatternCollapsedTex.GetPixelData<bool>(0);
        inPatternCollapsedTex[pattern * width * height + node] = true;
        openCells = true;
        byte[] resultBufData = {Convert.ToByte(isPossible), Convert.ToByte(openCells)};
        _resultBuf.SetData(resultBufData);
    }

    /// <summary>
    /// This should be called if the user wants to Ban a pattern from a node and directly upload those changes to the GPU.
    /// </summary>
    /// <param name="node"></param>
    /// <param name="pattern"></param>
    public void BanAndApply(int node, int pattern)
    {
        Ban(node, pattern);
        ApplyBanTextures();
    }

    /// <summary>
    /// This NEEDS to be called AFTER Ban in order to upload all texture changes to the GPU.
    /// Making this a separate call gives the option to first ban all wanted patterns and then upload the changes
    /// in one go. This should be much quicker.
    /// </summary>
    private void ApplyBanTextures()
    {
        _waveTex.Apply();
        _compatibleTex.Apply();
        _numPossiblePatternsTex.Apply();
        _memoisationTex.Apply();
        _inIsCollapsedTex.Apply();
        _inNeedsCollapseTex.Apply();
        _inPatternCollapsedTex.Apply();
    }

    private void CopyGpuWaveToCpu()
    { 
        _waveTex.GetPixelData<bool>(0).CopyTo(_waveCopyBuffer);
        
        Parallel.For(0, nbPatterns, pattern =>
        {
            int patternOffset = pattern * width * height;
            for (int node = 0; node < wave.Length; node++)
            {
                wave[node][pattern] = _waveCopyBuffer[node * patternOffset];
            }
        });
    }

    private void CopyGpuMemoisationToCpu()
    {
        _memoisationTex.GetPixelData<float>(0, 0).CopyTo(_sumOfWeightsFloat);
        _memoisationTex.GetPixelData<float>(0, 1).CopyTo(_sumsOfWeightLogWeightsFloat);
        _memoisationTex.GetPixelData<float>(0, 2).CopyTo(_entropiesFloat);

        for (int pattern = 0; pattern < nbPatterns; pattern++)
        {
            sumsOfWeights[pattern] = _sumOfWeightsFloat[pattern];
            sumsOfWeightLogWeights[pattern] = _sumsOfWeightLogWeightsFloat[pattern];
            entropies[pattern] = _entropiesFloat[pattern];
        }

        _numPossiblePatternsTex.GetPixelData<int>(0).CopyTo(numPossiblePatterns);
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
        _waveTex.Release();
        _memoisationTex.Release();
        _numPossiblePatternsTex.Release();
        _compatibleTex.Release();
        _inNeedsCollapseTex.Release();
        _outNeedsCollapseTex.Release();
        _inIsCollapsedTex.Release();
        _outIsCollapsedTex.Release();
        _inPatternCollapsedTex.Release();
        _outPatternCollapsedTex.Release();
    }
}
