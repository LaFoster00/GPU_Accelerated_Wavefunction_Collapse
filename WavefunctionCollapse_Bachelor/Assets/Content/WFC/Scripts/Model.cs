using System.Collections.Generic;
using Unity.Mathematics;
using Random = Unity.Mathematics.Random;

using PropagatorState = System.Collections.Generic.List<System.Collections.Generic.List<int>[]>;

public class Model
{
    #region Properties

    #region Private

    /**
   * The random number generator.
   */
    private Random _gen;

    /**
   * The distribution of the patterns as given in input.
   */
    private List<double> _patternsFrequencies;

    /**
   * The wave, indicating which patterns can be put in which cell.
   */
    private Wave _wave;

    /**
   * The number of distinct patterns.
   */
    private int _nbPatterns;

    /**
   * The propagator, used to propagate the information in the wave.
   */
    private Propagator _propagator;

    #endregion

    #region Public

    /*
      * Return value of observe.
      */
    public enum ObserveStatus
    {
        Success, // WFC has finished and has succeeded.
        Failure, // WFC has finished and failed.
        ToContinue // WFC isn't finished.
    };

    #endregion

    #endregion

    public Model(bool periodicOutput, int seed, List<double> patternsFrequencies,
        PropagatorState propagator, int waveHeight, int waveWidth)
    {
        _gen = Random.CreateFromIndex((uint) seed);
        this._patternsFrequencies = patternsFrequencies.NormalizeList();
        _wave = new Wave(waveHeight, waveWidth, ref patternsFrequencies);
        _nbPatterns = propagator.Count;
    }

    public (bool, int[,]) Run()
    {
        while (true)
        {

            // Define the value of an undefined cell.
            ObserveStatus result = Observe();

            // Check if the algorithm has terminated.
            if (result == ObserveStatus.Failure)
            {
                return (false, null);
            }
            else if (result == ObserveStatus.Success)
            {
                return (true, WaveToOutput());
            }

            // Propagate the information.
            _propagator.Propagate(_wave);
        }
    }

    /*
      * Define the value of the cell with lowest entropy.
      */
    public ObserveStatus Observe()
    {
        // Get the cell with lowest entropy.
        int argmin = _wave.GetMinEntropy(ref _gen);

        // If there is a contradiction, the algorithm has failed.
        if (argmin == -2)
        {
            return ObserveStatus.Failure;
        }

        // If the lowest entropy is 0, then the algorithm has succeeded and
        // finished.
        if (argmin == -1)
        {
            WaveToOutput(); // TODO this seems rather useless
            return ObserveStatus.Success;
        }

        // Choose an element according to the pattern distribution
        double s = 0;
        for (int k = 0; k < _nbPatterns; k++)
        {
            s += _wave.Get(argmin, k) ? _patternsFrequencies[k] : 0;
        }

        double randomValue = math.remap(0, 1, 0, s, _gen.NextDouble());
        int chosenValue = _nbPatterns - 1;

        for (int k = 0; k < _nbPatterns; k++)
        {
            randomValue -= _wave.Get(argmin, k) ? _patternsFrequencies[k] : 0;
            if (randomValue <= 0)
            {
                chosenValue = k;
                break;
            }
        }

        // And define the cell with the pattern.
        for (int k = 0; k < _nbPatterns; k++)
        {
            if (_wave.Get(argmin, k) != (k == chosenValue))
            {
                _propagator.AddToPropagator(argmin / _wave.Width, argmin % _wave.Width,
                    k);
                _wave.Set(argmin, k, false);
            }
        }

        return ObserveStatus.ToContinue;
    }

    /*
      * Propagate the information of the wave.
      */
    public void Propagate()
    {
        _propagator.Propagate(_wave);
    }

    /*
      * Remove pattern from cell (i,j).
      */
    public void RemoveWavePattern(int i, int j, int pattern)
    {
        if (_wave.Get(i, j, pattern))
        {
            _wave.Set(i, j, pattern, false);
            _propagator.AddToPropagator(i, j, pattern);
        }
    }

    /*
      * Transform the wave to a valid output (a 2d array of patterns that aren't in
      * contradiction). This function should be used only when all cell of the wave
      * are defined.
      */
    private int[,] WaveToOutput()
    {
        int[,] outputPatterns = new int[_wave.Height, _wave.Width];
        for (int i = 0; i < _wave.Size; i++)
        {
            for (int k = 0; k < _nbPatterns; k++)
            {
                if (_wave.Get(i, k))
                {
                    outputPatterns.SetValue(k, i);
                }
            }
        }

        return outputPatterns;
    }
}