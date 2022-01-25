using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Models.CPU_Model;
using Models.GPU_Model;
using Unity.Mathematics;
using UnityEngine;
using USCSL.Utils;
using Debug = UnityEngine.Debug;
using Random = UnityEngine.Random;

namespace WFC.Tiling
{
    public enum Solver
    {
        CPU_Sequential = 0,
        CPU_Parallel_Queue = 1,
        CPU_Parallel_Batched = 2,
        GPU_Naive = 3,
        GPU_Granular = 4,
        GPU_ComputeBuffer = 5
    }

    //[ExecuteInEditMode]
    public class WFC_Manager_TextureTile : MonoBehaviour
    {
        public WFC_Texture2DTile[] tiles;

        public (bool Success, Texture2D[,] Result) result;
        public (Model.StepInfo stepInfo, List<Texture2D>[,] debug) debugOutput;

        [Header("Benchmark")] 
        [SerializeField] private bool runBenchmark = true;
        [SerializeField] private int runsPerSolver = 10;

        [Header("Solver")]
        [SerializeField] private Solver solver;
        [SerializeField] private int maxNumIterations = -1;
        
        [Header("Output")]
        [SerializeField] private int width;
        [SerializeField] private int height;
        [SerializeField] private bool periodic = true;
        [SerializeField] private bool saveOutput = false;
        [SerializeField] private bool useApplicationPath = true;
        [SerializeField] private string filePath = "/Content/WFC/Outputs/";
        [SerializeField] private string fileName = "Output";
        
        [Header("Display")]
        [SerializeField] private Vector2Int displayOffset;
        [SerializeField] private int displayHeight = 16;
        [SerializeField] private int displayWidth = 16;
        [SerializeField] private bool drawFrame = true;
        [SerializeField] private Texture2D frameTexture;
        [SerializeField] private Texture2D highlightTexture;
        [SerializeField] private Texture2D greenHighlightTexture;
        [SerializeField] private Texture2D yellowFrameTexture;
        
        [Header("GPU Compute Shader")]
        [SerializeField] private ComputeShader observerShader;
        [SerializeField] private ComputeShader propagatorShader;
        [SerializeField] private ComputeShader banShader;
        [SerializeField] private ComputeShader clearOutBuffersShader;
        [SerializeField] private ComputeShader resetOpenNodesShader;

        [Header("ComputeBuffer Settings")]
        [SerializeField] private int totalObservePropagateIterations = 10;
        [SerializeField] private int propagationIterations = 4;

        [Header("Debug Mode")] [SerializeField]
        private bool timeFunctionCalls = false;
        [SerializeField] private Model.PropagatorSettings.DebugMode debugMode;
        [SerializeField] private float stepInterval;

        private Model.PropagatorSettings _settings;

        private void Start()
        {
            CodeTimerOptions.active = timeFunctionCalls;
            
            List<TileNeighbour<Texture2D>> neighbours = new List<TileNeighbour<Texture2D>>();
            foreach (var tile in tiles)
            {
                tile.GenerateOrientations();
                foreach (var neighbour in tile.neighbours)
                {
                    if (neighbour.active && tiles.Contains(neighbour.leftTile) && tiles.Contains(neighbour.rightTile))
                        neighbours.Add(neighbour);
                }
            }

            _settings = new Model.PropagatorSettings(debugMode, stepInterval, DisplayWaveDebug);

            if (runBenchmark)
                StartCoroutine(RunBenchmark(neighbours));
            else
                StartCoroutine(ExecuteWfc(neighbours));
        }

        private void Update()
        {
            _settings.stepInterval = stepInterval;
            _settings.debug = debugMode;
        }

        private IEnumerator ExecuteWfc(List<TileNeighbour<Texture2D>> neighbours)
        {
            yield return new WaitForSeconds(2);
            
            double startTime = Time.realtimeSinceStartup;
            int iteration = 0;
            
            Model model;
            switch (solver)
            {
                case Solver.CPU_Sequential:
                    model = new CPU_Model_Sequential(width, height, 1, periodic);
                    break;
                case Solver.CPU_Parallel_Queue:
                    model = new CPU_Model_Parallel_Queue(width, height, 1, periodic);
                    break;
                case Solver.CPU_Parallel_Batched:
                    model = new CPU_Model_Parallel_Batched(width, height, 1, periodic);
                    break;
                case Solver.GPU_Naive:
                    model = new GPU_Model_Naive(propagatorShader, width, height, 1, periodic);
                    break;
                case Solver.GPU_Granular:
                    model = new GPU_Model_Granular(observerShader, propagatorShader, banShader, width, height, 1, periodic);
                    break;
                case Solver.GPU_ComputeBuffer:
                default:
                    model = new GPU_Model_ComputeBuffer(observerShader, propagatorShader, banShader, clearOutBuffersShader, resetOpenNodesShader, propagationIterations, totalObservePropagateIterations,width, height, 1, periodic);
                    break;
            }

            TilingWFC<Texture2D> tilingWfc = new TilingWFC<Texture2D>(model,
                tiles.Cast<WFC_2DTile<Texture2D>>().ToArray(),
                neighbours.Cast<Neighbour<Texture2D>>().ToArray(), _settings);

            while (!result.Success)
            {
                Debug.Log("New Run");
                TilingWFC<Texture2D>.WFC_TypedResult wfcResult = new TilingWFC<Texture2D>.WFC_TypedResult();

                Unity.Mathematics.Random random =
                    Unity.Mathematics.Random.CreateFromIndex((uint) Random.Range(Int32.MinValue, Int32.MaxValue));
                yield return tilingWfc.Run(random.NextUInt(), maxNumIterations, wfcResult);

                result.Result = wfcResult.result;
                result.Success = wfcResult.success;
                iteration++;
                if (!result.Success) print("Generation failed. Retrying");
            }
            
            switch (solver)
            {
                case Solver.CPU_Parallel_Queue:
                case Solver.CPU_Parallel_Batched:
                    if (model is CPU_Model_Parallel_Base parallelModel)
                        parallelModel.Dispose();
                    break;
                case Solver.GPU_Naive:
                    if (model is GPU_Model_Naive naiveModel)
                    {
                        naiveModel.Dispose();
                    }
                    break;
                case Solver.GPU_Granular:
                case Solver.GPU_ComputeBuffer:
                    if (model is GPU_Model gpuModel)
                    {
                        //Free native resources
                        gpuModel.Dispose();
                    }
                    break;
            }

            print($"It took {Time.realtimeSinceStartup - startTime} seconds and {iteration} tries to complete this task!");
            PrintTimingData();

            if (!saveOutput) yield break;
            
            var outputTexture = OutputToTexture(result.Result);
            var outputImage = outputTexture.EncodeToPNG();
            Destroy(outputTexture);
            fileName = fileName.Replace(".png", "");
            if (filePath[filePath.Length - 1] != '/')
            {
                filePath += "/";
            }

            string fullPath;
            if (!useApplicationPath)
            {
                fullPath = filePath + fileName + $"_{width}_{height}" + ".png";
                File.WriteAllBytes(fullPath, outputImage);
            }
            else
            {
                if (filePath[0] != '/')
                {
                    filePath = "/" + filePath;
                }
                fullPath = Application.dataPath + filePath + fileName + $"_{width}_{height}" + ".png";
                File.WriteAllBytes(fullPath, outputImage);
            }
            print($"Image saved at {fullPath}.");
        }
        
        public Texture2D OutputToTexture(Texture2D[,] output)
        {
            int tilesize = output[0, 0].width;
            int MX = output.GetLength(0), MY = output.GetLength(1);
            Texture2D result = new Texture2D(MX * tilesize, MY * tilesize, output[0,0].format, false);

            for (int tileX = 0; tileX < MX; tileX++)
            {
                for (int tileY = 0; tileY < MY; tileY++)
                {
                    result.SetPixels(tileX * tilesize, tileY * tilesize, tilesize, tilesize, output[height - tileY - 1, tileX].GetPixels());
                }
            }

            result.Apply();

            return result;
        }

        private IEnumerator RunBenchmark(List<TileNeighbour<Texture2D>> neighbours)
        {
            yield return new WaitForSeconds(2);
            
            int iteration = 0;

            double[] executionTimes = new double[(int)Solver.GPU_ComputeBuffer + 1];

            int run = 0;
            while (run <= (int)Solver.GPU_ComputeBuffer)
            {
                run++;
                solver = (Solver)run - 1;
                
                Model model;
                switch (solver)
                {
                    case Solver.CPU_Sequential:
                        model = new CPU_Model_Sequential(width, height, 1, periodic);
                        break;
                    case Solver.CPU_Parallel_Queue:
                        model = new CPU_Model_Parallel_Queue(width, height, 1, periodic);
                        break;
                    case Solver.CPU_Parallel_Batched:
                        model = new CPU_Model_Parallel_Batched(width, height, 1, periodic);
                        break;
                    case Solver.GPU_Naive:
                        model = new GPU_Model_Naive(propagatorShader, width, height, 1, periodic);
                        break;
                    case Solver.GPU_Granular:
                        model = new GPU_Model_Granular(observerShader, propagatorShader, banShader, width, height, 1,
                            periodic);
                        break;
                    case Solver.GPU_ComputeBuffer:
                    default:
                        model = new GPU_Model_ComputeBuffer(observerShader, propagatorShader, banShader,
                            clearOutBuffersShader, resetOpenNodesShader, propagationIterations,
                            totalObservePropagateIterations, width, height, 1, periodic);
                        break;
                }
                
                print($"Running Benchmark for {solver.ToString()}.");

                TilingWFC<Texture2D> tilingWfc = new TilingWFC<Texture2D>(model,
                    tiles.Cast<WFC_2DTile<Texture2D>>().ToArray(),
                    neighbours.Cast<Neighbour<Texture2D>>().ToArray(), _settings);

                Stopwatch stopwatch = new Stopwatch();
                for (int i = 0; i < runsPerSolver; i++)
                {
                    stopwatch.Start();
                    
                    result = (false, null);
                    while (!result.Success)
                    {
                        Debug.Log("New Run");
                        TilingWFC<Texture2D>.WFC_TypedResult wfcResult = new TilingWFC<Texture2D>.WFC_TypedResult();

                        Unity.Mathematics.Random random =
                            Unity.Mathematics.Random.CreateFromIndex(
                                (uint) Random.Range(Int32.MinValue, Int32.MaxValue));
                        yield return tilingWfc.Run(random.NextUInt(), maxNumIterations, wfcResult);

                        result.Result = wfcResult.result;
                        result.Success = wfcResult.success;
                        iteration++;
                        if (!result.Success) print("Generation failed. Retrying");
                    }
                    stopwatch.Stop();
                    yield return new WaitForSeconds(0.01f);
                }
                executionTimes[(int)solver] += stopwatch.Elapsed.TotalSeconds;

                switch (solver)
                {
                    case Solver.CPU_Parallel_Queue:
                    case Solver.CPU_Parallel_Batched:
                        if (model is CPU_Model_Parallel_Base parallelModel)
                            parallelModel.Dispose();
                        break;
                    case Solver.GPU_Naive:
                        if (model is GPU_Model_Naive naiveModel)
                        {
                            naiveModel.Dispose();
                        }

                        break;
                    case Solver.GPU_Granular:
                    case Solver.GPU_ComputeBuffer:
                        if (model is GPU_Model gpuModel)
                        {
                            //Free native resources
                            gpuModel.Dispose();
                        }

                        break;
                }
            }
            PrintTimingData();

            for (int i = 0; i <= (int)Solver.GPU_ComputeBuffer; i++)
            {
                print($"Solver '{((Solver)i).ToString()} took {executionTimes[i] / runsPerSolver} seconds on average for a run.");
            }
        }

        private void PrintTimingData()
        {
            if (timeFunctionCalls)
            {
                var timings = CodeTimer_Average.FunctionCallTimings;
                foreach (var timing in timings)
                {
                    Debug.Log(CodeTimer_Average.GetMessage(timing.Value));
                }
            }
        }

        private void OnGUI()
        {
            if (result.Success)
            {
                for (int y = 0; y < result.Result.GetLength(0); y++)
                {
                    for (int x = 0; x < result.Result.GetLength(1); x++)
                    {
                        GUI.DrawTexture(
                            Rect.MinMaxRect(
                                displayWidth * x + displayOffset.x,
                                displayHeight * y + displayOffset.y,
                                displayWidth + displayWidth * x + displayOffset.x,
                                displayHeight + displayHeight * y + displayOffset.y),
                            result.Result[y, x]);
                        if (drawFrame)
                        {
                            GUI.DrawTexture(
                                Rect.MinMaxRect(
                                    displayWidth * x + displayOffset.x,
                                    displayHeight * y + displayOffset.y,
                                    displayWidth + displayWidth * x + displayOffset.x,
                                    displayHeight + displayHeight * y + displayOffset.y),
                                frameTexture);
                        }
                    }
                }
            }
            else if (debugOutput.debug != null)
            {
                for (int y = 0; y < debugOutput.debug.GetLength(0); y++)
                {
                    for (int x = 0; x < debugOutput.debug.GetLength(1); x++)
                    {
                        List<Texture2D> currentCellTextures = debugOutput.debug[y, x];
                        if (currentCellTextures.Count == 0) continue;
                        int texNumSide = (int) math.ceil(math.sqrt(currentCellTextures.Count));
                        int texDisplayWidth = displayWidth / texNumSide;
                        int texDisplayHeight = displayHeight / texNumSide;

                        for (int texY = 0; texY < texNumSide; texY++)
                        {
                            for (int texX = 0; texX < texNumSide; texX++)
                            {
                                int texIndex = texX + texY * texNumSide;
                                if (texIndex >= currentCellTextures.Count) continue;
                                int xmin = displayWidth * x + texX * texDisplayWidth + displayOffset.x;
                                int ymin = displayHeight * y + texY * texDisplayHeight + displayOffset.y;
                                int xmax = xmin + texDisplayHeight;
                                int ymax = ymin + texDisplayHeight;

                                GUI.DrawTexture(
                                    Rect.MinMaxRect(
                                        xmin,
                                        ymin,
                                        xmax,
                                        ymax),
                                    currentCellTextures[texIndex]);
                            }
                        }

                        GUI.DrawTexture(
                            Rect.MinMaxRect(
                                displayWidth * x + displayOffset.x,
                                displayHeight * y + displayOffset.y,
                                displayWidth + displayWidth * x + displayOffset.x,
                                displayHeight + displayHeight * y + displayOffset.y), 
                            frameTexture);
                    }
                }

                for (int node = 0; node < debugOutput.stepInfo.numPropagatingCells; node++)
                {
                    var (x, y) = debugOutput.stepInfo.propagatingCells[node].Item1
                        .IdToXY(debugOutput.stepInfo.width);
                    GUI.DrawTexture(
                        Rect.MinMaxRect(
                            displayWidth * x + displayOffset.x,
                            displayHeight * y + displayOffset.y,
                            displayWidth + displayWidth * x + displayOffset.x,
                            displayHeight + displayHeight * y + displayOffset.y),
                        yellowFrameTexture);
                }

                if (solver == Solver.CPU_Sequential)
                {
                    GUI.DrawTexture(
                        Rect.MinMaxRect(
                            displayWidth * debugOutput.stepInfo.currentTile.x + displayOffset.x,
                            displayHeight * debugOutput.stepInfo.currentTile.y + displayOffset.y,
                            displayWidth + displayWidth * debugOutput.stepInfo.currentTile.x + displayOffset.x,
                            displayHeight + displayHeight * debugOutput.stepInfo.currentTile.y + displayOffset.y),
                        greenHighlightTexture);

                    GUI.DrawTexture(
                        Rect.MinMaxRect(
                            displayWidth * debugOutput.stepInfo.targetTile.x + displayOffset.x,
                            displayHeight * debugOutput.stepInfo.targetTile.y + displayOffset.y,
                            displayWidth + displayWidth * debugOutput.stepInfo.targetTile.x + displayOffset.x,
                            displayHeight + displayHeight * debugOutput.stepInfo.targetTile.y + displayOffset.y),
                        highlightTexture);
                }
            }
        }

        private void DisplayWaveDebug(Model.StepInfo stepInfo, bool[][] wave, (int, int)[] orientedToTileId)
        {
            debugOutput = (stepInfo, WFC_Texture2DTile.DebugToOutput(stepInfo, wave, tiles, orientedToTileId));
        }
    }
}