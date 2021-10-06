using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using UnityEngine;

public enum Symmetry2D
{
    L,
    I,
    X,
    T,
    Diag, // : \
    F, // No symmetries, all transformations needed.
}

/**
 * Options needed to use the tiling wfc.
 */
public struct TilingWFCOptions
{
    public bool periodicOutput;
};

[Serializable]
public class WFC_2DTileOld<T>
{
    public string name;
    public Texture2D tile;
    public double weight; // Its weight on the distribution of presence of tiles
    public Symmetry2D symmetry = Symmetry2D.X;
    public List<T[,]> data; // The different orientations of the tile
    public WFC_Input_Tileset[] neighboursLeft;
    public WFC_Input_Tileset[] neighboursRight;
    public WFC_Input_Tileset[] neighboursTop;
    public WFC_Input_Tileset[] neighboursBottom;

    /**
   * Create a tile with its differents orientations, its symmetries and its
   * weight on the distribution of tiles.
   */
    public WFC_2DTileOld(List<T[,]> data, Symmetry2D symmetry, double weight)
    {
        this.data = data; 
        this.symmetry = symmetry;
        this.weight = weight;
    }

    /*
     * Create a tile with its base orientation, its symmetries and its
     * weight on the distribution of tiles.
     * The other orientations are generated with its first one.
     */
    public WFC_2DTileOld(T[,] data, Symmetry2D symmetry, double weight)
    {
        this.data = WFC_TilessetUtils.GenerateOriented(data, symmetry);
        this.symmetry = symmetry;
        this.weight = weight;
    }
}

/**
 * Class generating a new image with the tiling WFC algorithm.
 */
public class TilingWFC<T> {
    /**
   * The distincts tiles.
   */
  private List<WFC_2DTileOld<T>> tiles;

  /**
   * Map ids of oriented tiles to tile and orientation.
   */
  private List<(int,int)> id_to_oriented_tile;

  /**
   * Map tile and orientation to oriented tile id.
   */
  private List<List<int>> oriented_tile_ids;

  /**
   * Otions needed to use the tiling wfc.
   */
  private TilingWFCOptions options;

  /**
   * The underlying generic WFC algorithm.
   */
  private WFC wfc;

  /**
   * The number of vertical tiles
   */
  int height;

  /**
   * The number of horizontal tiles
   */
  int width;

  /**
   * Generate mapping from id to oriented tiles and vice versa.
   */
  static (List<(int, int)>, List<List<int>>)
  generate_oriented_tile_ids(List<WFC_2DTileOld<T>> tiles)
  {
    List<(int,int)> id_to_oriented_tile = new List<(int, int)>();
    List<List<int>> oriented_tile_ids = new List<List<int>>();

    int id = 0;
    for (int i = 0; i < tiles.Count; i++) {
      oriented_tile_ids.Add(new List<int>());
      for (int j = 0; j < tiles[i].data.Count; j++) {
        id_to_oriented_tile.Add((i, j));
        oriented_tile_ids[i].Add(id);
        id++;
      }
    }

    return (id_to_oriented_tile, oriented_tile_ids);
  }

  /**
   * Generate the propagator which will be used in the wfc algorithm.
   */
  static List<List<int>[]> generate_propagator(
      List<(int, int, int, int)> neighbors,
      List<WFC_2DTileOld<T>> tiles,
      List<(int, int)> id_to_oriented_tile,
      List<List<int>> oriented_tile_ids) {
    int nb_oriented_tiles = id_to_oriented_tile.Count;
    List<List<bool>[]> dense_propagator =
        Enumerable.Repeat(
            Enumerable.Repeat(
                Enumerable.Repeat(
                    false, 
                    nb_oriented_tiles).ToList(),
                4).ToArray(),
            nb_oriented_tiles).ToList();
    
    foreach (var neighbor in neighbors) {
      int tile1 = neighbor.Item1;
      int orientation1 = neighbor.Item2;
      int tile2 = neighbor.Item3;
      int orientation2 = neighbor.Item4;
      int[,] action_map1 =
          WFC_TilessetUtils.GenerateActionMap(tiles[tile1].symmetry);
      int[,] action_map2 =
          WFC_TilessetUtils.GenerateActionMap(tiles[tile2].symmetry);

      Action<int, int> add = ( action, direction) => {
          int temp_orientation1 = action_map1[action,orientation1];
          int temp_orientation2 = action_map2[action,orientation2];
          int oriented_tile_id1 =
              oriented_tile_ids[tile1][temp_orientation1];
          int oriented_tile_id2 =
              oriented_tile_ids[tile2][temp_orientation2];
          dense_propagator[oriented_tile_id1][direction][oriented_tile_id2] =
              true;
          direction = Direction.GetOppositeDirection(direction);
          dense_propagator[oriented_tile_id2][direction][oriented_tile_id1] =
              true;
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

    List<List<int>[]> propagator = new List<List<int>[]>(nb_oriented_tiles);
    for (int i = 0; i < nb_oriented_tiles; ++i) {
      for (int j = 0; j < nb_oriented_tiles; ++j) {
        for (int d = 0; d < 4; ++d) {
          if (dense_propagator[i][d][j]) {
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
  get_tiles_weights( List<WFC_2DTileOld<T>> tiles) {
    List<double> frequencies = new List<double>();
    for (int i = 0; i < tiles.Count; ++i) {
      for (int j = 0; j < tiles[i].data.Count; ++j) {
        frequencies.Add(tiles[i].weight / tiles[i].data.Count);
      }
    }
    return frequencies;
  }

  /**
   * Translate the generic WFC result into the image result
   */
  T[,] id_to_tiling(int[,] ids)
  {
      int size = tiles[0].data[0].GetLength(0);
      T[,] tiling = new T[size * ids.GetLength(1), size * ids.GetLength(1)];

      for (int i = 0; i < ids.GetLength(0); i++)
      {
          for (int j = 0; j < ids.GetLength(1); j++)
          {
              (int, int)oriented_tile = id_to_oriented_tile[ids[i, j]];
              for (int y = 0; y < size; y++)
              {
                  for (int x = 0; x < size; x++)
                  {
                      tiling[i * size + y, j * size + x] =
                          tiles[oriented_tile.Item1].data[oriented_tile.Item2][y, x];
                  }
              }
          }
      }

      return tiling;
  }

  void set_tile(int tile_id, int i, int j)
  {
    for (int p = 0; p < id_to_oriented_tile.Count; p++) {
      if (tile_id != p) {
        wfc.RemoveWavePattern(i, j, p);
      }
    }
  }

  /**
   * Construct the TilingWFC class to generate a tiled image.
   */
  TilingWFC(
      List<WFC_2DTileOld<T>> tiles,
      List<(int, int, int, int)> neighbors,
      int height, int width,
      TilingWFCOptions options, int seed)
  {
      this.tiles = tiles;
      this.id_to_oriented_tile = generate_oriented_tile_ids(tiles).Item1;
      this.oriented_tile_ids = generate_oriented_tile_ids(tiles).Item2;
      this.options = options;
      wfc = new WFC(options.periodicOutput, seed, get_tiles_weights(tiles),
          generate_propagator(neighbors, tiles, id_to_oriented_tile,
              oriented_tile_ids), height, width);
      this.height = height;
      this.width = width;
  }

  /**
   * Set the tile at a specific position.
   * Returns false if the given tile and orientation does not exist,
   * or if the coordinates are not in the wave
   */
  bool set_tile(int tile_id, int orientation, int i, int j)
  {
    if (tile_id >= oriented_tile_ids.Count || orientation >= oriented_tile_ids[tile_id].Count || i >= height || j >= width) {
      return false;
    }

    int oriented_tile_id = oriented_tile_ids[tile_id][orientation];
    set_tile(oriented_tile_id, i, j);
    return true;
  }

  /**
   * Run the tiling wfc and return the result if the algorithm succeeded
   */
  (bool, T[,]) run() {
    var a = wfc.Run();
    if (a.Item1 == false) {
      return (false, null);
    }
    return (true, id_to_tiling(a.Item2));
  }
}