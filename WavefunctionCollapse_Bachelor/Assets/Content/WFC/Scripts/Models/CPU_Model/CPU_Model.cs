using System;
using System.Collections;
using System.Threading.Tasks;
using UnityEngine;
using WFC;

public abstract class CPU_Model : Model
{
    protected CPU_Model(int width, int height, int patternSize, bool periodic) : base(width, height, patternSize, periodic)
    {
    }
}