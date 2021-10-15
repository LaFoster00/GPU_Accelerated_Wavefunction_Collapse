using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Unity.Mathematics;
using UnityEngine;
using WFC.Tiling;

namespace WFC
{
    public class Propagator
    {
        /*
         The size of the patterns.
        */
        private readonly int _patternsSize;

        /*
         propagator[pattern1][direction] contains all the patterns that can
         be placed next to pattern1 in the direction.
        */
        private List<int>[][] _propagatorState;

        /*
         The wave width and height.
        */
        private readonly int _waveWidth;

        private readonly int _waveHeight;

        /**
   * True if the wave and the output is toric.
   */
        private readonly bool _periodicOutput;

        /*
         All the tuples (y, x, pattern) that should be propagated.
         The tuple should be propagated when wave.get(y, x, pattern) is set to
         false.
        */
        List<(int, int, int)> _propagating = new List<(int, int, int)>();

        /*
         compatible.get(y, x, pattern)[direction] contains the number of patterns
         present in the wave that can be placed in the cell next to (y,x) in the
         opposite direction of direction without being in contradiction with pattern
         placed in (y,x). If wave.get(y, x, pattern) is set to false, then
         compatible.get(y, x, pattern) has every element negative or null
        */
        private int[,,][] _compatible;

        public class StepInfo
        {
            public (int y, int x, int pattern) currentTile;
            public (int y, int x, int pattern) targetTile;
            public List<(int, int, int)> propagatingCells;
        }

        public StepInfo stepInfo;
        
        public class Settings
        {
            public enum DebugMode
            {
                None,
                OnChange,
                OnSet
            }

            public DebugMode debug;
            public float stepInterval;
            public Action<StepInfo, bool[,,], (int, int)[]> debugToOutput;
            public (int, int)[] orientedToTileId;

            public Settings(DebugMode debug, float stepInterval, Action<StepInfo, bool[,,], (int, int)[]> debugToOutput)
            {
                this.debug = debug;
                this.stepInterval = stepInterval;
                this.debugToOutput = debugToOutput;
            }
        }

        private Settings _settings;

        /*
         Constructor building the propagator and initializing compatible.
        */
        public Propagator(int waveHeight, int waveWidth, bool periodicOutput,
            List<int>[][] propagatorState, Settings settings)
        {
            _waveHeight = waveHeight;
            _waveWidth = waveWidth;
            _periodicOutput = periodicOutput;
            _propagatorState = propagatorState;
            _patternsSize = propagatorState.Length;

            _compatible = new int[_waveHeight, _waveWidth, _patternsSize][];
            _settings = settings;
            InitCompatible();
            stepInfo = new StepInfo()
            {
                propagatingCells = _propagating,
            };
        }

        /* Initialize compatible. */
        private void InitCompatible()
        {
            // We compute the number of pattern compatible in all directions.
            for (int y = 0; y < _waveHeight; y++)
            {
                for (int x = 0; x < _waveWidth; x++)
                {
                    /* In case of tiling pattern is an oriented tile not an actual pattern. */
                    for (int pattern = 0; pattern < _patternsSize; pattern++)
                    {
                        int[] value = new int[4];
                        for (int direction = 0; direction < 4; direction++)
                        {
                            value[direction] = _propagatorState[pattern][Directions.GetOppositeDirection(direction)]
                                .Count;
                        }

                        _compatible[y, x, pattern] = value;
                    }
                }
            }
        }


        /*
         Add an element to the propagator.
         This function is called when wave.get(y, x, pattern) is set to false.
        */
        public void AddToPropagator(int y, int x, int pattern)
        {
            // All the direction are set to 0, since the pattern cannot be set in (y,x).
            int[] temp = new int[4];
            _compatible[y, x, pattern] = temp;
            _propagating.Add((y, x, pattern));
        }

        /* Propagate the information given with add_to_propagator. */
        public IEnumerator Propagate(Wave wave)
        {
            // We propagate every element while there is element to propagate.
            while (_propagating.Count != 0)
            {
                // The cell and pattern that has been set to false.
                stepInfo.currentTile = _propagating.Last();
                _propagating.RemoveAt(_propagating.Count - 1);

                // We propagate the information in all 4 directions.
                for (int direction = 0; direction < 4; direction++)
                {
                    // We get the next cell in the direction direction.
                    int dx = Directions.DirectionsX[direction];
                    int dy = Directions.DirectionsY[direction];
                    if (_periodicOutput)
                    {
                        stepInfo.targetTile.x = (stepInfo.currentTile.x + dx + wave.width) % wave.width;
                        stepInfo.targetTile.y = (stepInfo.currentTile.y + dy + wave.height) % wave.height;
                    }
                    else
                    {
                        stepInfo.targetTile.x = stepInfo.currentTile.x + dx;
                        stepInfo.targetTile.y = stepInfo.currentTile.y + dy;
                        if (stepInfo.targetTile.x < 0 || stepInfo.targetTile.x >= wave.width)
                        {
                            continue;
                        }

                        if (stepInfo.targetTile.y < 0 || stepInfo.targetTile.y >= wave.height)
                        {
                            continue;
                        }
                    }

                    // The index of the second cell, and the patterns compatible with the just discarded pattern
                    int i2 = stepInfo.targetTile.x + stepInfo.targetTile.y * wave.width;
                    List<int> patterns = _propagatorState[stepInfo.currentTile.pattern][direction];

                    /* For every pattern that could be placed in that cell without being in contradiction with pattern1 */
                    foreach (var pat in patterns)
                    {
                        // We decrease the number of compatible patterns in the opposite
                        // direction If the pattern was discarded from the wave, the element
                        // is still negative, which is not a problem
                        int[] compatible = _compatible[stepInfo.targetTile.y, stepInfo.targetTile.x, pat];
                        compatible[direction]--;

                        // If the element was set to 0 with this operation, we need to remove
                        // the pattern from the wave, and propagate the information
                        if (compatible[direction] == 0)
                        {
                            AddToPropagator(stepInfo.targetTile.y, stepInfo.targetTile.x, pat);
                            wave.Set(i2, pat, false);
                            if (_settings.debug == Settings.DebugMode.OnSet)
                            {
                                wave.DebugDrawCurrentState();
                                yield return _settings.stepInterval == 0
                                    ? null
                                    : new WaitForSeconds(_settings.stepInterval);
                            }
                        }
                    }
                    
                    if (_settings.debug == Settings.DebugMode.OnChange)
                    {
                        wave.DebugDrawCurrentState();
                        yield return _settings.stepInterval == 0
                            ? null
                            : new WaitForSeconds(_settings.stepInterval);
                    }
                }
            }
        }
    }
}