using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public static class Utils
{
    public static List<double> Normalize(this List<double> l)
    {
        List<double> lR = new List<double>(l.Count);
        double sumWeights = 0.0;
        foreach (double weight in l) {
            sumWeights += weight;
        }

        double invSumWeights = 1.0/sumWeights;
        for (int i = 0; i < l.Count; i++)
        {
            lR[i] = l[i] * invSumWeights;
        }

        return lR;
    }
    
    public static double[] Normalize(this double[] l)
    {
        double[] lR = new double[l.Length];
        double sumWeights = 0.0;
        foreach (double weight in l) {
            sumWeights += weight;
        }

        double invSumWeights = 1.0/sumWeights;
        for (int i = 0; i < l.Length; i++)
        {
            lR[i] = l[i] * invSumWeights;
        }

        return lR;
    }
    
    /**
   * Return the current 2D array rotated 90° anticlockwise
   */
    public static T[,] Rotated<T>(this T[,] array)
    {
        int height = array.GetLength(0);
        int width = array.GetLength(1);
        T[,] result = new T[width, height];
        for (int y = 0; y < width; y++) {
            for (int x = 0; x < height; x++) {
                result[y, x] = array[x, width - 1 - y];
            }
        }
        return result;
    }
    
    /**
   * Return the current 2D array reflected along the x axis.
   */
    public static T[,] Reflected<T>(this T[,] array)
    {
        int height = array.GetLength(0);
        int width = array.GetLength(1);
        T[,] result = new T[width, height];
        for (int y = 0; y < height; y++) {
            for (int x = 0; x < width; x++) {
                result[y, x] = array[y, width - 1 - x];
            }
        }
        return result;
    }
    
    public static T[,] Make2DArray<T>(this T[] input, int height, int width)
    {
        T[,] output = new T[height, width];
        for (int i = 0; i < height; i++)
        {
            for (int j = 0; j < width; j++)
            {
                output[i, j] = input[i * width + j];
            }
        }
        return output;
    }
}
