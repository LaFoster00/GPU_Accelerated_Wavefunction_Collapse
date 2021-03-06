using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using USCSL;

namespace WFC.Tiling
{
    public static class WFC_TilessetUtils
    {
        public static int ToIndex(this Rotation rotation)
        {
            switch (rotation)
            {
                default:
                case Rotation.R0:
                    return 0;
                case Rotation.R90:
                    return 1;
                case Rotation.R180:
                    return 2;
                case Rotation.R270:
                    return 3;
            }
        }

        public static Rotation ToRotation(this int index)
        {
            switch (index)
            {
                default:
                case 0:
                    return Rotation.R0;
                case 1:
                    return Rotation.R90;
                case 2:
                    return Rotation.R180;
                case 3:
                    return Rotation.R270;
            }
        }

        public static int ToIndex(this Symmetry2D symmetry)
        {
            switch (symmetry)
            {
                case Symmetry2D.X:
                    return 0;
                case Symmetry2D.I:
                    return 1;
                case Symmetry2D.Diag:
                    return 2;
                case Symmetry2D.T:
                    return 3;
                case Symmetry2D.L:
                    return 4;
                case Symmetry2D.F:
                default:
                    return 5;
            }
        }

        /*
         Orientation IDs are the ideas generated in GenerateOrientedTileIds() and point to the
         uniquely oriented variations of each tile. This is needed to represent each permutation of each tile
         with a single number and can later be reversed through the data from GenerateOrientedTileIds(). 
         */

        /*
         Generate the map associating an orientation id to the orientation
         id obtained when rotating 90° anticlockwise the tile.
        */
        public static int[] GenerateRotationMap(Symmetry2D symmetry2D)
        {
            switch (symmetry2D)
            {
                case Symmetry2D.X:
                    return new[] {0};
                case Symmetry2D.I:
                case Symmetry2D.Diag:
                    return new[] {1, 0};
                case Symmetry2D.T:
                case Symmetry2D.L:
                    return new[] {1, 2, 3, 0};
                case Symmetry2D.F:
                default:
                    return new[] {1, 2, 3, 0, 5, 6, 7, 4};
            }
        }

        public static int NbOfPossibleOrientations(Symmetry2D symmetry)
        {
            switch (symmetry)
            {
                case Symmetry2D.X:
                    return 1;
                case Symmetry2D.I:
                case Symmetry2D.Diag:
                    return 2;
                case Symmetry2D.T:
                case Symmetry2D.L:
                    return 4;
                default:
                    return 8;
            }
        }

        /*
         Generate the map associating an orientation id to the orientation
         id obtained when reflecting the tile along the x axis.
        */
        public static int[] GenerateReflectionMap(Symmetry2D symmetry)
        {
            switch (symmetry)
            {
                case Symmetry2D.X:
                    return new[] {0};
                case Symmetry2D.I:
                    return new[] {0, 1};
                case Symmetry2D.Diag:
                    return new[] {1, 0};
                case Symmetry2D.T:
                    return new[] {0, 3, 2, 1};
                case Symmetry2D.L:
                    return new[] {1, 0, 3, 2};
                case Symmetry2D.F:
                default:
                    return new[] {4, 7, 6, 5, 0, 3, 2, 1};
            }
        }

        /*
       Generate the map associating an orientation id and an additional action to the
       resulting orientation id. An action is an additional transform.
       Actions 0, 1, 2, and 3 are 0°, 90°, 180°, and 270° anticlockwise rotations.
       Actions 4, 5, 6, and 7 are actions 0, 1, 2, and 3 preceded by a reflection
       on the x axis.
       
       E.g: We need to find out the orientation of given tile X which is rotated by 90° (orientation index 1),
       after we rotate it another 90° and flip it. We retrieve the ActionMap at [5(action), 1 (original orientation)] 
       and get the resulting orientation index.
       */
        public static int[,] GenerateActionMap(Symmetry2D symmetry)
        {
            int[] rotationMap = GenerateRotationMap(symmetry);
            int[] reflectionMap = GenerateReflectionMap(symmetry);
            int size = rotationMap.Length;
            int[,] actionMap = new int[8, size];

            // Generate base transform (index [0,0-size] is just the same as getting the orientation id directly)
            for (int i = 0; i < size; ++i)
            {
                actionMap[0, i] = i;
            }

            // Generate all none reflected lookups
            for (int a = 1; a < 4; ++a)
            {
                for (int i = 0; i < size; ++i)
                {
                    actionMap[a, i] = rotationMap[actionMap[a - 1, i]];
                }
            }

            // Generate the base reflected lookup
            for (int i = 0; i < size; ++i)
            {
                actionMap[4, i] = reflectionMap[i];
            }

            // Generate all other reflected and rotated lookups 
            for (int a = 5; a < 8; ++a)
            {
                for (int i = 0; i < size; ++i)
                {
                    actionMap[a, i] = rotationMap[actionMap[a - 1, i]];
                }
            }

            return actionMap;
        }

        public static int[][,] GenerateAllActionMaps()
        {
            return new[]
            {
                GenerateActionMap(Symmetry2D.X),
                GenerateActionMap(Symmetry2D.I),
                GenerateActionMap(Symmetry2D.Diag),
                GenerateActionMap(Symmetry2D.T),
                GenerateActionMap(Symmetry2D.L),
                GenerateActionMap(Symmetry2D.F),
            };
        }
    }
}