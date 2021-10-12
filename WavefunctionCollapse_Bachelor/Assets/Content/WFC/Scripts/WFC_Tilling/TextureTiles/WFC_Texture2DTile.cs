using System;
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

        public override Texture2D[,] ResultToOutput(int[,] wave, WFC_2DTile<Texture2D>[] tiles, (int, int)[] orientedToTileId)
        {
            Texture2D[,] output = new Texture2D[wave.GetLength(0), wave.GetLength(1)];
            for (int y = 0; y < wave.GetLength(0); y++)
            {
                for (int x = 0; x < wave.GetLength(1); x++)
                {
                    (int Tile, int Orientation) currentTile = orientedToTileId[wave[y, x]];
                    output[y, x] = tiles[currentTile.Tile].orientations[currentTile.Orientation];
                }
            }
            Debug.Log("Such texture, much wow!");
            return output;
        }
    }
}