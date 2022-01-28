using System;
using System.Collections;
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
    public class CPU_Model_Parallel_Queue : CPU_Model_Parallel_Base
    {
        private NativeQueue<int> openNodes;
        
        public CPU_Model_Parallel_Queue(int width, int height, int patternSize, bool periodic) : base(width, height, patternSize, periodic)
        {
        }

        protected override void Init()
        {
            base.Init();
            //Make hashset as large as wave so that no new entries have to be allocated.
            openNodes = new NativeQueue<int>(Allocator.Persistent);
        }

        protected override void Clear()
        {
            openNodes.Clear();
            base.Clear();
        }

        protected override IEnumerator Run_Internal(WFC_Objects objects, WFC_Result result)
        {
            var timer = new CodeTimer_Average(true, true, true, "Observe_Parallel", Debug.Log);
            var nextNodeJob = new NextUnobservedNode_Job
            {
                jobInfo = this.jobInfo,
                wave = this.waveIn,
                memoisation = this.memoisation,
                random = objects.random,
            };
            nextNodeJob.Execute();
            
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
                
                timer.Stop(false);

                if (propagatorSettings.debug != PropagatorSettings.DebugMode.None)
                {
                    yield return DebugDrawCurrentState();
                }

                var propagateTimer = new CodeTimer_Average(true, true, true, "Propagate_Parallel", Debug.Log);
                var propagation = Propagate();
                propagation.MoveNext();
                while (propagation.MoveNext())
                {
                    yield return propagation.Current;
                }
                
                propagateTimer.Stop(false);

                if (!isPossible)
                {
                    result.output = null;
                    result.success = false;
                    result.finished = true;
                }
            }
            else
            {
                timer.Stop(false);
                result.output = WaveToOutput();
                result.success = true;
                result.finished = true;
            }
        }

        private IEnumerator Propagate()
        {
            var openWorkNodesArray = new NativeArray<int>(4, Allocator.Persistent);
            while (openNodes.Count > 0)
            {
                int changedNode = openNodes.Dequeue();
                
                /* Calculate neighboring nodes here instead of inside ban to avoid potential race conditions. */
                int2 nodeCoord = new int2(changedNode % width, changedNode / width);
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
                    openWorkNodesArray[direction] = node2;
                }

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
        }
        
        [BurstCompile]
        private struct Observe_Job : IJob
        {
            [ReadOnly] public JobInfo jobInfo;
            [ReadOnly] public int node;

            public NativeArray<bool> wave;
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
                        Ban_Job ban = new Ban_Job()
                        {
                            node = node,
                            pattern = pattern,
                            isPossible = isPossible,
                            jobInfo = jobInfo,
                            wave = wave,
                            memoisation = memoisation,
                            weighting = weighting,
                            openNodes = openNodes,
                        };
                        
                        ban.Execute();
                        
                        isPossible = ban.isPossible;

                    }
                }

                distribution.Dispose();
            }

            public int RandomFromDistribution(double threshold)
            {
                NativeSlice<double> distributionSlice = distribution;
                double sum = Extensions.SumDoubles(ref distributionSlice);
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

        [BurstCompile]
        private struct Propagate_Job : IJobParallelFor
        {
            [ReadOnly] public JobInfo jobInfo;
            [WriteOnly] public bool isPossible;
            [ReadOnly] public int changedNode;
        
            /* Wave data, wave[node * nbPatterns + pattern] */
            [ReadOnly] public NativeArray<bool> waveIn;
            [NativeDisableContainerSafetyRestriction]
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
                //var timer = new CodeTimer_Average(true, true, true, "Propagate_Internal_Parallel", Debug.Log);
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
                        Ban_Job ban = new Ban_Job()
                        {
                            node = node,
                            pattern = changedNodePattern,
                            isPossible = isPossible,
                            jobInfo = jobInfo,
                            wave = waveOut,
                            memoisation = memoisation,
                            weighting = weighting,
                            openNodes = openNodes
                        };
                        
                        ban.Execute();

                        isPossible = ban.isPossible;
                    }
                }
                //timer.Stop(false);
            }
        }

        protected override IEnumerator DebugDrawCurrentState()
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
            Ban_Job ban = new Ban_Job()
            {
                node = node,
                pattern = pattern,
                isPossible = isPossible,
                jobInfo = jobInfo,
                wave = waveOut,
                memoisation = memoisation,
                weighting = weighting,
                openNodes = openNodes.AsParallelWriter()
            };
            ban.Execute();
            isPossible = ban.isPossible;
        }

        [BurstCompile]
        private struct Ban_Job : IJob
        {
            [ReadOnly] public int node;
            [ReadOnly] public int pattern;
            [ReadOnly] public bool isPossible;
            [ReadOnly] public JobInfo jobInfo;
            public NativeSlice<bool> wave;
            public NativeSlice<Memoisation> memoisation;
            [ReadOnly] public NativeSlice<Weighting> weighting;
            public NativeQueue<int>.ParallelWriter openNodes;


            public void Execute()
            {
                openNodes.Enqueue(node);
            
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
            }
        }

        public override void Dispose()
        {
            base.Dispose();
            openNodes.Dispose();
        }
    }
}
