using System.Collections;
using System.Collections.Generic;

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
    public class TilingWFC<T>
    {
        /* The distinct tiles. */
        protected WFC_2DTile<T>[] tiles;

        /* Map ids of oriented tiles to tile and orientation. */
        protected (int, int)[] orientedToTileId;

        /* Map tile and orientation to oriented tile id. */
        protected int[][] orientedTileIds;

        protected Model model;
        
        public TilingWFC(Model solver, WFC_2DTile<T>[] tiles, Neighbour<T>[] neighbours, Model.PropagatorSettings propagatorSettings)
        {
            model = solver;
            this.tiles = tiles;
            (orientedToTileId, orientedTileIds) = GenerateOrientedTileIds(tiles);
            propagatorSettings.orientedToTileId = orientedToTileId;
            solver.SetData(
                orientedToTileId.Length,
                GetTileWeights(tiles),
                GeneratePropagator(neighbours, tiles, orientedToTileId, orientedTileIds),
                propagatorSettings);
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
         The propagator holds information on which tile can lie in which direction of all the oriented tiles.
         Bool Array holds the dense propagator, int array the standard one.
        */
        protected static (bool[][][], int[][][]) GeneratePropagator(
            Neighbour<T>[] neighbors,
            WFC_2DTile<T>[] tiles,
            (int, int)[] orientedToTileId,
            int[][] orientedTileIds)
        {
            int nbOrientedTiles = orientedToTileId.Length;
            /* Create #nbOrientedTiles arrays filled with 4 arrays filled with #nbOrientedTiles false values */
            bool[][][] densePropagator = new bool[nbOrientedTiles][][];
            for (int pattern = 0; pattern < nbOrientedTiles; pattern++)
            {
                densePropagator[pattern] = new bool[4][];
                for (var dir = 0; dir < 4; dir++)
                {
                    densePropagator[pattern][dir] = new bool[nbOrientedTiles];
                }
            }

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
            int[][][] propagator = new int[nbOrientedTiles][][];
            for (int tile = 0; tile < nbOrientedTiles; tile++)
            {
                propagator[tile] = new int[4][];
                for (int dir = 0; dir < 4; dir++)
                {
                    var values = new List<int>();
                    for (int neighbour = 0; neighbour < nbOrientedTiles; neighbour++)
                    {
                        if (densePropagator[tile][dir][neighbour])
                        {
                            values.Add(neighbour);
                        }
                    }

                    propagator[tile][dir] = values.ToArray();
                }
            }

            return (densePropagator, propagator);
        }

        /* Get probability of presence of tiles. */
        protected static double[] GetTileWeights(WFC_2DTile<T>[] tiles)
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

        protected virtual void CollapseNodeToPattern( int y, int x, int pattern)
        {
            for (int p = 0; p < orientedToTileId.Length; p++)
            {
                if (pattern != p)
                {
                    model.Ban(x + y * model.width, p);
                }
            }
        }

        /*
         Set the tile at a specific position.
         Returns false if the given tile and orientation does not exist,
         or if the coordinates are not in the wave
        */
        protected bool CollapseNodeToPattern(int y, int x, int pattern, int orientation)
        {
            if (pattern >= orientedTileIds.Length || orientation >= orientedTileIds[pattern].Length ||
                y >= model.height ||
                x >= model.width)
            {
                return false;
            }

            int orientedTileID = orientedTileIds[pattern][orientation];
            CollapseNodeToPattern(y, x, orientedTileID);
            return true;
        }

        public class WFC_TypedResult
        {
            public bool success;
            public T[,] result;
        }
        
        /* Run the tiling wfc and return the result if the algorithm succeeded */
        public IEnumerator Run(uint seed, int limit, WFC_TypedResult returnValue)
        {
            Model.WFC_Result result = new Model.WFC_Result();
            yield return model.Run(seed, limit, result);
            if (result.success == false)
            {
                returnValue.success = false;
                returnValue.result = default;
                yield break;
            }

            returnValue.success = true;
            returnValue.result = tiles[0].ResultToOutput(result.output, tiles, orientedToTileId);
        }
    }
}