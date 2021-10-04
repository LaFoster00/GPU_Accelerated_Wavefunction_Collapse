using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public static class Utils
{
    public static List<double> NormalizeList(this List<double> l)
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
}
