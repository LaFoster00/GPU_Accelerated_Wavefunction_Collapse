using System;
using System.Collections;
using System.Threading.Tasks;
using UnityEngine;
using WFC;

public abstract class CPU_Model : Model
{
    /* Wave data, wave[node][pattern] */
    protected bool[][] wave;
    
    /*
     Which patterns can be placed in which direction of the current pattern
     propagator[pattern][direction] : int[] possibilities
     */
    protected int[][][] propagator;
    
    /* Which cells are fully observed. */
    protected int[] observed;

    protected int[] numPossiblePatterns;
    protected double[] distribution, weightLogWeights, sumsOfWeights, sumsOfWeightLogWeights, entropies;

    protected CPU_Model(int width, int height, int patternSize, bool periodic) : base(width, height, patternSize, periodic)
    {
    }

    public override void SetData(int nbPatterns, double[] weights, (bool[][][] dense, int[][][] standard) propagator,
        PropagatorSettings propagatorSettings)
    {
        base.SetData(nbPatterns, weights, propagator, propagatorSettings);
        this.propagator = propagator.standard;
    }
}