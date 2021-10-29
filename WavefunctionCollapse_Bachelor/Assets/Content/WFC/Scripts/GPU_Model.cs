using System;
using System.Collections;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using WFC;
using Random = Unity.Mathematics.Random;

public class GPU_Model : Model, IDisposable
{
    private ComputeShader _propagatorShader;

    #region ShaderResources

    /*
    Actual wave result
    wave[int3(nodeX, nodeY, pattern)]
    */
    private Texture3D _waveTex;

    private ComputeBuffer _weightBuf;
    
    /*
    Packed textures holding
    depth 0 : sumOfWeights
    depth 1 : sumsOfWeightLogWeights
    depth 2 : entropies
    */
    private Texture2DArray _memoisationTex;

    private Texture2D _numPossiblePatternsTex;
    
    /* propagator[uint3(pattern, otherPattern, direction)] */
    private Texture3D _propagatorTex;
    
    /* compatible[int3(nodeX, nodeY, pattern-direction] */
    private Texture3D _compatibleTex;
    
    private ComputeBuffer _resultBuf;
    
    /* Neighbours of cells that changed. */
    Texture2D _inNeedsCollapseTex;
    Texture2D _outNeedsCollapseTex;

    /* Cells in which the patterns changed. */
    Texture2D _inIsCollapsedTex;
    Texture2D _outIsCollapsedTex;

    /*
    Which pattern changed.
    input_pattern_change[uint3(nodeX, nodeY, pattern)]
    */
    Texture3D _inPatternCollapsedTex;
    Texture3D _outPatternCollapsedTex;


    private bool[] _collapseClearData;
    private bool[] _patternCollapseClearData;
    #endregion
    

    [StructLayout(LayoutKind.Sequential)]
    private struct PropagatorResults
    {
        public bool isPossible;
        public bool openCells;
    }
    
    [StructLayout(LayoutKind.Sequential)]
    private struct Weight
    {
        public float weight;
        public float logWeight;
    };
    

    public GPU_Model(
        ComputeShader propagatorShader,
        int width, int height, int patternSize, bool periodic) : 
        base(width, height, patternSize, periodic)
    {
        _propagatorShader = propagatorShader;

        _memoisationTex = new Texture2DArray(width, height, 3, TextureFormat.RFloat, false);
        _numPossiblePatternsTex = new Texture2D(width, height, TextureFormat.RFloat, false);
        
        _inIsCollapsedTex = new Texture2D(width, height, TextureFormat.R8, false);
        _outIsCollapsedTex = new Texture2D(width, height, TextureFormat.R8, false);
        _inNeedsCollapseTex = new Texture2D(width, height, TextureFormat.R8, false);
        _outNeedsCollapseTex = new Texture2D(width, height, TextureFormat.R8, false);

        _resultBuf = new ComputeBuffer(1, sizeof(bool) * 2);

        _collapseClearData = new bool[wave.Length];
    }

    public override void SetData(int nbPatterns, double[] weights, (bool[][][] dense, int[][][] standard) propagator,
        PropagatorSettings propagatorSettings)
    {
        base.SetData(nbPatterns, weights, propagator, propagatorSettings);
        
        _waveTex = new Texture3D(width, height, nbPatterns, TextureFormat.R8, false);
        
        _weightBuf = new ComputeBuffer(weights.Length, sizeof(float) * 2);
        
        _propagatorTex = new Texture3D(nbPatterns, nbPatterns, 4, TextureFormat.R8, false);
        
        _compatibleTex =
            new Texture3D(width, height, nbPatterns * 4, GraphicsFormat.R32_SInt, TextureCreationFlags.None);
        
        _inPatternCollapsedTex = new Texture3D(width, height, nbPatterns, TextureFormat.R8, false);
        _outPatternCollapsedTex = new Texture3D(width, height, nbPatterns, TextureFormat.R8, false);
        
        _patternCollapseClearData = new bool[wave.Length * nbPatterns];
    }

    ~GPU_Model()
    {
        Dispose();
    }

    protected override void Init()
    {
        if (wave == null)
        {
            base.Init();
            base.Clear();

            {
                Weight[] weightBufData = new Weight[weights.Length];
                for (int pattern = 0; pattern < weights.Length; pattern++)
                {
                    weightBufData[pattern].weight = (float) weights[pattern];
                    weightBufData[pattern].logWeight = (float) weightLogWeights[pattern];
                }
                _weightBuf.SetData(weightBufData);
            }
            
            _resultBuf.SetData(new []{ new PropagatorResults() });

            {
                bool[] propagatorData = new bool[nbPatterns * nbPatterns * 4];
                Parallel.For(0, 4, dir =>
                {
                    int dirOffset = dir * nbPatterns * nbPatterns;
                    for (int pattern = 0; pattern < nbPatterns; pattern++)
                    {
                        int patternOffset = pattern * nbPatterns;
                        for (int otherPattern = 0; otherPattern < nbPatterns; otherPattern++)
                        {
                            propagatorData[otherPattern + patternOffset + dirOffset] = densePropagator[pattern][otherPattern][dir];
                        }
                    }
                });
                _propagatorTex.SetPixelData(propagatorData, 0);
                _propagatorTex.Apply();
            }
            
            _inIsCollapsedTex.SetPixelData(_collapseClearData, 0);
            _inNeedsCollapseTex.SetPixelData(_collapseClearData, 0);
            _inPatternCollapsedTex.SetPixelData(_patternCollapseClearData, 0);

            _inIsCollapsedTex.Apply();
            _inNeedsCollapseTex.Apply();
            _inPatternCollapsedTex.Apply();
            ClearOutBuffers();
            
            BindResources();
        }
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
        {
            bool[] waveTexData = new bool[wave.Length * nbPatterns];
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
            float[] sumOfWeightsData = new float[nbPatterns];
            float[] sumsOfWeightLogWeightsData = new float[nbPatterns];
            float[] entropiesData = new float[nbPatterns];

            for (int pattern = 0; pattern < nbPatterns; pattern++)
            {
                sumOfWeightsData[pattern] = (float) sumsOfWeights[pattern];
                sumsOfWeightLogWeightsData[pattern] = (float) sumsOfWeightLogWeights[pattern];
                entropiesData[pattern] = (float) entropies[pattern];
            }
            
            _memoisationTex.SetPixelData(sumOfWeightsData, 0, 0);
            _memoisationTex.SetPixelData(sumsOfWeightLogWeightsData, 0, 1);
            _memoisationTex.SetPixelData(entropiesData, 0, 2);
            _memoisationTex.Apply();
        }
        
        {
            _numPossiblePatternsTex.SetPixelData(numPossiblePatterns, 0);
        }

        {
            int[] compatibleData = new int[wave.Length * (nbPatterns * 4)];
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
                        compatibleData[x + yOffset + patternDirOffset] = compatible[x + yOffset][pattern][dir];
                    }
                }
            });
            _compatibleTex.SetPixelData(compatibleData, 0);
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
        public PropagatorResults[] propagatorResult;
    }
    
    public override IEnumerator Run(uint seed, int limit, WFC_Result result)
    {
        Init();
        Clear();

        WFC_Objects objects = new WFC_Objects()
        {
            random = new Random(seed),
            propagatorResult = new PropagatorResults[1],
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
        while (objects.propagatorResult[0].openCells)
        {
            _propagatorShader.Dispatch(
                0,
                (int) Math.Ceiling(width / 16.0f),
                (int) Math.Ceiling(height / 16.0f),
                1);
            
            /* Copy result of Compute operation back to CPU buffer. */
            _resultBuf.GetData(objects.propagatorResult);
            
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

    protected override void Observe(int node, ref Random random)
    {
        base.Observe(node, ref random);
        ApplyBanTextures();
    }
    
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
        
        _waveTex.SetPixelData(new []{false}, 0, pattern * nbNodes + node);

        _compatibleTex.SetPixelData(new []{0}, 0, (pattern * 4 + 0) * nbNodes + node);
        _compatibleTex.SetPixelData(new []{0}, 0, (pattern * 4 + 1) * nbNodes + node);
        _compatibleTex.SetPixelData(new []{0}, 0, (pattern * 4 + 2) * nbNodes + node);
        _compatibleTex.SetPixelData(new []{0}, 0, (pattern * 4 + 3) * nbNodes + node);
        
        _numPossiblePatternsTex.SetPixelData(new []{numPossiblePatterns[node]}, 0, node);

        _memoisationTex.SetPixelData(new []{(float)sumsOfWeights[node]}, 0, 0,node);
        _memoisationTex.SetPixelData(new []{(float)sumsOfWeightLogWeights[node]}, 0, 1, node);
        _memoisationTex.SetPixelData(new []{(float)entropies[node]}, 0, 2, node);
        
        /* Update the collapse information for the compute shader. */
        _inIsCollapsedTex.SetPixelData(new []{true}, 0, node);

        int x = node % width;
        int y = node / width;
        for (int dir = 0; dir < 4; dir++)
        {
            int x2 = x + Directions.DirectionsX[dir];
            int y2 = y + Directions.DirectionsY[dir];
            int node2 = x2 + y2 * width;
            _inNeedsCollapseTex.SetPixelData(new[] {true}, 0, node2);
        }

        _inPatternCollapsedTex.SetPixelData(new []{true}, 0, pattern * width * height + node);
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
        /* TODO: This could be slower and cause problems. Check that. */
        Parallel.Invoke(
            _waveTex.Apply,
            _compatibleTex.Apply, 
            _numPossiblePatternsTex.Apply, 
            _memoisationTex.Apply,
            _inIsCollapsedTex.Apply,
            _inNeedsCollapseTex.Apply,
            _inPatternCollapsedTex.Apply);
    }

    private void CopyGpuWaveToCpu()
    {
        var waveTexData = _waveTex.GetPixelData<bool>(0);
        
        Parallel.For(0, nbPatterns, pattern =>
        {
            int patternOffset = pattern * width * height;
            for (int node = 0; node < wave.Length; node++)
            {
                wave[node][pattern] = waveTexData[node * patternOffset];
            }
        });
        
        waveTexData.Dispose();
    }

    private void CopyGpuMemoisationToCpu()
    {
        var sumOfWeightsTexData = _memoisationTex.GetPixelData<float>(0, 0);
        var sumsOfWeightLogWeightsTexData = _memoisationTex.GetPixelData<float>(0, 1);
        var entropiesTexData = _memoisationTex.GetPixelData<float>(0, 2);

        for (int pattern = 0; pattern < nbPatterns; pattern++)
        {
            sumsOfWeights[pattern] = sumOfWeightsTexData[pattern];
            sumsOfWeightLogWeights[pattern] = sumsOfWeightLogWeightsTexData[pattern];
            entropies[pattern] = entropiesTexData[pattern];
        }

        sumOfWeightsTexData.Dispose();
        sumsOfWeightLogWeightsTexData.Dispose();
        entropiesTexData.Dispose();

        var numPossiblePatternsTexData = _numPossiblePatternsTex.GetPixelData<int>(0);
        
        //TODO: Timer test these loops against Parallel.For loops to see what would be faster.
        for (int node = 0; node < wave.Length; node++)
        {
            numPossiblePatterns[node] = numPossiblePatternsTexData[node];
        }
        
        numPossiblePatternsTexData.Dispose();
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
        _weightBuf?.Dispose();
        _resultBuf?.Dispose();
    }
}
