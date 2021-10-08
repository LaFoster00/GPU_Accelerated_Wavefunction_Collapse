using System;
using System.Collections.Generic;
using System.Linq;

namespace WFC.Tiling
{
    public interface Neighbour<T>
    {
        public delegate void AddToPropagator(Neighbour<T> neighbour, int[][,] actionMaps, int[][] orientedTileIds,
            bool[][][] propagator, int action, Direction direction);

        public AddToPropagator Add { get; }
    }

/*
 Class generating a new image with the tiling WFC algorithm.
 */
    public class TilingWFC<T> : Model
    {
        /* The distincts tiles. */
        private WFC_2DTile<T>[] _tiles;

        /* Map ids of oriented tiles to tile and orientation. */
        private (int, int)[] _idToOrientedTile;

        /* Map tile and orientation to oriented tile id. */
        private int[][] _orientedTileIds;

        public TilingWFC(WFC_2DTile<T>[] tiles, Neighbour<T>[] neighbours, int height, int width, bool periodicOutput,
            int seed) : base(periodicOutput, seed, height, width)
        {
            (_idToOrientedTile, _orientedTileIds) = GenerateOrientedTileIds(tiles);
            Init(GetTileWeights(tiles), GeneratePropagator(neighbours, tiles, _idToOrientedTile, _orientedTileIds));
            _tiles = tiles;
        }

        /* Generate id and mapping from id to oriented tiles and vice versa.*/
        static ((int, int)[], int[][]) GenerateOrientedTileIds(WFC_2DTile<T>[] tiles)
        {
            List<(int, int)> idToOrientedTile = new List<(int, int)>();
            int[][] orientedTileIds = new int[tiles.Length][];

            int id = 0;
            for (int tID = 0; tID < tiles.Length; tID++) // tI = tileID
            {
                tiles[tID].tileId = tID; // associate each tile with a unique id (no orientation)
                orientedTileIds[tID] = new int[tiles[tID].orientations.Length];
                for (int oID = 0; oID < tiles[tID].orientations.Length; oID++) //oID = orientationID
                {
                    // Each oriented tile id (Solver) will return their original tile and its orientation index
                    idToOrientedTile.Add((tID, oID)); 
                    // Each original tile id and orientation will return their oriented tile id (Solver)
                    orientedTileIds[tID][oID] = id;  
                    id++;
                }
            }

            return (idToOrientedTile.ToArray(), orientedTileIds);
        }

        /*
         Generate the propagator which will be used in the wfc algorithm.
         The propagator holds information on which tile can lie in which direction of all the oriented tiles
        */
        static List<int>[][] GeneratePropagator(
            Neighbour<T>[] neighbors,
            WFC_2DTile<T>[] tiles,
            (int, int)[] idToOrientedTile,
            int[][] orientedTileIds)
        {
            int nbOrientedTiles = idToOrientedTile.Length;
            /* Create #nbOrientedTiles arrays filled with 4 arrays filled with #nbOrientedTiles false values */
            bool[][][] densePropagator =
                Enumerable
                    .Repeat(
                        Enumerable.Repeat(
                            Enumerable.Repeat(
                                false, nbOrientedTiles).ToArray(),
                            4).ToArray(),
                        nbOrientedTiles).ToArray();

            int[][,] actionMaps = WFC_TilessetUtils.GenerateAllActionMaps();

            foreach (var neighbor in neighbors)
            {
                Neighbour<T>.AddToPropagator add = neighbor.Add;

                add(neighbor, actionMaps, orientedTileIds, densePropagator, 0, Direction.Right);
                add(neighbor, actionMaps, orientedTileIds, densePropagator, 1, Direction.Down);
                add(neighbor, actionMaps, orientedTileIds, densePropagator, 2, Direction.Left);
                add(neighbor, actionMaps, orientedTileIds, densePropagator, 3, Direction.Up);
                add(neighbor, actionMaps, orientedTileIds, densePropagator, 4, Direction.Left);
                add(neighbor, actionMaps, orientedTileIds, densePropagator, 5, Direction.Up);
                add(neighbor, actionMaps, orientedTileIds, densePropagator, 6, Direction.Right);
                add(neighbor, actionMaps, orientedTileIds, densePropagator, 7, Direction.Down);
            }

            /* Store the indices of all compatible oriented tiles. */
            List<int>[][] propagator = 
                Enumerable.Repeat(Enumerable.Repeat(new List<int>(), 4).ToArray(), nbOrientedTiles).ToArray();
            for (int i = 0; i < nbOrientedTiles; ++i)
            {
                for (int j = 0; j < nbOrientedTiles; ++j)
                {
                    for (int d = 0; d < 4; ++d)
                    {
                        if (densePropagator[i][d][j])
                        {
                            // TODO reduce entries as there are very many very quickly
                            propagator[i][d].Add(j); 
                        }
                    }
                }
            }

            return propagator;
        }

        /* Get probability of presence of tiles. */
        static double[] GetTileWeights(WFC_2DTile<T>[] tiles)
        {
            List<double> frequencies = new List<double>(tiles.Length);
            for (int i = 0; i < tiles.Length; ++i)
            {
                for (int j = 0; j < tiles[i].orientations.Length; ++j)
                {
                    frequencies.Add(tiles[i].weight / tiles[i].orientations.Length);
                }
            }

            return frequencies.ToArray();
        }

        void SetTile(int tileId, int y, int x)
        {
            for (int p = 0; p < _idToOrientedTile.Length; p++)
            {
                if (tileId != p)
                {
                    RemoveWavePattern(y, x, p);
                }
            }
        }

        /*
         Set the tile at a specific position.
         Returns false if the given tile and orientation does not exist,
         or if the coordinates are not in the wave
        */
        bool SetTile(int tileId, int orientation, int y, int x)
        {
            if (tileId >= _orientedTileIds.Length || orientation >= _orientedTileIds[tileId].Length ||
                y >= waveHeight ||
                x >= waveWidth)
            {
                return false;
            }

            int orientedTileID = _orientedTileIds[tileId][orientation];
            SetTile(orientedTileID, y, x);
            return true;
        }

        /* Run the tiling wfc and return the result if the algorithm succeeded */
        public (bool, T[,]) Run()
        {
            (bool Success, int[,] Result) a = Run_Internal();
            if (a.Success == false)
            {
                return (false, null);
            }

            return (true, _tiles[0].ResultToOutput(a.Result, _tiles));
        }
    }
}