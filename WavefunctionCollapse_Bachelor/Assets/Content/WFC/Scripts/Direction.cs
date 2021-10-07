/*
 * Copyright (c) 2018-2019 Mathieu Fehr and Nathanaël Courant.
 * MIT License
 */

using Unity.Mathematics;
using UnityEngine;

public enum Direction
{
    Left = 1,
    Right = 2,
    Up = 3,
    Down = 0,
    None = -1
}

public static class Directions
{
    public static int ToX(this Direction direction)
    {
        return DirectionsX[(int) direction];
    }

    public static int ToY(this Direction direction)
    {
        return DirectionsY[(int) direction];
    }

    public static Direction ToDirection(this int direction)
    {
        if (direction >= 0 && direction <= 3)
            return (Direction) direction;
        Debug.Log($"{direction} is not valid. Returning none!");
        return Direction.None;
    }

    /*
     * A direction is represented by an unsigned integer in the range [0; 3].
     * The x and y values of the direction can be retrieved in these tables.
     */
    public static readonly int[] DirectionsX = new[] {0, -1, 1, 0};
    public static readonly int[] DirectionsY = new[] {-1, 0, 0, 1};

    public static int GetOppositeDirection(int direction)
    {
        return 3 - direction;
    }

    public static Direction GetOppositeDirection(Direction direction)
    {
        return (Direction)(3 - (int) direction);
    }
}