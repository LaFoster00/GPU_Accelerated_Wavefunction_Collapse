using System;
using System.Collections;
using System.ComponentModel;
using System.Security;
using System.Threading.Tasks;
using UnityEngine;
using USCSL;
using Random = Unity.Mathematics.Random;

namespace WFC
{
    public class Model
    {
        /* Wave data, wave[node][pattern] */
        protected bool[][] wave;

        /*
         Which patterns can be placed in which direction of the current node
         propagator[pattern][direction] : int[] possibilities
         */
        protected int[][][] propagator;
        
        /*  */
        private int[][][] _compatible;
        
        /* Which cells are fully observed. */
        protected int[] observed;

        /* Contains node and the deleted pattern */
        private (int, int)[] _stack;
        private int _stackSize, _observedSoFar;

        
        protected readonly int width, height, nbPatterns, patternSize;
        protected bool periodic;

        protected double[] weights;
        private double[] _weightLogWeights, _distribution;

        private int[] _numPossiblePatterns;
        private double _totalSumOfWeights, _totalSumOfWeightLogWeights, _startingEntropy;
        private double[] _sumsOfWeights, _sumsOfWeightLogWeights, _entropies;

        public enum Heuristic
        {
            Entropy,
            MostRemainingPatterns,
            Scanline
        };

        Heuristic heuristic;

        public class PropagatorSettings
        {
            public enum DebugMode
            {
                None,
                OnChange,
                OnSet
            }

            public DebugMode debug;
            public float stepInterval;
            public Action<StepInfo, bool[][], (int, int)[]> debugToOutput;
            public (int, int)[] orientedToTileId;

            public PropagatorSettings(DebugMode debug, float stepInterval, Action<StepInfo, bool[][], (int, int)[]> debugToOutput)
            {
                this.debug = debug;
                this.stepInterval = stepInterval;
                this.debugToOutput = debugToOutput;
            }
        }

        protected PropagatorSettings propagatorSettings;
        
        public class StepInfo
        {
            public int width, height;
            public (int y, int x) currentTile;
            public (int y, int x) targetTile;
            public (int, int)[] propagatingCells;
            public int numPropagatingCells;
        }

        protected StepInfo stepInfo = new StepInfo();
        
        protected Model(int width, int height, int patternSize, bool periodic, Heuristic heuristic, PropagatorSettings propagatorSettings)
        {
            this.width = width;
            this.height = height;
            stepInfo.width = width;
            stepInfo.height = height;
            this.patternSize = patternSize;
            this.periodic = periodic;
            this.heuristic = heuristic;
            this.propagatorSettings = propagatorSettings;
        }

        void Init()
        {
            wave = new bool[width * height][];
            _compatible = new int[wave.Length][][];
            for (int i = 0; i < wave.Length; i++)
            {
                wave[i] = new bool[nbPatterns];
                _compatible[i] = new int[nbPatterns][];
                for (int t = 0; t < nbPatterns; t++) 
                    _compatible[i][t] = new int[4];
            }

            _distribution = new double[nbPatterns];
            observed = new int[width * height];

            _weightLogWeights = new double[nbPatterns];
            _totalSumOfWeights = 0;
            _totalSumOfWeightLogWeights = 0;

            for (int t = 0; t < nbPatterns; t++)
            {
                _weightLogWeights[t] = weights[t] * Math.Log(weights[t]);
                _totalSumOfWeights += weights[t];
                _totalSumOfWeightLogWeights += _weightLogWeights[t];
            }

            _startingEntropy = Math.Log(_totalSumOfWeights) - _totalSumOfWeightLogWeights / _totalSumOfWeights;

            _numPossiblePatterns = new int[width * height];
            _sumsOfWeights = new double[width * height];
            _sumsOfWeightLogWeights = new double[width * height];
            _entropies = new double[width * height];

            _stack = new (int, int)[wave.Length * nbPatterns];
            _stackSize = 0;
            stepInfo.propagatingCells = _stack;
        }
        
        public class WFC_Result
        {
            public bool finished;
            public bool success;
            public int[,] output;
        }

        public IEnumerator Run(uint seed, int limit, WFC_Result result)
        {
            if (wave == null) Init();

            Clear();
            Random random = new Random(seed);

            if (limit < 0)
            {
                while (!result.finished)
                {
                    if (propagatorSettings.debug == PropagatorSettings.DebugMode.None)
                    {
                        Run_Internal(random, result).MoveNext();
                    }
                    else
                    {
                        yield return Run_Internal(random, result);
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
                        Run_Internal(random, result).MoveNext();
                    }
                    else
                    {
                        yield return Run_Internal(random, result);
                    }
                }
            }
        }

        private IEnumerator Run_Internal(Random random, WFC_Result result)
        {
            int node = NextUnobservedNode(random);
            if (node >= 0)
            {
                Observe(node, random);
                var propagationResult = new PropagatorResult();
                
                var propagation = Propagate(propagationResult);
                propagation.MoveNext();
                while (propagation.MoveNext())
                {
                    yield return propagation.Current;
                }

                if (!propagationResult.success)
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

        protected int NextUnobservedNode(Random random)
        {
            if (heuristic == Heuristic.Scanline)
            {
                for (int i = _observedSoFar; i < wave.Length; i++)
                {
                    if (!periodic && (i % width + patternSize > width || i / width + patternSize > height)) continue;
                    if (_numPossiblePatterns[i] > 1)
                    {
                        _observedSoFar = i + 1;
                        return i;
                    }
                }

                return -1;
            }

            double min = Double.MaxValue;
            int argmin = -1;
            for (int node = 0; node < wave.Length; node++)
            {
                if (!periodic && (node % width + patternSize > width || node / width + patternSize > height)) continue;
                int remainingValues = _numPossiblePatterns[node];
                double entropy = heuristic == Heuristic.Entropy ? _entropies[node] : remainingValues;
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

        void Observe(int node, Random random)
        {
            // Choose an element according to the pattern distribution
            bool[] w = wave[node];
            for (int pattern = 0; pattern < nbPatterns; pattern++)
            {
                _distribution[pattern] = w[pattern] ? weights[pattern] : 0.0;
            }

            int r = _distribution.RandomFromDistribution(random.NextDouble());
            for (int pattern = 0; pattern < nbPatterns; pattern++)
                if (w[pattern] != (pattern == r))
                    Ban(node, pattern);
        }

        protected class PropagatorResult
        {
            public bool success;
            
            public static implicit operator bool(PropagatorResult x)
            {
                return x.success;
            }
        }

        protected IEnumerator Propagate(PropagatorResult result)
        {
            while (_stackSize > 0)
            {
                /* Remove all incompatible patterns resulting from previously collapsed nodes. */
                (int node, int pattern) = _stack[_stackSize - 1];
                _stackSize--;
                stepInfo.numPropagatingCells = _stackSize;

                stepInfo.currentTile.x = node % width;
                stepInfo.currentTile.y = node / width;
                
                for (int direction = 0; direction < 4; direction++)
                {
                    stepInfo.targetTile.x = stepInfo.currentTile.x + Directions.DirectionsX[direction];
                    stepInfo.targetTile.y = stepInfo.currentTile.y + Directions.DirectionsY[direction];
                    if (periodic)
                    {
                       stepInfo.targetTile.x %= width;
                       stepInfo.targetTile.y %= height;
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
                    int[][] compatiblePatterns = _compatible[node2];

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

            result.success = _numPossiblePatterns[0] > 0;
        }

        protected void Ban(int node, int pattern)
        {
            wave[node][pattern] = false;

            int[] comp = _compatible[node][pattern];
            for (int d = 0; d < 4; d++)
                comp[d] = 0;
            _stack[_stackSize] = (node, pattern);
            _stackSize++;

            _numPossiblePatterns[node] -= 1;
            _sumsOfWeights[node] -= weights[pattern];
            _sumsOfWeightLogWeights[node] -= _weightLogWeights[pattern];

            double sum = _sumsOfWeights[node];
            _entropies[node] = Math.Log(sum) - _sumsOfWeightLogWeights[node] / sum;
        }

        protected virtual void Clear()
        {
            Parallel.For(0, wave.Length, node =>
            {
                for (int pattern = 0; pattern < nbPatterns; pattern++)
                {
                    wave[node][pattern] = true;
                    for (int direction = 0; direction < 4; direction++)
                        _compatible[node][pattern][direction] =
                            propagator[pattern][Directions.GetOppositeDirection(direction)].Length;
                }

                _numPossiblePatterns[node] = weights.Length;
                _sumsOfWeights[node] = _totalSumOfWeights;
                _sumsOfWeightLogWeights[node] = _totalSumOfWeightLogWeights;
                _entropies[node] = _startingEntropy;
                observed[node] = -1;
            });
            
            _observedSoFar = 0;
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
        
        public IEnumerator DebugDrawCurrentState()
        {
            propagatorSettings.debugToOutput(stepInfo, wave, propagatorSettings.orientedToTileId);
            yield return propagatorSettings.stepInterval == 0
                ? null
                : new WaitForSeconds(propagatorSettings.stepInterval);
        }
    }
}