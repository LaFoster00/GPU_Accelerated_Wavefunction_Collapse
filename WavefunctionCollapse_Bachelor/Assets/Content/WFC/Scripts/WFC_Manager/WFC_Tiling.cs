using System;
using System.Collections.Generic;
using System.Linq;

/**
 * Options needed to use the tiling wfc.
 */
public struct TilingWFCOptions
{
    public bool periodicOutput;
};
/**
 * Class generating a new image with the tiling WFC algorithm.
 */
public class TilingWFC<T> 
{
    /* The distincts tiles. */
  private WFC_2DTile<T>[] _tiles;

  /* Map ids of oriented tiles to tile and orientation. */
  private (int,int)[] _idToOrientedTile;

  /* Map tile and orientation to oriented tile id. */
  private int[][] _orientedTileIds;

  /* Options needed to use the tiling wfc. */
  private TilingWFCOptions _options;

  /* The generic wfc model */
  private Model _model;
  
  /* Dimensions */
  private int _height, _width;

  public TilingWFC(WFC_2DTile<T>[] tiles, Neighbour<T>[] neighbours, int height, int width, TilingWFCOptions options,
      int seed)
  {
      _tiles = tiles;
      var tileIds = GenerateOrientedTileIds(tiles);
      _idToOrientedTile = tileIds.Item1;
      _orientedTileIds = tileIds.Item2;
      _options = options;
      _height = height;
      _width = width;
      _model = new Model(_options.periodicOutput, seed, get_tiles_weights(tiles),
          generate_propagator(neighbours, tiles, _idToOrientedTile, _orientedTileIds), height, width);
  }

  /* Generate mapping from id to oriented tiles and vice versa.*/
  static ((int, int)[], int[][]) GenerateOrientedTileIds(WFC_2DTile<T>[] tiles)
  {
      List<(int, int)> idToOrientedTile = new List<(int, int)>();
      int[][] orientedTileIds = new int[tiles.Length][];

      int id = 0;
      for (int tID = 0; tID < tiles.Length; tID++) // tI = tileID
      {
          orientedTileIds[tID] = new int[tiles[tID].orientations.Length];
          for (int oID = 0; oID < tiles[tID].orientations.Length; oID++) //oID = orientationID
          {
              idToOrientedTile.Add((tID, oID)); // Each oriented tile id (Solver) will return their original tile and its orientation index
              orientedTileIds[tID][oID] = id; // Each original tile id and orientation will return their oriented tile id (Solver) 
              id++;
          }
      }

      return (idToOrientedTile.ToArray(), orientedTileIds);
  }

  /**
   * Generate the propagator which will be used in the wfc algorithm.
   */
  static List<List<int>[]> generate_propagator(
      List<(int, int, int, int)> neighbors,
      WFC_2DTile<T>[] tiles,
      (int, int)[] idToOrientedTile,
      int[][] orientedTileIds)
  {
      int nbOrientedTiles = idToOrientedTile.Length;
      /* bool[nbOrientedTiles][4][nbOrientedTiles]
       
       std::vector<std::array<std::vector<bool>, 4>> dense_propagator(
       nb_oriented_tiles, {std::vector<bool>(nb_oriented_tiles, false),
                            std::vector<bool>(nb_oriented_tiles, false),
                            std::vector<bool>(nb_oriented_tiles, false),
                            std::vector<bool>(nb_oriented_tiles, false)});
       */
      
      // Create #nbOrientedTiles arrays filled with 4 arrays filled with #nbOrientedTiles false values
      bool[][][] densePropagator =
          Enumerable.Repeat( // Create #nbOrientedTiles arrays filled with 4 arrays filled with #nbOrientedTiles false values
              Enumerable.Repeat(
                  Enumerable.Repeat(
                      false, nbOrientedTiles).ToArray(),
                  4).ToArray(),
              nbOrientedTiles).ToArray();

      foreach (var neighbor in neighbors)
      {
          int tile1 = neighbor.Item1;
          int orientation1 = neighbor.Item2;
          int tile2 = neighbor.Item3;
          int orientation2 = neighbor.Item4;
          int[,] action_map1 = WFC_TilessetUtils.GenerateActionMap(tiles[tile1].symmetry);
          int[,] action_map2 = WFC_TilessetUtils.GenerateActionMap(tiles[tile2].symmetry);

          Action<int, int> add = (action, direction) =>
          {
              int tempOrientation1 = action_map1[action, orientation1];
              int tempOrientation2 = action_map2[action, orientation2];
              int orientedTileID1 = orientedTileIds[tile1][tempOrientation1];
              int orientedTileID2 = orientedTileIds[tile2][tempOrientation2];
              densePropagator[orientedTileID1][direction][orientedTileID2] = true;
              direction = Direction.GetOppositeDirection(direction);
              densePropagator[orientedTileID2][direction][orientedTileID1] = true;
          };

          add(0, 2);
          add(1, 0);
          add(2, 1);
          add(3, 3);
          add(4, 1);
          add(5, 3);
          add(6, 2);
          add(7, 0);
      }

      List<List<int>[]> propagator = new List<List<int>[]>(nbOrientedTiles);
      for (int i = 0; i < nbOrientedTiles; ++i)
      {
          for (int j = 0; j < nbOrientedTiles; ++j)
          {
              for (int d = 0; d < 4; ++d)
              {
                  if (densePropagator[i][d][j])
                  {
                      propagator[i][d].Add(j);
                  }
              }
          }
      }

      return propagator;
  }

  /**
   * Get probability of presence of tiles.
   */
  static List<double>
  get_tiles_weights( WFC_2DTile<T>[] tiles) {
    List<double> frequencies = new List<double>();
    for (int i = 0; i < tiles.Length; ++i) {
      for (int j = 0; j < tiles[i].orientations.Length; ++j) {
        frequencies.Add(tiles[i].weight / tiles[i].orientations.Length);
      }
    }
    return frequencies;
  }

  /**
   * Translate the generic WFC result into the image result
   */
  T[,] id_to_tiling(int[,] ids)
  {
      return null;
  }

  void set_tile(int tile_id, int i, int j)
  {
    for (int p = 0; p < _idToOrientedTile.Length; p++) {
      if (tile_id != p) {
        _model.RemoveWavePattern(i, j, p);
      }
    }
  }

  /**
   * Set the tile at a specific position.
   * Returns false if the given tile and orientation does not exist,
   * or if the coordinates are not in the wave
   */
  bool set_tile(int tile_id, int orientation, int i, int j)
  {
    if (tile_id >= _orientedTileIds.Length || orientation >= _orientedTileIds[tile_id].Length || i >= _height || j >= _width) {
      return false;
    }

    int oriented_tile_id = _orientedTileIds[tile_id][orientation];
    set_tile(oriented_tile_id, i, j);
    return true;
  }

  /**
   * Run the tiling wfc and return the result if the algorithm succeeded
   */
  (bool, T[,]) run() {
    var a = _model.Run();
    if (a.Item1 == false) {
      return (false, null);
    }
    return (true, id_to_tiling(a.Item2));
  }
}