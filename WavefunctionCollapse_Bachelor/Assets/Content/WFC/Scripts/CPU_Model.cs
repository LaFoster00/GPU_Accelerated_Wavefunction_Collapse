using System;
using System.Collections;
using System.Threading.Tasks;
using UnityEngine;
using WFC;
using Random = Unity.Mathematics.Random;

public class CPU_Model : Model
{
    public CPU_Model(int width, int height, int patternSize, bool periodic, int nbPatterns, double[] weights, int[][][] propagator, PropagatorSettings propagatorSettings) 
        : base(width, height, patternSize, periodic, nbPatterns, weights, propagator, propagatorSettings)
    {
    }

    private void Init()
    {
        wave = new bool[width * height][];
        compatible = new int[wave.Length][][];
        for (int i = 0; i < wave.Length; i++)
        {
            wave[i] = new bool[nbPatterns];
            compatible[i] = new int[nbPatterns][];
            for (int t = 0; t < nbPatterns; t++)
                compatible[i][t] = new int[4];
        }

        distribution = new double[nbPatterns];
        observed = new int[width * height];

        weightLogWeights = new double[nbPatterns];
        totalSumOfWeights = 0;
        totalSumOfWeightLogWeights = 0;

        for (int t = 0; t < nbPatterns; t++)
        {
            weightLogWeights[t] = weights[t] * Math.Log(weights[t]);
            totalSumOfWeights += weights[t];
            totalSumOfWeightLogWeights += weightLogWeights[t];
        }

        startingEntropy = Math.Log(totalSumOfWeights) - totalSumOfWeightLogWeights / totalSumOfWeights;

        numPossiblePatterns = new int[width * height];
        sumsOfWeights = new double[width * height];
        sumsOfWeightLogWeights = new double[width * height];
        entropies = new double[width * height];

        stack = new (int, int)[wave.Length * nbPatterns];
        stackSize = 0;
        stepInfo.propagatingCells = stack;
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
            random = new Random(seed),
        };

        if (limit < 0)
        {
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
        }
        else
        {
            for (int i = 0; i < limit; i++)
            {
                if (result.finished) break;

                if (propagatorSettings.debug == PropagatorSettings.DebugMode.None)
                {
                    Run_Internal(objects, result).MoveNext();
                }
                else
                {
                    yield return Run_Internal(objects, result);
                }
            }
        }

        if (!isPossible)
        {
            yield return DebugDrawCurrentState();
        }
    }
    
    private IEnumerator Run_Internal(WFC_Objects objects, WFC_Result result)
        {
            int node = NextUnobservedNode(objects.random);
            if (node >= 0)
            {
                Observe(node, ref objects.random);

                var propagation = Propagate();
                propagation.MoveNext();
                while (propagation.MoveNext())
                {
                    yield return propagation.Current;
                }

                if (!isPossible)
                {
                    result.output = null;
                    result.success =  false;
                    result.finished = true;
                }
            }
            else
            {
                result.output = WaveToOutput();
                result.success =  true;
                result.finished = true;
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

        private void Observe(int node, ref Random random)
        {
            // Choose an element according to the pattern distribution
            bool[] w = wave[node];
            for (int pattern = 0; pattern < nbPatterns; pattern++)
            {
                distribution[pattern] = w[pattern] ? weights[pattern] : 0.0;
            }

            int r = distribution.RandomFromDistribution(random.NextDouble());
            for (int pattern = 0; pattern < nbPatterns; pattern++)
                if (w[pattern] != (pattern == r))
                    Ban(node, pattern);
        }

        private IEnumerator Propagate()
        {
            while (stackSize > 0)
            {
                /* Remove all incompatible patterns resulting from previously collapsed nodes. */
                (int node, int pattern) = stack[stackSize - 1];
                stackSize--;
                stepInfo.numPropagatingCells = stackSize;

                stepInfo.currentTile.x = node % width;
                stepInfo.currentTile.y = node / width;
                
                for (int direction = 0; direction < 4; direction++)
                {
                    stepInfo.targetTile.x = stepInfo.currentTile.x + Directions.DirectionsX[direction];
                    stepInfo.targetTile.y = stepInfo.currentTile.y + Directions.DirectionsY[direction];
                    if (periodic)
                    {
                       stepInfo.targetTile.x = (stepInfo.targetTile.x + width) % width;
                       stepInfo.targetTile.y = (stepInfo.targetTile.y + height) % height;
                    }
                    else if (!periodic && (stepInfo.targetTile.x < 0
                                           || stepInfo.targetTile.y < 0
                                           || stepInfo.targetTile.x + patternSize > width 
                                           || stepInfo.targetTile.y + patternSize > height))
                    {
                        continue;
                    }
                    
                    int node2 = stepInfo.targetTile.x + stepInfo.targetTile.y * width;
                    int[] patterns = propagator[pattern][direction];
                    int[][] compatiblePatterns = compatible[node2];

                    foreach (var pat in patterns)
                    {
                        int[] compatPattern = compatiblePatterns[pat];

                        compatPattern[direction]--;
                        /*
                         If the element was set to 0 with this operation, we need to remove
                         the pattern from the wave, and propagate the information
                         */
                        if (compatPattern[direction] == 0)
                        {
                            Ban(node2, pat);
                            if (!isPossible)
                            {
                                yield break;
                            }
                            if (propagatorSettings.debug == PropagatorSettings.DebugMode.OnSet)
                            {
                                yield return DebugDrawCurrentState();
                            }
                        }

                        if (propagatorSettings.debug == PropagatorSettings.DebugMode.OnChange)
                        {
                            yield return DebugDrawCurrentState();
                        }
                    }
                }
            }
        }
        
        private void Clear()
        {
            Parallel.For(0, wave.Length, node =>
            {
                for (int pattern = 0; pattern < nbPatterns; pattern++)
                {
                    wave[node][pattern] = true;
                    for (int direction = 0; direction < 4; direction++)
                        compatible[node][pattern][direction] =
                            propagator[pattern][Directions.GetOppositeDirection(direction)].Length;
                }

                numPossiblePatterns[node] = weights.Length;
                sumsOfWeights[node] = totalSumOfWeights;
                sumsOfWeightLogWeights[node] = totalSumOfWeightLogWeights;
                entropies[node] = startingEntropy;
                observed[node] = -1;
            });
            
            observedSoFar = 0;
            isPossible = true;
        }
        
        /*
         Transform the wave to a valid output (a 2d array of patterns that aren't in
         contradiction). This function should be used only when all cell of the wave
         are defined.
        */
        private int[,] WaveToOutput()
        {
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
            propagatorSettings.debugToOutput(stepInfo, wave, propagatorSettings.orientedToTileId);
            yield return propagatorSettings.stepInterval == 0
                ? null
                : new WaitForSeconds(propagatorSettings.stepInterval);
        }
}