using System;
using System.Collections;

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

        /* Contains node and the deleted pattern */
        protected (int, int)[] stack;
        protected int stackSize, observedSoFar;

        
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
        
        protected Model(int width, int height, int patternSize, bool periodic, int nbPatterns, double[] weights, int[][][] propagator, PropagatorSettings propagatorSettings)
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
            this.propagator = propagator;
        }

        public class WFC_Result
        {
            public bool finished;
            public bool success;
            public int[,] output;
        }

        public abstract IEnumerator Run(uint seed, int limit, WFC_Result result);

        public void Ban(int node, int pattern)
        {
            wave[node][pattern] = false;

            int[] comp = compatible[node][pattern];
            for (int d = 0; d < 4; d++)
                comp[d] = 0;
            stack[stackSize] = (node, pattern);
            stackSize++;

            numPossiblePatterns[node] -= 1;
            sumsOfWeights[node] -= weights[pattern];
            sumsOfWeightLogWeights[node] -= weightLogWeights[pattern];

            double sum = sumsOfWeights[node];
            entropies[node] = Math.Log(sum) - sumsOfWeightLogWeights[node] / sum;
            isPossible = numPossiblePatterns[node] > 0;
        }
    }
}