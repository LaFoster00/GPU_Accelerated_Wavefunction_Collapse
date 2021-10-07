using System.Collections.Generic;
using Unity.Mathematics;
using Random = Unity.Mathematics.Random;

public class Model
{
    #region Properties

    #region Private

    /* The random number generator. */
    private Random _gen;

    /* The distribution of the patterns as given in input. */
    protected double[] patternsFrequencies;

    /* The wave, indicating which patterns can be put in which cell. */
    protected Wave wave;

    /* The number of distinct patterns. */
    protected int nbPatterns;

    /* The propagator, used to propagate the information in the wave. */
    private Propagator _propagator;
    
    /* Wave output dimensions */
    protected readonly int waveHeight, waveWidth;
    
    /* If the ouput will be periodic */
    protected readonly bool periodicOutput;

    #endregion

    #region Public

    /* Return value of observe. */
    public enum ObserveStatus
    {
        Success, // WFC has finished and has succeeded.
        Failure, // WFC has finished and failed.
        ToContinue // WFC isn't finished.
    };

    #endregion

    #endregion

    public Model(bool periodicOutput, int seed, int waveHeight, int waveWidth)
    {
        _gen = Random.CreateFromIndex((uint) seed);
        this.waveHeight = waveHeight;
        this.waveWidth = waveWidth;
        this.periodicOutput = periodicOutput;
    }

    protected void Init(double[] patternsFrequencies, List<int>[][] propagatorState)
    {
        this.patternsFrequencies = patternsFrequencies.Normalize();
        wave = new Wave(waveHeight, waveWidth, this.patternsFrequencies);
        nbPatterns = propagatorState.Length;
        _propagator = new Propagator(waveHeight, waveWidth, periodicOutput, propagatorState);
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
            _propagator.Propagate(wave);
        }
    }

    /*
      * Define the value of the cell with lowest entropy.
      */
    public ObserveStatus Observe()
    {
        // Get the cell with lowest entropy.
        int argmin = wave.GetMinEntropy(ref _gen);

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
        for (int k = 0; k < nbPatterns; k++)
        {
            s += wave.Get(argmin, k) ? patternsFrequencies[k] : 0;
        }

        double randomValue = math.remap(0, 1, 0, s, _gen.NextDouble());
        int chosenValue = nbPatterns - 1;

        for (int k = 0; k < nbPatterns; k++)
        {
            randomValue -= wave.Get(argmin, k) ? patternsFrequencies[k] : 0;
            if (randomValue <= 0)
            {
                chosenValue = k;
                break;
            }
        }

        // And define the cell with the pattern.
        for (int k = 0; k < nbPatterns; k++)
        {
            if (wave.Get(argmin, k) != (k == chosenValue))
            {
                _propagator.AddToPropagator(argmin / wave.width, argmin % wave.width,
                    k);
                wave.Set(argmin, k, false);
            }
        }

        return ObserveStatus.ToContinue;
    }

    /*
      * Propagate the information of the wave.
      */
    public void Propagate()
    {
        _propagator.Propagate(wave);
    }

    /*
      * Remove pattern from cell (i,j).
      */
    public void RemoveWavePattern(int y, int x, int pattern)
    {
        if (wave.Get(y, x, pattern))
        {
            wave.Set(y, x, pattern, false);
            _propagator.AddToPropagator(y, x, pattern);
        }
    }

    /*
      * Transform the wave to a valid output (a 2d array of patterns that aren't in
      * contradiction). This function should be used only when all cell of the wave
      * are defined.
      */
    private int[,] WaveToOutput()
    {
        int[,] outputPatterns = new int[wave.height, wave.width];
        for (int i = 0; i < wave.size; i++)
        {
            for (int k = 0; k < nbPatterns; k++)
            {
                if (wave.Get(i, k))
                {
                    outputPatterns.SetValue(k, i);
                }
            }
        }

        return outputPatterns;
    }
}