using System.Collections;
using System.Collections.Generic;
using UnityEngine;

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
    
    /**
   * Generate the map associating an orientation id to the orientation
   * id obtained when rotating 90° anticlockwise the tile.
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

    /**
   * Generate the map associating an orientation id to the orientation
   * id obtained when reflecting the tile along the x axis.
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
    
     /**
   * Generate the map associating an orientation id and an action to the
   * resulting orientation id.
   * Actions 0, 1, 2, and 3 are 0°, 90°, 180°, and 270° anticlockwise rotations.
   * Actions 4, 5, 6, and 7 are actions 0, 1, 2, and 3 followed by a reflection
   * on the x axis.
   */
    public static int[,] GenerateActionMap(Symmetry2D symmetry)
    {
        int[] rotationMap = GenerateRotationMap(symmetry);
        int[] reflectionMap = GenerateReflectionMap(symmetry);
        int size = rotationMap.Length;
        int[,] actionMap = new int[8, size];
        
        for (int i = 0; i < size; ++i)
        {
            actionMap[0, i] = i;
        }

        for (int a = 1; a < 4; ++a)
        {
            for (int i = 0; i < size; ++i)
            {
                actionMap[a, i] = rotationMap[actionMap[a - 1, i]];
            }
        }

        for (int i = 0; i < size; ++i)
        {
            actionMap[4, i] = reflectionMap[actionMap[0, i]];
        }

        for (int a = 5; a < 8; ++a)
        {
            for (int i = 0; i < size; ++i)
            {
                actionMap[a, i] = rotationMap[actionMap[a - 1, i]];
            }
        }

        return actionMap;
    }
    
    /**
   * Generate all distinct rotations of a 2D array given its symmetries;
   */
    public static List<T[,]> GenerateOriented<T>(T[,] data, Symmetry2D symmetry) {
        List<T[,]> oriented = new List<T[,]>();
        oriented.Add(data);
        
        switch (symmetry) {
            case Symmetry2D.I:
            case Symmetry2D.Diag:
                oriented.Add(data.Rotated());
                break;
            case Symmetry2D.T:
            case Symmetry2D.L:
                oriented.Add(data = data.Rotated());
                oriented.Add(data = data.Rotated());
                oriented.Add(data = data.Rotated());
                break;
            case Symmetry2D.F:
                oriented.Add(data = data.Rotated());
                oriented.Add(data = data.Rotated());
                oriented.Add(data = data.Rotated());
                oriented.Add(data = data.Rotated().Reflected());
                oriented.Add(data = data.Rotated());
                oriented.Add(data = data.Rotated());
                oriented.Add(data = data.Rotated());
                break;
            default:
                break;
        }

        return oriented;
    }
}
