using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using WFC;

namespace WFC.CPU
{
    public static class WFC_Solver_CPU
    {
        public static void SolveGrid(WFC_Cell[] cells, bool randomStartCell = false, bool randomCollapseUndecidedCells = true)
        {
            bool allCellsSolved = false;
            WFC_Cell openCell = WFC_Utility.GetMinEntropyCell(cells);
            while (!allCellsSolved)
            {
                
            }
        }
    }
}