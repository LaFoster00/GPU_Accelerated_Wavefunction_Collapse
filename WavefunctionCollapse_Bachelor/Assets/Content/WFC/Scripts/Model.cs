using System;
using System.Collections;
using System.Threading.Tasks;
using Random = Unity.Mathematics.Random;

namespace WFC
{
    public abstract class Model
    {
        /* Wave data, wave[node][pattern] */
        protected bool[][] wave;

        /*
         Which patterns can be placed in which direction of the current pattern
         propagator[pattern][direction] : int[] possibilities
         */
        protected int[][][] propagator;

        /*
         Which patterns can be placed in which direction of the current node
         propagator[pattern][direction] : bool[] compatibles
         */
        protected bool[][][] densePropagator;
        
        /* int[wave.length][nbPatterns][4 : direction] */
        protected int[][][] compatible;
        
        /* Which cells are fully observed. */
        protected int[] observed;


        public readonly int width, height, patternSize;
        protected int nbPatterns;
        protected bool periodic;

        protected double[] weights;
        protected double[] weightLogWeights, distribution;

        protected bool isPossible;
        protected int[] numPossiblePatterns;
        protected double totalSumOfWeights, totalSumOfWeightLogWeights, startingEntropy;
        protected double[] sumsOfWeights, sumsOfWeightLogWeights, entropies;

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

        protected Model(int width, int height, int patternSize, bool periodic)
        {
            this.width = width;
            this.height = height;
            stepInfo.width = width;
            stepInfo.height = height;
            this.patternSize = patternSize;
            this.periodic = periodic;
        }

        public virtual void SetData(int nbPatterns, double[] weights,
            (bool[][][] dense, int[][][] standard) propagator, PropagatorSettings propagatorSettings)
        {
            this.propagatorSettings = propagatorSettings;
            this.nbPatterns = nbPatterns;
            this.weights = weights;
            this.propagator = propagator.standard;
            densePropagator = propagator.dense;
        }
        
        public class WFC_Result
        {
            public bool finished;
            public bool success;
            public int[,] output;
        }

        public abstract IEnumerator Run(uint seed, int limit, WFC_Result result);

        protected virtual void Observe(int node, ref Random random)
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
        
        public virtual void Ban(int node, int pattern)
        {
            wave[node][pattern] = false;

            int[] comp = compatible[node][pattern];
            for (int d = 0; d < 4; d++)
                comp[d] = 0;

            numPossiblePatterns[node] -= 1;
            sumsOfWeights[node] -= weights[pattern];
            sumsOfWeightLogWeights[node] -= weightLogWeights[pattern];

            double sum = sumsOfWeights[node];
            entropies[node] = Math.Log(sum) - sumsOfWeightLogWeights[node] / sum;
            isPossible = numPossiblePatterns[node] > 0;
        }

        protected virtual void Init()
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
        }

        protected virtual void Clear()
        {
            Parallel.For(0, wave.Length, node =>
            {
                for (int pattern = 0; pattern < nbPatterns; pattern++)
                {
                    wave[node][pattern] = true;
                    for (int direction = 0; direction < 4; direction++)
                        compatible[node][pattern][direction] =
                            propagator[pattern][Directions.GetOppositeDirection(direction)].Length;
                    //TODO: Check if OppositeDirection is the right one for the gpu solver
                }

                numPossiblePatterns[node] = weights.Length;
                sumsOfWeights[node] = totalSumOfWeights;
                sumsOfWeightLogWeights[node] = totalSumOfWeightLogWeights;
                entropies[node] = startingEntropy;
                observed[node] = -1;
            });
            
            isPossible = true;
        }
    }
}