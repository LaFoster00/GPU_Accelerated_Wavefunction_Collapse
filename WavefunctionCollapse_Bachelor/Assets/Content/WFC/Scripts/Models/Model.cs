using System;
using System.Collections;

namespace WFC
{
    public abstract class Model
    {
        public static int PropagationThreadGroupSizeX = 4, PropagationThreadGroupSizeY = 4;
        
        public readonly int width, height, patternSize;
        protected int nbPatterns, nbNodes;
        protected readonly bool periodic;
        
        protected bool isPossible;
        
        protected double[] weights;
        protected double totalSumOfWeights, totalSumOfWeightLogWeights, startingEntropy;
        
        /*
         Which patterns can be placed in which direction of the current node
         propagator[pattern][direction] : bool[] compatibles
         */
        protected bool[][][] densePropagator;

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
            nbNodes = width * height;
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
            totalSumOfWeights = 0;
            totalSumOfWeightLogWeights = 0;
        }

        protected virtual void Clear()
        {
            isPossible = true;
        }
    }
}