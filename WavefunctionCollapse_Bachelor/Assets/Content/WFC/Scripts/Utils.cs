using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;

public static class Utils
{
    public static (int x, int y) IdToXY(this int a, int width)
    {
        return (a % width, a / width);
    }
    
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
    
    public static int RandomFromDistribution(this double[] distribution, double threshold)
    {
        double sum = distribution.Sum();
        for (int i = 0; i < distribution.Length; i++)
        {
            distribution[i] /= sum;
        }
        
        double x = 0;
        
        for (int pattern = 0; pattern < distribution.Length; pattern++)
        {
            x += distribution[pattern];
            if (threshold <= x) return pattern;
            pattern++;
        }
        
        return 0;
    }

}
