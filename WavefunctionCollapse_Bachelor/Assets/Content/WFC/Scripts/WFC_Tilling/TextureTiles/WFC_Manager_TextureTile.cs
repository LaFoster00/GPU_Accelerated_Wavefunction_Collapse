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
        public (int2 currentCell, int2 targetCell, List<Texture2D>[,] debug) debugOutput;

        [SerializeField] private int displayHeight = 16;
        [SerializeField] private int displayWidth = 16;
        [SerializeField] private int width;
        [SerializeField] private int height;
        [SerializeField] private bool drawFrame = true;
        [SerializeField] private Texture2D frameTexture;
        [SerializeField] private Texture2D highlightTexture;
        [SerializeField] private Texture2D greenHighlightTexture;
        [SerializeField] private Propagator.Settings.DebugMode debugMode;
        [SerializeField] private float stepInterval;

        private Propagator.Settings _settings;

        public static WFC_Manager_TextureTile CoroutineManager;

        private void OnEnable()
        {
            CoroutineManager = this;
        }

        private void Start()
        {
            List<TileNeighbour<Texture2D>> neighbours = new List<TileNeighbour<Texture2D>>();
            foreach (var tile in tiles)
            {
                tile.GenerateOrientations();
                foreach (var neighbour in tile.neighbours)
                {
                    if (tiles.Contains(neighbour.leftTile) && tiles.Contains(neighbour.rightTile))
                        neighbours.Add(neighbour);
                }
            }

            _settings = new Propagator.Settings(debugMode, stepInterval, DisplayWaveDebug);

            TilingWFC<Texture2D> tilingWfc = new TilingWFC<Texture2D>(tiles.Cast<WFC_2DTile<Texture2D>>().ToArray(),
                neighbours.Cast<Neighbour<Texture2D>>().ToArray(), height, width, true,
                Random.Range(Int32.MinValue, Int32.MaxValue), _settings);

            StartCoroutine(ExecuteWfc(tilingWfc));
        }

        private void Update()
        {
            _settings.stepInterval = stepInterval;
            _settings.debug = debugMode;
        }

        private IEnumerator ExecuteWfc(TilingWFC<Texture2D> tilingWfc)
        {
            double startTime = Time.realtimeSinceStartup;
            TilingWFC<Texture2D>.WFC_TypedResult wfcResult = new TilingWFC<Texture2D>.WFC_TypedResult();
            
            yield return tilingWfc.Run(wfcResult);
            
            result.Result = wfcResult.result;
            result.Success = wfcResult.success;
            print(
                wfcResult.success
                    ? $"Hurray. It only took {Time.realtimeSinceStartup - startTime} seconds to complete this really simple task!"
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
            else if (debugMode != Propagator.Settings.DebugMode.None && debugOutput.debug != null)
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
                
                GUI.DrawTexture(
                    Rect.MinMaxRect(
                        displayWidth * debugOutput.currentCell.x,
                        displayHeight * debugOutput.currentCell.y,
                        displayWidth + displayWidth * debugOutput.currentCell.x,
                        displayHeight + displayHeight * debugOutput.currentCell.y), 
                    greenHighlightTexture);
                
                GUI.DrawTexture(
                    Rect.MinMaxRect(
                        displayWidth * debugOutput.targetCell.x,
                        displayHeight * debugOutput.targetCell.y,
                        displayWidth + displayWidth * debugOutput.targetCell.x,
                        displayHeight + displayHeight * debugOutput.targetCell.y), 
                    highlightTexture);
            }
        }

        private void DisplayWaveDebug(int2 currentCell, int2 targetCell, bool[,,] wave, (int, int)[] orientedToTileId)
        {
            debugOutput = (currentCell, targetCell, WFC_Texture2DTile.DebugToOutput(wave, tiles, orientedToTileId));
        }
    }
}