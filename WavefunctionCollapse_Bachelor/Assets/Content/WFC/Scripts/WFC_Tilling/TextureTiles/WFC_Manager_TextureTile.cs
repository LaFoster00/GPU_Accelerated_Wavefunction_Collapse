#if UNITY_EDITOR
using UnityEditor;
#endif
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Unity.Mathematics;
using UnityEngine;
using Random = UnityEngine.Random;

namespace WFC.Tiling
{
    //[ExecuteInEditMode]
    public class WFC_Manager_TextureTile : MonoBehaviour
    {
        public WFC_Texture2DTile[] tiles;

        public (bool Success, Texture2D[,] Result) result;
        public (Model.StepInfo stepInfo, List<Texture2D>[,] debug) debugOutput;

        [SerializeField] private bool periodic = true;
        [SerializeField] private int displayHeight = 16;
        [SerializeField] private int displayWidth = 16;
        [SerializeField] private int maxNumIterations = -1;
        [SerializeField] private int width;
        [SerializeField] private int height;
        [SerializeField] private bool drawFrame = true;
        [SerializeField] private Texture2D frameTexture;
        [SerializeField] private Texture2D highlightTexture;
        [SerializeField] private Texture2D greenHighlightTexture;
        [SerializeField] private Texture2D yellowFrameTexture;
        [SerializeField] private Model.PropagatorSettings.DebugMode debugMode;
        [SerializeField] private float stepInterval;

        private Model.PropagatorSettings _settings;

        private void Start()
        {
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

            StartCoroutine(ExecuteWfc(neighbours));
        }

        private void Update()
        {
            _settings.stepInterval = stepInterval;
            _settings.debug = debugMode;
        }

        private IEnumerator ExecuteWfc(List<TileNeighbour<Texture2D>> neighbours)
        {
            double startTime = Time.realtimeSinceStartup;
            int itteration = 0;
            while (!result.Success)
            {
                TilingWFC<Texture2D> tilingWfc = new TilingWFC<Texture2D>(tiles.Cast<WFC_2DTile<Texture2D>>().ToArray(),
                    neighbours.Cast<Neighbour<Texture2D>>().ToArray(), height, width, periodic, _settings);
                
                TilingWFC<Texture2D>.WFC_TypedResult wfcResult = new TilingWFC<Texture2D>.WFC_TypedResult();

                Unity.Mathematics.Random random = Unity.Mathematics.Random.CreateFromIndex((uint)Random.Range(Int32.MinValue, Int32.MaxValue));
                yield return tilingWfc.Run(random.NextUInt(), maxNumIterations, wfcResult);

                result.Result = wfcResult.result;
                result.Success = wfcResult.success;
                itteration++;
            }

            print(
                result.Success
                    ? $"Hurray. It only took {Time.realtimeSinceStartup - startTime} seconds and {itteration} tries to complete this really simple task!"
                    : $"WuHuwhwaaawg i cant do it!");
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
                            Rect.MinMaxRect(displayWidth * x, displayHeight * y, displayWidth + displayWidth * x,
                                displayHeight + displayHeight * y), result.Result[y, x]);
                        if (drawFrame)
                        {
                            GUI.DrawTexture(
                                Rect.MinMaxRect(displayWidth * x, displayHeight * y, displayWidth + displayWidth * x,
                                    displayHeight + displayHeight * y), frameTexture);
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
                        int texNumSide = (int)math.ceil(math.sqrt(currentCellTextures.Count));
                        int texDisplayWidth = displayWidth / texNumSide;
                        int texDisplayHeight = displayHeight / texNumSide;
                        
                        for (int texY = 0; texY < texNumSide; texY++)
                        {
                            for (int texX = 0; texX < texNumSide; texX++)
                            {
                                int texIndex = texX + texY * texNumSide;
                                if (texIndex >= currentCellTextures.Count) continue;
                                int xmin = displayWidth * x + texX * texDisplayWidth;
                                int ymin = displayHeight * y + texY * texDisplayHeight;
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
                            Rect.MinMaxRect(displayWidth * x, displayHeight * y, displayWidth + displayWidth * x,
                                displayHeight + displayHeight * y), frameTexture);
                    }
                }

                for (int node = 0; node < debugOutput.stepInfo.numPropagatingCells; node++)
                {
                    var coord = debugOutput.stepInfo.propagatingCells[node].Item1.IdToXY(debugOutput.stepInfo.width);
                    GUI.DrawTexture(
                        Rect.MinMaxRect(
                            displayWidth * coord.x,
                            displayHeight * coord.y,
                            displayWidth + displayWidth * coord.x,
                            displayHeight + displayHeight * coord.y),
                        yellowFrameTexture);
                }

                GUI.DrawTexture(
                    Rect.MinMaxRect(
                        displayWidth * debugOutput.stepInfo.currentTile.x,
                        displayHeight * debugOutput.stepInfo.currentTile.y,
                        displayWidth + displayWidth * debugOutput.stepInfo.currentTile.x,
                        displayHeight + displayHeight * debugOutput.stepInfo.currentTile.y), 
                    greenHighlightTexture);
                
                GUI.DrawTexture(
                    Rect.MinMaxRect(
                        displayWidth * debugOutput.stepInfo.targetTile.x,
                        displayHeight * debugOutput.stepInfo.targetTile.y,
                        displayWidth + displayWidth * debugOutput.stepInfo.targetTile.x,
                        displayHeight + displayHeight * debugOutput.stepInfo.targetTile.y), 
                    highlightTexture);
            }
        }

        private void DisplayWaveDebug(Model.StepInfo stepInfo, bool[][] wave, (int, int)[] orientedToTileId)
        {
            debugOutput = (stepInfo, WFC_Texture2DTile.DebugToOutput(stepInfo, wave, tiles, orientedToTileId));
        }
    }
}