using System;
using System.Collections;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using USCSL;
using USCSL.Utils;
using WFC;
using Random = Unity.Mathematics.Random;

namespace Models.CPU_Model
{
    [BurstCompile]
    public class CPU_Model_Parallel : CPU_Model, IDisposable
    {
        private NativeArray<bool> waveIn;
        private NativeArray<bool> waveOut;

        private struct JobInfo
        {
            [MarshalAs(UnmanagedType.I1)] public bool periodic;
            public int width, height; 
            public int patternSize;
            public int nbPatterns, nbNodes;
        }
        private JobInfo jobInfo;

        private struct Propagator
        {
            [MarshalAs(UnmanagedType.I1)] public bool down;
            [MarshalAs(UnmanagedType.I1)] public bool left;
            [MarshalAs(UnmanagedType.I1)] public bool right;
            [MarshalAs(UnmanagedType.I1)] public bool up;
        };
        private NativeArray<Propagator> propagator;

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
        private NativeArray<Memoisation> memoisation;

        private NativeQueue<int> openNodes;

        public CPU_Model_Parallel(int width, int height, int patternSize, bool periodic) : base(width, height, patternSize, periodic)
        {
            jobInfo.width = width;
            jobInfo.height = height;
            jobInfo.patternSize = patternSize;
            jobInfo.periodic = periodic;
        }

        public override void SetData(int nbPatterns, double[] weights, (bool[][][] dense, int[][][] standard) propagator,
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
            
            //Make hashset as large as wave so that no new entries have to be allocated.
            openNodes = new NativeQueue<int>(Allocator.Persistent);
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
                mem.numPossiblePatterns= nbPatterns;
                mem.sumsOfWeights = totalSumOfWeights;
                mem.sumsOfWeightsLogWeights = totalSumOfWeightLogWeights;
                mem.entropies = startingEntropy;
                memoisation[node] = mem;
            });

            waveOut.CopyFrom(waveIn);
            openNodes.Clear();
            
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

            if (!isPossible)
            {
                yield return DebugDrawCurrentState();
            }
        }

        private IEnumerator Run_Internal(WFC_Objects objects, WFC_Result result)
        {
            var nextNodeJob = new NextUnobservedNode_Job
            {
                jobInfo = this.jobInfo,
                wave = this.waveIn,
                memoisation = this.memoisation,
                random = objects.random,
            };
            
            nextNodeJob.Execute();
            //Copy back the random object, the state will be needed later.
            objects.random = nextNodeJob.random;
            //Copy back the resulting node
            int node = nextNodeJob.result;
            if (node >= 0)
            {
                var observeJob = new Observe_Job
                {
                    jobInfo = this.jobInfo,
                    node = node,
                    random = objects.random,
                    wave = this.waveIn,
                    weighting = this.weighting,
                    memoisation = this.memoisation,
                    isPossible = this.isPossible,
                    openNodes = this.openNodes.AsParallelWriter(),
                };
                observeJob.Execute();
                objects.random = observeJob.random;
                isPossible = observeJob.isPossible;
                
                waveOut.CopyFrom(waveIn);

                if (propagatorSettings.debug != PropagatorSettings.DebugMode.None)
                {
                    yield return DebugDrawCurrentState();
                }
                
                var propagation = Propagate();
                propagation.MoveNext();
                while (propagation.MoveNext())
                {
                    yield return propagation.Current;
                }

                if (!isPossible)
                {
                    result.output = null;
                    result.success = false;
                    result.finished = true;
                }
            }
            else
            {
                result.output = WaveToOutput();
                result.success = true;
                result.finished = true;
            }
        }
        
        [BurstCompile]
        private struct NextUnobservedNode_Job : IJob
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
        
        [BurstCompile]
        private struct Observe_Job : IJob
        {
            [ReadOnly] public JobInfo jobInfo;
            [ReadOnly] public int node;
            
            [ReadOnly] public NativeArray<bool> wave;
            [ReadOnly] public NativeArray<Weighting> weighting;
            [ReadOnly] public Random random;
            [WriteOnly] public bool isPossible;
            
            public NativeArray<Memoisation> memoisation;
            public NativeQueue<int>.ParallelWriter openNodes;

            private NativeArray<double> distribution;
            
            public void Execute()
            {
                // Choose an element according to the pattern distribution
                distribution = new NativeArray<double>(jobInfo.nbPatterns, Allocator.Temp);
            
                for (int pattern = 0; pattern < jobInfo.nbPatterns; pattern++)
                {
                    distribution[pattern] = wave[node * jobInfo.nbPatterns + pattern] ? weighting[pattern].weight : 0.0;
                }

                int r = RandomFromDistribution(random.NextDouble());
                for (int pattern = 0; pattern < jobInfo.nbPatterns; pattern++)
                {
                    if (wave[node * jobInfo.nbPatterns + pattern] != (pattern == r))
                    {
                        int2 nodeCoord = new int2(node % jobInfo.width, node / jobInfo.width);

                        Extensions.GetReadWriteRef(ref wave, out var waveRef);
                        Extensions.GetReadWriteRef(ref memoisation, out var memoisationRef);
                        Extensions.GetReadonlyRef(ref weighting, out var weightingRef);

                        Ban_Burst(node, ref nodeCoord, pattern,
                            ref isPossible,
                            in jobInfo,
                            ref waveRef,
                            ref memoisationRef,
                            ref weightingRef,
                            ref openNodes);
                    }
                }

                distribution.Dispose();
            }
            
            public int RandomFromDistribution(double threshold)
            {
                Extensions.GetReadWriteRef(ref distribution, out var distributionRef);
                double sum = Extensions.SumDoubles(ref distributionRef);
                for (int i = 0; i < distribution.Length; i++)
                {
                    distribution[i] /= sum;
                }
        
                double x = 0;
        
                for (int pattern = 0; pattern < distribution.Length; pattern++)
                {
                    x += distribution[pattern];
                    if (threshold <= x) return pattern;
                }
        
                return 0;
            }
        }

        private IEnumerator Propagate()
        {
            var timer = new CodeTimer_Average(true, true, true, UnityEngine.Debug.Log);
            var openWorkNodesArray = new NativeArray<int>(4, Allocator.TempJob);
            while (openNodes.Count > 0)
            {
                int changedNode = openNodes.Dequeue();
                openWorkNodesArray[0] = openNodes.Dequeue();
                openWorkNodesArray[1] = openNodes.Dequeue();
                openWorkNodesArray[2] = openNodes.Dequeue();
                openWorkNodesArray[3] = openNodes.Dequeue();
                
                var propagateJob = new Propagate_Job()
                {
                    changedNode = changedNode,
                    jobInfo = this.jobInfo,
                    isPossible = isPossible,
                    waveIn = this.waveIn,
                    waveOut = this.waveOut,
                    openWorkNodes = openWorkNodesArray,
                    propagator = this.propagator,
                    weighting = this.weighting,
                    memoisation = this.memoisation,
                    openNodes = openNodes.AsParallelWriter(),
                };
                propagateJob.Schedule(4, 1).Complete();
                isPossible = propagateJob.isPossible;
                waveIn.CopyFrom(waveOut);

                if (propagatorSettings.debug != PropagatorSettings.DebugMode.None)
                {
                    yield return DebugDrawCurrentState();
                }
            }

            openWorkNodesArray.Dispose();
            timer.Stop();
        }

        [BurstCompile]
        private struct Propagate_Job : IJobParallelFor
        {
            [ReadOnly] public JobInfo jobInfo;
            [WriteOnly] public bool isPossible;
            [ReadOnly] public int changedNode;
        
            /* Wave data, wave[node * nbPatterns + pattern] */
            [ReadOnly] public NativeArray<bool> waveIn;
            [WriteOnly] public NativeArray<bool> waveOut;
            
            /* Maps the index of execute to the actual node index of that thread. */
            [ReadOnly] public NativeArray<int> openWorkNodes;
            /*
             Which patterns can be placed in which direction of the current pattern
             propagator[pattern * 4 + direction] : int[] possibilities
             */
            [ReadOnly] public NativeArray<Propagator> propagator;
            [ReadOnly] public NativeArray<Weighting> weighting;

            [NativeDisableContainerSafetyRestriction]
            public NativeArray<Memoisation> memoisation;
            public NativeQueue<int>.ParallelWriter openNodes;

            public void Execute(int direction)
            {
                int node = openWorkNodes[direction];
                int2 nodeCoord = new int2(changedNode % jobInfo.width, changedNode / jobInfo.width);
                
                /* Generate own coordinate */
                nodeCoord.x += Directions.DirectionsX[direction];
                nodeCoord.y += Directions.DirectionsY[direction];

                if (jobInfo.periodic)
                {
                    nodeCoord.x = (nodeCoord.x + jobInfo.width) % jobInfo.width;
                    nodeCoord.y = (nodeCoord.y + jobInfo.height) % jobInfo.height;
                }
                else if (!jobInfo.periodic && (nodeCoord.x < 0 
                                               || nodeCoord.y < 0
                                               || nodeCoord.x >= jobInfo.width 
                                               || nodeCoord.y >= jobInfo.height))
                {
                    return;
                }

                /*
                 * Go over all still possible patterns in the current node and check if the are compatible
                 * with the still possible patterns of the other node.
                 */
                for (int changedNodePattern = 0; changedNodePattern < jobInfo.nbPatterns; changedNodePattern++)
                {
                    /* Go over each pattern of the active node and check if they are still valid. */
                    if (waveIn[node * jobInfo.nbPatterns + changedNodePattern] != true) continue;
                    /*
                     * Go over all possible patterns of the other cell and check if any of them are compatible
                     * with the possibleNodePattern
                     */
                    bool anyPossible = false;
                    for (int thisNodePattern = 0; thisNodePattern < jobInfo.nbPatterns; thisNodePattern++)
                    {
                        if (waveIn[changedNode * jobInfo.nbPatterns + thisNodePattern] == true)
                        {
                            if (GetPropagatorValue(
                                    propagator[
                                        GetPropagatorIndex(changedNodePattern, thisNodePattern,
                                            jobInfo.nbPatterns)], Directions.GetOppositeDirection(direction)) == true)
                            {
                                anyPossible = true;
                                break;
                            }
                        }
                    }

                    /* If there were no compatible patterns found Ban the pattern. */
                    if (!anyPossible)
                    {
                        Extensions.GetReadWriteRef(ref waveOut, out var waveRef);
                        Extensions.GetReadWriteRef(ref memoisation, out var memoisationRef);
                        Extensions.GetReadonlyRef(ref weighting, out var weightingRef);

                        Ban_Burst(node, ref nodeCoord, changedNodePattern,
                            ref isPossible,
                            in jobInfo,
                            ref waveRef,
                            ref memoisationRef,
                            ref weightingRef,
                            ref openNodes);
                    }
                }
            }
        }

        private IEnumerator DebugDrawCurrentState()
        {
            bool[][] waveManaged = new bool[nbNodes][];
            for (int node = 0; node < nbNodes; node++)
            {
                waveManaged[node] = new bool[nbPatterns];
                for (int pattern = 0; pattern < nbPatterns; pattern++)
                {
                    waveManaged[node][pattern] = waveIn[node * nbPatterns + pattern];
                }
            }

            NativeArray<int> nativeOpenNodesArray = openNodes.ToArray(Allocator.Temp);
            int[] openNodesArray = nativeOpenNodesArray.ToArray();
            nativeOpenNodesArray.Dispose();
            (int, int)[] propagatingCells = new (int, int)[openNodesArray.Length];
            for (int i = 0; i < openNodesArray.Length; i++)
            {
                propagatingCells[i].Item1 = openNodesArray[i];
            }
            stepInfo.propagatingCells = propagatingCells;
            stepInfo.numPropagatingCells = openNodesArray.Length;
            
            propagatorSettings.debugToOutput(stepInfo, waveManaged, propagatorSettings.orientedToTileId);
            yield return propagatorSettings.stepInterval == 0
                ? null
                : new WaitForSeconds(propagatorSettings.stepInterval);
        }
    
        public override void Ban(int node, int pattern)
        {
            int2 nodeCoord = new int2(node % jobInfo.width, node / jobInfo.width);
            
            Extensions.GetReadWriteRef(ref waveIn, out var waveRef);
            Extensions.GetReadWriteRef(ref memoisation, out var memoisationRef);
            Extensions.GetReadonlyRef(ref weighting, out var weightingRef);
            NativeQueue<int>.ParallelWriter openNodesPW = openNodes.AsParallelWriter();
                            
            Ban_Burst(node, ref nodeCoord, pattern,
                ref isPossible,
                in jobInfo,
                ref waveRef,
                ref memoisationRef,
                ref weightingRef,
                ref openNodesPW);
        }
        
        [BurstCompile]
        private static void Ban_Burst(int node, ref int2 nodeCoord, int pattern,
            ref bool isPossible,
            in JobInfo jobInfo,
            ref Extensions.NativeArrayRef waveRef, 
            ref Extensions.NativeArrayRef memoisationRef, 
            ref Extensions.NativeArrayRef weightingRef,
            ref NativeQueue<int>.ParallelWriter openNodes)
        {
            openNodes.Enqueue(node);
            
            Extensions.ToNativeArray(ref waveRef, out NativeArray<bool> wave);
            Extensions.ToNativeArray(ref memoisationRef, out NativeArray<Memoisation> memoisation);
            Extensions.ToNativeArray(ref weightingRef, out NativeArray<Weighting> weighting);
            wave[node * jobInfo.nbPatterns + pattern] = false;

            Memoisation mem = memoisation[node];
            mem.numPossiblePatterns -= 1;
            mem.sumsOfWeights -= weighting[pattern].weight;
            mem.sumsOfWeightsLogWeights -= weighting[pattern].logWeight;

            double sum = mem.sumsOfWeights;
            mem.entropies = math.log(sum) - mem.sumsOfWeightsLogWeights / sum;
            if (mem.numPossiblePatterns <= 0)
            {
                isPossible = false;
            }

            memoisation[node] = mem;

            /* Mark the neighbouring nodes for collapse and update info */
            for (int direction = 0; direction < 4; direction++)
            {
                /* Generate neighbour coordinate */
                int x2 = nodeCoord.x + Directions.DirectionsX[direction];
                int y2 = nodeCoord.y + Directions.DirectionsY[direction];

                if (jobInfo.periodic)
                {
                    x2 = (x2 + jobInfo.width) % jobInfo.width;
                    y2 = (y2 + jobInfo.height) % jobInfo.height;
                }
                else if (!jobInfo.periodic && (x2 < 0
                                               || y2 < 0
                                               || x2 + jobInfo.patternSize >= jobInfo.width
                                               || y2 + jobInfo.patternSize >= jobInfo.height))
                {
                    continue;
                }

                /* Add neighbour to hash set of pen neighbours. */
                int node2 = y2 * jobInfo.width + x2;
                openNodes.Enqueue(node2);
            }

            wave.Dispose();
            memoisation.Dispose();
            weighting.Dispose();
        }

        /*
         Transform the wave to a valid output (a 2d array of patterns that aren't in
         contradiction). This function should be used only when all cell of the wave
         are defined.
         */
        private int[,] WaveToOutput()
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
        
        public void Dispose()
        {
            waveIn.Dispose();
            waveOut.Dispose();
            weighting.Dispose();
            memoisation.Dispose();
            propagator.Dispose();
            openNodes.Dispose();
        }
    }
}
