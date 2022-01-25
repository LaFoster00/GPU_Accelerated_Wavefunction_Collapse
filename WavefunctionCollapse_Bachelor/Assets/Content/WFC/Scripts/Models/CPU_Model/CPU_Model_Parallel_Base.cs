using System;
using System.Collections;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using Random = Unity.Mathematics.Random;

namespace Models.CPU_Model
{
    public abstract class CPU_Model_Parallel_Base : CPU_Model, IDisposable
    {
        protected NativeArray<bool> waveIn;
        protected NativeArray<bool> waveOut;

        protected struct JobInfo
        {
            [MarshalAs(UnmanagedType.I1)] public bool periodic;
            public int width, height;
            public int patternSize;
            public int nbPatterns, nbNodes;
        }

        protected JobInfo jobInfo;

        protected struct Propagator
        {
            [MarshalAs(UnmanagedType.I1)] public bool down;
            [MarshalAs(UnmanagedType.I1)] public bool left;
            [MarshalAs(UnmanagedType.I1)] public bool right;
            [MarshalAs(UnmanagedType.I1)] public bool up;
        };

        protected NativeArray<Propagator> propagator;

        protected struct Weighting
        {
            public double weight;
            public double logWeight;
            public double distribution;
        };

        protected NativeArray<Weighting> weighting;

        protected struct Memoisation
        {
            public double sumsOfWeights;
            public double sumsOfWeightsLogWeights;
            public double entropies;
            public int numPossiblePatterns;
        };

        protected NativeArray<Memoisation> memoisation;

        public CPU_Model_Parallel_Base(int width, int height, int patternSize, bool periodic) : base(width, height,
            patternSize, periodic)
        {
            jobInfo.width = width;
            jobInfo.height = height;
            jobInfo.patternSize = patternSize;
            jobInfo.periodic = periodic;
        }

        public override void SetData(int nbPatterns, double[] weights,
            (bool[][][] dense, int[][][] standard) propagator,
            PropagatorSettings propagatorSettings)
        {
            base.SetData(nbPatterns, weights, propagator, propagatorSettings);
            jobInfo.nbPatterns = nbPatterns;
            jobInfo.nbNodes = nbNodes;

            this.propagator = new NativeArray<Propagator>(nbPatterns * nbPatterns, Allocator.Persistent);

            /* Convert dense propagator to native array flattened representation */
            for (int pattern = 0; pattern < nbPatterns; pattern++)
            {
                for (int otherPattern = 0; otherPattern < nbPatterns; otherPattern++)
                {
                    Propagator prop = this.propagator[pattern * nbPatterns + otherPattern];
                    prop.down = propagator.dense[pattern][0][otherPattern];
                    prop.left = propagator.dense[pattern][1][otherPattern];
                    prop.right = propagator.dense[pattern][2][otherPattern];
                    prop.up = propagator.dense[pattern][3][otherPattern];
                    this.propagator[pattern * nbPatterns + otherPattern] = prop;
                }
            }
        }

        [BurstCompile]
        protected static int GetPropagatorIndex(int pattern, int otherPattern, int nbPatterns)
        {
            return pattern * nbPatterns + otherPattern;
        }

        [BurstCompile]
        protected static bool GetPropagatorValue(in Propagator propagator, int direction)
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

        protected override void Init()
        {
            base.Init();

            waveIn = new NativeArray<bool>(width * height * nbPatterns, Allocator.Persistent);
            waveOut = new NativeArray<bool>(width * height * nbPatterns, Allocator.Persistent);
            weighting = new NativeArray<Weighting>(nbPatterns, Allocator.Persistent);

            for (int pattern = 0; pattern < nbPatterns; pattern++)
            {
                Weighting weight = weighting[pattern];
                weight.weight = weights[pattern];
                weight.logWeight = weights[pattern] * math.log(weights[pattern]);
                totalSumOfWeights += weights[pattern];
                totalSumOfWeightLogWeights += weight.logWeight;
                weighting[pattern] = weight;
            }

            startingEntropy = Math.Log(totalSumOfWeights) - totalSumOfWeightLogWeights / totalSumOfWeights;

            memoisation = new NativeArray<Memoisation>(nbNodes, Allocator.Persistent);
        }

        protected override void Clear()
        {
            Parallel.For(0, nbNodes, node =>
                /* for (int node = 0; node < wave.Length; node++) */
            {
                for (int pattern = 0; pattern < nbPatterns; pattern++)
                {
                    waveIn[node * nbPatterns + pattern] = true;
                }

                Memoisation mem = memoisation[node];
                mem.numPossiblePatterns = nbPatterns;
                mem.sumsOfWeights = totalSumOfWeights;
                mem.sumsOfWeightsLogWeights = totalSumOfWeightLogWeights;
                mem.entropies = startingEntropy;
                memoisation[node] = mem;
            });

            waveOut.CopyFrom(waveIn);

            base.Clear();
        }

        public override IEnumerator Run(uint seed, int limit, WFC_Result result)
        {
            if (!waveIn.IsCreated) Init();

            Clear();
            WFC_Objects objects = new WFC_Objects
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
        }

        protected abstract IEnumerator Run_Internal(WFC_Objects objects, WFC_Result result);
        
        protected int NextUnobservedNode(ref Random random)
        {
            double min = Double.MaxValue;
            int argmin = -1;
            for (int node = 0; node < nbNodes; node++)
            {
                if (!periodic && (node % width + patternSize > width || node / width + patternSize > height)) continue;
                int remainingValues = memoisation[node].numPossiblePatterns;
                double entropy = memoisation[node].entropies;
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

        [BurstCompile]
        protected struct NextUnobservedNode_Job : IJob
        {
            [ReadOnly] public JobInfo jobInfo;

            [ReadOnly] public NativeArray<bool> wave;
            [ReadOnly] public NativeArray<Memoisation> memoisation;
            [ReadOnly] public Random random;

            [WriteOnly] public int result;

            public void Execute()
            {
                double min = Double.MaxValue;
                int argmin = -1;
                for (int node = 0; node < jobInfo.nbNodes; node++)
                {
                    if (!jobInfo.periodic &&
                        (node % jobInfo.width + jobInfo.patternSize > jobInfo.width ||
                         node / jobInfo.width + jobInfo.patternSize > jobInfo.height))
                    {
                        continue;
                    }

                    int remainingValues = memoisation[node].numPossiblePatterns;
                    double entropy = memoisation[node].entropies;
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

                result = argmin;
            }
        }

        protected abstract IEnumerator DebugDrawCurrentState();

        /*
         Transform the wave to a valid output (a 2d array of patterns that aren't in
         contradiction). This function should be used only when all cell of the wave
         are defined.
         */
        protected int[,] WaveToOutput()
        {
            int[] observed = new int[nbNodes];
            int[,] outputPatterns = new int[height, width];
            for (int node = 0; node < nbNodes; node++)
            for (int pattern = 0; pattern < nbPatterns; pattern++)
            {
                if (waveIn[node * nbPatterns + pattern] == true)
                {
                    observed[node] = pattern;
                    int x = node % width;
                    int y = node / width;
                    outputPatterns[y, x] = observed[node];
                    break;
                }
            }

            return outputPatterns;
        }

        public virtual void Dispose()
        {
            waveIn.Dispose();
            waveOut.Dispose();
            weighting.Dispose();
            memoisation.Dispose();
            propagator.Dispose();
        }
    }
}