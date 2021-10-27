using System;
using System.Collections;
using System.Threading.Tasks;

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

        protected Model(int width, int height, int patternSize, bool periodic, int nbPatterns, double[] weights,
            (bool[][][] dense, int[][][] standard) propagator, PropagatorSettings propagatorSettings)
        {
            this.width = width;
            this.height = height;
            stepInfo.width = width;
            stepInfo.height = height;
            this.patternSize = patternSize;
            this.periodic = periodic;
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

        public abstract void Ban(int node, int pattern);

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