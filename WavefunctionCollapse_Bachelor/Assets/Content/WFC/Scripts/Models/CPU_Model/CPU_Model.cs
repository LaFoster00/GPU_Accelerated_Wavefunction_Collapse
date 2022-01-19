using System;
using System.Collections;
using System.Threading.Tasks;
using UnityEngine;
using WFC;
using Random = Unity.Mathematics.Random;

namespace Models.CPU_Model
{
    public abstract class CPU_Model : Model
    {
        protected CPU_Model(int width, int height, int patternSize, bool periodic) : base(width, height, patternSize,
            periodic)
        {
        }

        protected class WFC_Objects
        {
            public Random random;
        }
    }
}