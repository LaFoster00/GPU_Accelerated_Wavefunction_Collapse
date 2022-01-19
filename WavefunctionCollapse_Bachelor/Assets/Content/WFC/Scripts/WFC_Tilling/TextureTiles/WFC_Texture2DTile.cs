using System;
using System.Collections.Generic;
using UnityEngine;
using USCSL;

namespace WFC.Tiling
{
    [CreateAssetMenu(menuName = "WFC/Input/Texture2DTile", fileName = "New Texture2DTile")]
    public class WFC_Texture2DTile : WFC_2DTile<Texture2D>
    {
        protected override Func<Texture2D, Texture2D> GetRotatedContent
        {
            get { return texture2D => texture2D.Rotated(); }
        }

        protected override Func<Texture2D, Texture2D> GetMirroredContent
        {
            get { return texture2D => texture2D.Mirrored(); }
        }

        public override Texture2D[,] ResultToOutput(int[,] result, WFC_2DTile<Texture2D>[] tiles, (int, int)[] orientedToTileId)
        {
            Texture2D[,] output = new Texture2D[result.GetLength(0), result.GetLength(1)];
            for (int y = 0; y < result.GetLength(0); y++)
            {
                for (int x = 0; x < result.GetLength(1); x++)
                {
                    var (tile, orientation) = orientedToTileId[result[y, x]];
                    output[y, x] = tiles[tile].orientations[orientation];
                }
            }
            Debug.Log("Such texture, much wow!");
            return output;
        }

        public static List<Texture2D>[,] DebugToOutput(Model.StepInfo stepInfo, bool[][] wave, WFC_Texture2DTile[] tiles, (int, int)[] orientedToTileId)
        {
            List<Texture2D>[,] output = new List<Texture2D>[stepInfo.height, stepInfo.width];
            for (int y = 0; y < stepInfo.height; y++)
            {
                for (int x = 0; x < stepInfo.width; x++)
                {
                    int node = x + y * stepInfo.width;
                    output[y, x] = new List<Texture2D>();
                    for (int p = 0; p < wave[node].Length; p++)
                    {
                        if (!wave[node][p]) continue;
                        
                        (int Tile, int Orientation) currentTile = orientedToTileId[p];
                        output[y, x].Add(tiles[currentTile.Tile].orientations[currentTile.Orientation]);
                    }
                }
            }

            return output;
        }
    }
}