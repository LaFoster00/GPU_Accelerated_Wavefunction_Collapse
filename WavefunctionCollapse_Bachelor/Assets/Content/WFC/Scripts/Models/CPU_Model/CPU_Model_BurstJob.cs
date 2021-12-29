using System;
using System.Collections;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using WFC;

[BurstCompile]
public class CPU_Model_BurstJob : CPU_Model
{
    private NativeArray<bool> wave;

    private struct Propagator
    {
        [MarshalAs(UnmanagedType.I1)] public bool down;
        [MarshalAs(UnmanagedType.I1)] public bool left;
        [MarshalAs(UnmanagedType.I1)] public bool right;
        [MarshalAs(UnmanagedType.I1)] public bool up;
    };

    [BurstCompile]
    private static int GetPropagatorIndex(int pattern, int otherPattern, int nbPatterns)
    {
        return pattern * nbPatterns + otherPattern;
    }

    [BurstCompile]
    private static bool GetPropagatorValue(in Propagator propagator, int direction)
    {
        switch (direction)
        {
            case 0:
                return propagator.down;
            case 1:
                return propagator.left;
            case 2:
                return propagator.right;
            case 3:
                return propagator.up;
            default:
                throw new Exception("Direction not valid");
        }
    }

    private struct Weighting
    {
        public double weight;
        public double logWeight;
        public double distribution;
    };
    NativeArray<Weighting> weighting;

    struct Memoisation
    {
        public double sumsOfWeights;
        public double sumsOfWeightsLogWeights;
        public double entropies;
        public int numPossiblePatterns;
    };
    NativeArray<Memoisation> memoisation;
    
    public CPU_Model_BurstJob(int width, int height, int patternSize, bool periodic) : base(width, height, patternSize, periodic)
    {
    }

    public override void SetData(int nbPatterns, double[] weights, (bool[][][] dense, int[][][] standard) propagator,
        PropagatorSettings propagatorSettings)
    {
        base.SetData(nbPatterns, weights, propagator, propagatorSettings);
    }

    protected override void Clear()
    {
        Parallel.For(0, wave.Length, node =>
            /* for (int node = 0; node < wave.Length; node++) */
        {
            for (int pattern = 0; pattern < nbPatterns; pattern++)
            {
                wave[node * nbPatterns + pattern] = true;
            }

            Memoisation mem = memoisation[node];
            mem.numPossiblePatterns= nbPatterns;
            mem.sumsOfWeights = totalSumOfWeights;
            mem.sumsOfWeightsLogWeights = totalSumOfWeightLogWeights;
            mem.entropies = startingEntropy;
            memoisation[node] = mem;
        });
        
        base.Clear();
    }

    protected override void Init()
    {
        base.Init();
        
        wave = new NativeArray<bool>(width * height * nbPatterns, Allocator.Persistent);
        weighting = new NativeArray<Weighting>(nbPatterns, Allocator.Persistent);

        for (int t = 0; t < nbPatterns; t++)
        {
            Weighting weight = weighting[t];
            weight.logWeight = weights[t] * math.log(weights[t]);
            totalSumOfWeights += weights[t];
            totalSumOfWeightLogWeights += weight.logWeight;
            weighting[t] = weight;
        }

        startingEntropy = Math.Log(totalSumOfWeights) - totalSumOfWeightLogWeights / totalSumOfWeights;
        
        memoisation = new NativeArray<Memoisation>(nbPatterns, Allocator.Persistent);
    }

    public override IEnumerator Run(uint seed, int limit, WFC_Result result)
    {
        throw new System.NotImplementedException();
    }

    [BurstCompile]
    private struct Propagate_Job : IJobParallelFor
    {
        [ReadOnly] public int nbPatterns;
        [ReadOnly] public int width, height;
        [ReadOnly] public bool isPeriodic;
        [ReadOnly] public int patternSize;

        public bool isPossible;
        
        /* Maps the index of execute to the actual node index of that thread. */
        [ReadOnly] public NativeArray<int> openWorkNodes;

        /* Wave data, wave[node * nbPatterns + pattern] */
        public NativeArray<bool> wave;

        /*
         Which patterns can be placed in which direction of the current pattern
         propagator[pattern * 4 + direction] : int[] possibilities
         */
        [ReadOnly] public NativeArray<Propagator> propagator;

        public NativeArray<double> weights;
        
        public NativeArray<int> numPossiblePatterns;
        public NativeArray<double> distribution, weightLogWeights, sumsOfWeights, sumsOfWeightLogWeights, entropies;

        public NativeHashSet<int> openNodes;

        public void Execute(int index)
        {
            int node = openWorkNodes[index];
            int2 nodeCoord = new int2(node / width, node % width);

            for (int direction = 0; direction < 4; direction++)
            {
                /* Generate neighbour coordinate */ 
                int x2 = nodeCoord.x + Directions.DirectionsX[direction];
                int y2 = nodeCoord.y + Directions.DirectionsX[direction];

                if (isPeriodic)
                {
                    x2 = (x2 + width) % width;
                    y2 = (y2 + height) % height;
                }
                else if (!isPeriodic && (x2 < 0
                                          || y2 < 0
                                          || x2 >= width
                                          || y2 >= height))
                {
                    continue;
                }

                int node2 = y2 * width + x2;

                /*
                 * Go over all still possible patterns in the current node and check if the are compatible
                 * with the still possible patterns of the other node.
                 */
                for (int possibleNodePattern = 0; possibleNodePattern < nbPatterns; possibleNodePattern++)
                {
                    /* Go over each pattern of the active node and check if they are still active. */
                    if (!wave[node * nbPatterns + possibleNodePattern] == true) continue;
                    /*
                     * Go over all possible patterns of the other cell and check if any of them are compatible
                     * with the possibleNodePattern
                     */
                    bool anyPossible = false;
                    for (int compatible_pattern = 0; compatible_pattern < nbPatterns; compatible_pattern++)
                    {
                        if (wave[node2 * nbPatterns + compatible_pattern] == true)
                        {
                            if (GetPropagatorValue(propagator[GetPropagatorIndex(possibleNodePattern, compatible_pattern, nbPatterns)], direction) == true)
                            {
                                anyPossible = true;
                                break;
                            }
                        }
                    }

                    /* If there were no compatible patterns found Ban the pattern. */
                    if (!anyPossible)
                    {
                        Ban(node, nodeCoord, possibleNodePattern);
                    }
                }
            }
        }

        private void Ban(int node, int2 nodeCoord, int pattern)
        {
            wave[node * nbPatterns + pattern] = false;

            numPossiblePatterns[node] -= 1;
            sumsOfWeights[node] -= weights[pattern];
            sumsOfWeightLogWeights[node] -= weightLogWeights[pattern];

            double sum = sumsOfWeights[node];
            entropies[node] = math.log(sum) - sumsOfWeightLogWeights[node] / sum;
            isPossible = numPossiblePatterns[node] > 0;

            if (numPossiblePatterns[node] <= 0)
            {
                isPossible = false;
            }

            /* Mark the neighbouring nodes for collapse and update info */
            for (int direction = 0; direction < 4; direction++)
            {
                /* Generate neighbour coordinate */
                int x2 = nodeCoord.x + Directions.DirectionsX[direction];
                int y2 = nodeCoord.y + Directions.DirectionsY[direction];

                if (isPeriodic)
                {
                    x2 = (x2 + width) % width;
                    y2 = (y2 + height) % height;
                }
                else if (!isPeriodic && (x2 < 0
                                         || y2 < 0
                                         || x2 + patternSize >= width
                                         || y2 + patternSize >= height))
                {
                    continue;
                }

                /* Add neighbour to hash set of pen neighbours. */
                int node2 = y2 * width + x2;
                openNodes.Add(node2);
            }
        }
    }

    public override void Ban(int node, int pattern)
    {
        throw new System.NotImplementedException();
    }
}
