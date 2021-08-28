using System.Collections;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using WFC;

namespace WFC
{
    [CreateAssetMenu(fileName = "WFC_Tile_", menuName = "WFC/Tile")]
    public class WFC_Tile : ScriptableObject
    {
        #region PROPERTIES

        public half tileId;

        public half[][] possibleNeighbours = new half[WFC_Constants.NumNeighbours][];

        #endregion
    }
}