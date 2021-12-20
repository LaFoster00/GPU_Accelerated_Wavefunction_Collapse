using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using WFC;

public class CPU_Model_BurstJob : CPU_Model
{
    public CPU_Model_BurstJob(int width, int height, int patternSize, bool periodic) : base(width, height, patternSize, periodic)
    {
    }

    public override IEnumerator Run(uint seed, int limit, WFC_Result result)
    {
        throw new System.NotImplementedException();
    }

    public override void Ban(int node, int pattern)
    {
        throw new System.NotImplementedException();
    }
}
