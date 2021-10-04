using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using PropagatorState = System.Collections.Generic.List<System.Collections.Generic.List<int>[]>;

public class Propagator
{
  /**
   * The size of the patterns.
   */
  private readonly int _patternsSize;

  /**
   * propagator[pattern1][direction] contains all the patterns that can
   * be placed in next to pattern1 in the direction direction.
   */
  private PropagatorState _propagatorState;

  /**
   * The wave width and height.
   */
  private readonly int _waveWidth;

  private readonly int _waveHeight;

  /**
   * True if the wave and the output is toric.
   */
  private readonly bool _periodicOutput;

  /**
   * All the tuples (y, x, pattern) that should be propagated.
   * The tuple should be propagated when wave.get(y, x, pattern) is set to
   * false.
   */
  List<(int, int, int)> _propagating;

  /**
   * compatible.get(y, x, pattern)[direction] contains the number of patterns
   * present in the wave that can be placed in the cell next to (y,x) in the
   * opposite direction of direction without being in contradiction with pattern
   * placed in (y,x). If wave.get(y, x, pattern) is set to false, then
   * compatible.get(y, x, pattern) has every element negative or null
   */
  private int[,,][] _compatible;

  /**
   * Initialize compatible.
   */
  private void InitCompatible()
  {
    int[] value = new int[4];
    // We compute the number of pattern compatible in all directions.
    for (int y = 0; y < _waveHeight; y++)
    {
      for (int x = 0; x < _waveWidth; x++)
      {
        for (int pattern = 0; pattern < _patternsSize; pattern++)
        {
          for (int direction = 0; direction < 4; direction++)
          {
            value[direction] =
              (_propagatorState[pattern][Direction.GetOppositeDirection(direction)]
                .Count);
          }

          _compatible[y, x, pattern] = value;
        }
      }
    }
  }

  /*
  * Constructor building the propagator and initializing compatible.
  */
  public Propagator(int waveHeight, int waveWidth, bool periodicOutput,
    PropagatorState propagatorState)
  {
    _patternsSize = propagatorState.Count;
    _propagatorState = new PropagatorState(propagatorState);
    _waveWidth = waveWidth;
    _waveHeight = waveHeight;
    _periodicOutput = periodicOutput;

    _compatible = new int[_waveHeight, _waveWidth, _patternsSize][];
    InitCompatible();
  }


  /**
   * Add an element to the propagator.
   * This function is called when wave.get(y, x, pattern) is set to false.
   */
  public void AddToPropagator(int y, int x, int pattern)
  {
    // All the direction are set to 0, since the pattern cannot be set in (y,x).
    int[] temp = new int[4];
    _compatible[y, x, pattern] = temp;
    _propagating.Add((y, x, pattern));
  }

  /**
   * Propagate the information given with add_to_propagator.
   */
  public void Propagate(Wave wave)
  {
    // We propagate every element while there is element to propagate.
    while (_propagating.Count != 0)
    {

      // The cell and pattern that has been set to false.
      int y1, x1, pattern;
      (y1, x1, pattern) = _propagating.Last();
      _propagating.RemoveAt(_propagating.Count - 1);

      // We propagate the information in all 4 directions.
      for (int direction = 0; direction < 4; direction++)
      {

        // We get the next cell in the direction direction.
        int dx = Direction.DirectionsX[direction];
        int dy = Direction.DirectionsY[direction];
        int x2, y2;
        if (_periodicOutput)
        {
          x2 = ( x1 + dx + wave.Width) % wave.Width;
          y2 = ( y1 + dy + wave.Height) % wave.Height;
        }
        else
        {
          x2 = x1 + dx;
          y2 = y1 + dy;
          if (x2 < 0 || x2 >= wave.Width)
          {
            continue;
          }

          if (y2 < 0 || y2 >= wave.Height)
          {
            continue;
          }
        }

        // The index of the second cell, and the patterns compatible
        int i2 = x2 + y2 * wave.Width;
        ref List<int> patterns = ref _propagatorState[pattern][direction];

        // For every pattern that could be placed in that cell without being in
        // contradiction with pattern1
        foreach (var it in patterns)
        {
          // We decrease the number of compatible patterns in the opposite
          // direction If the pattern was discarded from the wave, the element
          // is still negative, which is not a problem
          ref int[] value = ref _compatible[y2, x2, it];
          value[direction]--;

          // If the element was set to 0 with this operation, we need to remove
          // the pattern from the wave, and propagate the information
          if (value[direction] == 0)
          {
            AddToPropagator(y2, x2, it);
            wave.Set(i2, it, false);
          }
        }
      }
    }
  }
}