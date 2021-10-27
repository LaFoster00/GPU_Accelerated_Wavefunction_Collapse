using System;
using System.Collections;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using WFC;

public class GPU_Model : Model, IDisposable
{
    private ComputeShader _propagatorShader;
    private ComputeShader _clearDataShader;

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
        public uint openCells;
    }
    
    [StructLayout(LayoutKind.Sequential)]
    private struct Weight
    {
        public float weight;
        public float logWeight;
    };
    

    public GPU_Model(
        ComputeShader propagatorShader, 
        ComputeShader clearDataShader,
        int width, int height, int patternSize, bool periodic, int nbPatterns, 
        double[] weights,
        (bool[][][] dense, int[][][] standard) propagator,
        PropagatorSettings propagatorSettings) : 
        base(width, height, patternSize, periodic, nbPatterns, weights, propagator, propagatorSettings)
    {
        _propagatorShader = propagatorShader;
        _clearDataShader = clearDataShader;
        
        _waveTex = new Texture3D(width, height, nbPatterns, TextureFormat.R8, false);
        
        _weightBuf = new ComputeBuffer(weights.Length, sizeof(float) * 2);
        _memoisationTex = new Texture2DArray(width, height, 3, TextureFormat.RFloat, false);
        _numPossiblePatternsTex = new Texture2D(width, height, TextureFormat.RFloat, false);
        
        _propagatorTex = new Texture3D(nbPatterns, nbPatterns, 4, TextureFormat.R8, false);
        
        _compatibleTex =
            new Texture3D(width, height, nbPatterns * 4, GraphicsFormat.R32_SInt, TextureCreationFlags.None);
        
        _inIsCollapsedTex = new Texture2D(width, height, TextureFormat.R8, false);
        _outIsCollapsedTex = new Texture2D(width, height, TextureFormat.R8, false);
        _inNeedsCollapseTex = new Texture2D(width, height, TextureFormat.R8, false);
        _outNeedsCollapseTex = new Texture2D(width, height, TextureFormat.R8, false);
        _inPatternCollapsedTex = new Texture3D(width, height, nbPatterns, TextureFormat.R8, false);
        _outPatternCollapsedTex = new Texture3D(width, height, nbPatterns, TextureFormat.R8, false);
        
        _resultBuf = new ComputeBuffer(1, sizeof(bool) * 2);

        _collapseClearData = new bool[wave.Length];
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
        }
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

    public override IEnumerator Run(uint seed, int limit, WFC_Result result)
    {
        Init();
        Clear();
        
        /* Dispatch code here! */
        
        return null;
    }

    public override void Ban(int node, int pattern)
    {
    }

    public void Dispose()
    {
        _weightBuf?.Dispose();
        _resultBuf?.Dispose();
    }
}
