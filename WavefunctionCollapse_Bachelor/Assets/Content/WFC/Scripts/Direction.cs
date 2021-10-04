/*
 * Copyright (c) 2018-2019 Mathieu Fehr and Nathanaël Courant.
 * MIT License
 */

public static class Direction
{
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
}