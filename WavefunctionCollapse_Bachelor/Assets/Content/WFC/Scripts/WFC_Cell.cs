using System.Collections;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

namespace WFC
{
    public class WFC_Cell : MonoBehaviour
    {
        #region PROPERTIES

        public List<half> currentTilePossibilities = new List<half>();
        
        public List<half> tileWhitelist = new List<half>();
        
        public readonly WFC_Cell[] Neighbours = new WFC_Cell[WFC_Constants.NumNeighbours];

        #endregion
    }
}