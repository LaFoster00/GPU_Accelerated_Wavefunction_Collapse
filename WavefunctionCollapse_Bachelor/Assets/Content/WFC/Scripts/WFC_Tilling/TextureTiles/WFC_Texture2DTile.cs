using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using USCSL;
using WFC;

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
                    (int Tile, int Orientation) currentTile = orientedToTileId[result[y, x]];
                    output[y, x] = tiles[currentTile.Tile].orientations[currentTile.Orientation];
                }
            }
            Debug.Log("Such texture, much wow!");
            return output;
        }

        public static List<Texture2D>[,] DebugToOutput(bool[,,] wave, WFC_Texture2DTile[] tiles, (int, int)[] orientedToTileId)
        {
            List<Texture2D>[,] output = new List<Texture2D>[wave.GetLength(0), wave.GetLength(1)];
            for (int y = 0; y < wave.GetLength(0); y++)
            {
                for (int x = 0; x < wave.GetLength(1); x++)
                {
                    output[y, x] = new List<Texture2D>();
                    for (int p = 0; p < wave.GetLength(2); p++)
                    {
                        if (!wave[y, x, p]) continue;
                        
                        (int Tile, int Orientation) currentTile = orientedToTileId[p];
                        output[y, x].Add(tiles[currentTile.Tile].orientations[currentTile.Orientation]);
                    }
                }
            }

            return output;
        }
    }
}