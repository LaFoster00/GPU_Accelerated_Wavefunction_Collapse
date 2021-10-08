#if UNITY_EDITOR
using UnityEditor;
#endif
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Random = UnityEngine.Random;

namespace WFC.Tiling
{
    //[ExecuteInEditMode]
    public class WFC_Manager_TextureTile : MonoBehaviour
    {
        public WFC_Texture2DTile[] tiles;

        public int width;
        public int height;

        private void Start()
        {
            double startTime = Time.realtimeSinceStartup;
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

            TilingWFC<Texture2D> tilingWfc = new TilingWFC<Texture2D>(tiles.Cast<WFC_2DTile<Texture2D>>().ToArray(),
                neighbours.Cast<Neighbour<Texture2D>>().ToArray(), height, width, true, 
                Random.Range(Int32.MinValue, Int32.MaxValue));

            (bool Success, Texture2D[,] Result) result = tilingWfc.Run();

            if (result.Success)
            {
                print($"Hurray. It only took {Time.realtimeSinceStartup - startTime} seconds to complete this really simple task!");
            }
        }

        /*
        private void OnEnable()
        {
#if UNITY_EDITOR
            AssetDatabase.importPackageCompleted += OnImportPackageCompleted;
#endif
        }

        private void OnDisable()
        {
#if UNITY_EDITOR
            AssetDatabase.importPackageCompleted -= OnImportPackageCompleted;
#endif
        }

        private void OnImportPackageCompleted(string packagename)
        {
            Dictionary<WFC_Texture2DTile, int> tile_ids = new Dictionary<WFC_Texture2DTile, int>(tiles.Length);
            int tileIndex = 1;
            foreach (var tile in tiles)
            {
                tile_ids.Add(tile, tileIndex++);
            }

            foreach (var tile in tiles)
            {
                foreach (var neighbour in tile.neighbours)
                {

                }

            }
        }
        */
    }
}