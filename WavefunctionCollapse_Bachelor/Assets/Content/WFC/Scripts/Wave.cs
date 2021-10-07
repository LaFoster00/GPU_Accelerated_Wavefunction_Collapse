using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Unity.Mathematics;
using Random = Unity.Mathematics.Random;

/*
 * Struct containing the values needed to compute the entropy of all the cells.
 * This struct is updated every time the wave is changed.
 * p'(pattern) is equal to patterns_frequencies[pattern] if wave.get(cell,
 * pattern) is set to true, otherwise 0.
 */
public struct EntropyMemoisation
{
 public double[] PlogpSum; // The sum of p'(pattern) * log(p'(pattern)).
 public double[] Sum; // The sum of of p'(pattern).
 public double[] LogSum; // The log sum.
 public int[] NbPatterns; // The number of patterns present.
 public double[] Entropy; // The entropy of the cell.
}

/*
 * Contains the pattern possibilities in every cell.
 * Also contains information about cell entropy.
 */
public class Wave
{
    #region Properties

    #region Private

    /* The patterns frequencies p given to wfc. */
    private readonly List<double> _patternFrequencies;

    /* The precomputation of p * log(p). */
    private readonly List<double> _plogPatternFrequencies;

    /* The precomputation of min (p * log(p)) / 2.
     This is used to define the maximum value of the noise. */
    private readonly double _minAbsHalfPlogp;

    /* The memoisation of important values for the computation of entropy. */
    private EntropyMemoisation _memoisation;

    /*
     This value is set to true if there is a contradiction in the wave (all
     elements set to false in a cell). 
    */
    private bool _isImpossible;

    /* The number of distinct patterns; */
    private readonly int _nbPatterns;

    /*
     The actual wave. data[index, pattern] is equal to true if the pattern can
     be placed in the cell index. 
    */
    private bool[,,] _data;

    #endregion

    #region Public

    /* The size of the Wave */
    public readonly int width;
    public readonly int height;
    public readonly int size;

    #endregion

    #endregion

    /* Return distribution * log(distribution). */
    List<double> GetPlogp(ref List<double> distribution)
    {
        List<double> plogp = new List<double>(distribution.Count);
        for (int i = 0; i < distribution.Count; i++)
        {
            plogp.Add(distribution[i] * math.log(distribution[i]));
        }

        return plogp;
    }

    /* Return min(v) / 2. */
    double GetMinAbsHalf(ref List<double> v)
    {
        double minAbsHalf = Single.MaxValue;
        for (int i = 0; i < v.Count; i++)
        {
            minAbsHalf = math.min(minAbsHalf, math.abs(v[i] / 2.0));
        }

        return minAbsHalf;
    }

    public Wave(int height, int width, double[] patternFrequencies)
    {
        #region Init Members

        this.width = width;
        this.height = height;
        size = width * height;
        _patternFrequencies = new List<double>(patternFrequencies);
        _plogPatternFrequencies = GetPlogp(ref _patternFrequencies);
        _minAbsHalfPlogp = GetMinAbsHalf(ref _plogPatternFrequencies);
        _isImpossible = false;
        _nbPatterns = patternFrequencies.Length;
        _data = new bool[height, width, _nbPatterns];
        Parallel.For(0, _data.Length, index => { _data.SetValue(true, index); });

        #endregion

        // Initialize the memoisation of entropy.
        double baseEntropy = 0;
        double baseS = 0;
        for (int i = 0; i < _nbPatterns; i++)
        {
            baseEntropy += _plogPatternFrequencies[i];
            baseS += patternFrequencies[i];
        }

        double logBaseS = math.log(baseS);
        double entropyBase = logBaseS - baseEntropy / baseS;
        _memoisation.PlogpSum = new double[height * width];
        _memoisation.Sum = Enumerable.Repeat(baseS, height * width).ToArray();
        _memoisation.LogSum = Enumerable.Repeat(logBaseS, height * width).ToArray();
        _memoisation.NbPatterns = Enumerable.Repeat(_nbPatterns, height * width).ToArray();
        _memoisation.Entropy = Enumerable.Repeat(entropyBase, height * width).ToArray();
    }

    /* Return true if pattern can be placed in cell index. */
    public bool Get(int index, int pattern)
    {
        int y = index / width;
        int x = index % width;
        return !_data[y, x, pattern];
    }

    /* Return true if pattern can be placed in cell (i,j) */
    public bool Get(int y, int x, int pattern)
    {
        return Get(y * width + x, pattern);
    }

    /* Set the value of pattern in cell index. */
    public void Set(int index, int pattern, bool value)
    {
        int y = index / width;
        int x = index % width;
        bool oldValue = _data[y, x, pattern];
        // If the value isn't changed, nothing needs to be done.
        if (oldValue == value)
        {
            return;
        }

        // Otherwise, the memoisation should be updated.
        _data[y, x, pattern] = value;
        _memoisation.PlogpSum[index] -= _plogPatternFrequencies[pattern];
        _memoisation.Sum[index] -= _patternFrequencies[pattern];
        _memoisation.LogSum[index] = math.log(_memoisation.Sum[index]);
        _memoisation.NbPatterns[index]--;
        _memoisation.Entropy[index] =
            _memoisation.LogSum[index] -
            _memoisation.PlogpSum[index] / _memoisation.Sum[index];
        // If there is no patterns possible in the cell, then there is a
        // contradiction.
        if (_memoisation.NbPatterns[index] == 0)
        {
            _isImpossible = true;
        }
    }

    /* Set the value of pattern in cell (i,j). */
    public void Set(int i, int j, int pattern, bool value)
    {
        Set(i * width + j, pattern, value);
    }

    /* Return the index of the cell with lowest entropy different of 0.
      If there is a contradiction in the wave, return -2.
      If every cell is decided, return -1. */
    public int GetMinEntropy(ref Random gen)
    {
        if (_isImpossible)
        {
            return -2;
        }

        // The minimum entropy (plus a small noise)
        double min = Double.MaxValue;
        int argmin = -1;

        for (int i = 0; i < size; i++)
        {

            // If the cell is decided, we do not compute the entropy (which is equal
            // to 0).
            if (_memoisation.NbPatterns[i] == 1)
            {
                continue;
            }

            // Otherwise, we take the memoised entropy.
            double entropy = _memoisation.Entropy[i];

            // We first check if the entropy is less than the minimum.
            // This is important to reduce noise computation (which is not
            // negligible).
            if (entropy <= min)
            {

                // Then, we add noise to decide randomly which will be chosen.
                // noise is smaller than the smallest p * log(p), so the minimum entropy
                // will always be chosen.
                double noise = math.remap(0, 1, 0, _minAbsHalfPlogp, gen.NextDouble());
                if (entropy + noise < min)
                {
                    min = entropy + noise;
                    argmin = i;
                }
            }
        }

        return argmin;
    }
}