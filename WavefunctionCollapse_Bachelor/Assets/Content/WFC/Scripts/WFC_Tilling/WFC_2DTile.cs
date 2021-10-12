using System;
using System.Collections.Generic;
using UnityEngine;

namespace WFC.Tiling
{
    [Serializable]
    public enum Symmetry2D
    {
        X,
        I,
        Diag, // : \
        T,
        L,
        F, // No symmetries, all transformations needed.
    }


    [Serializable]
    public enum Rotation
    {
        R0,
        R90,
        R180,
        R270,
    }

/* Helper struct for keeping track of tile neighbours and their orientation */
    [Serializable]
    public class TileNeighbour<T> : Neighbour<T>
    {
        // The tile describing its neighbour
        public WFC_2DTile<T> leftTile;

        // The index of orientation of the base tile (see WFC_2DTile<>.orientations)
        public int leftTileOrientation;

        // The actual neighbouring tile
        public WFC_2DTile<T> rightTile;

        // The index of orientation of the tile (see WFC_2DTile<>.orientations)
        public int rightTileOrientation;

        public Neighbour<T>.AddToPropagator Add =>
            (neighbour, actionMaps, orientedTileIds, propagator, action, direction) =>
            {
                TileNeighbour<T> tileNeighbour = (TileNeighbour<T>) neighbour;

                int leftActionOrientation =
                    actionMaps[tileNeighbour.leftTile.symmetry.ToIndex()][action, tileNeighbour.leftTileOrientation];
                int leftOrientedTileId = orientedTileIds[tileNeighbour.leftTile.tileId][leftActionOrientation];

                int rightActionOrientation =
                    actionMaps[tileNeighbour.rightTile.symmetry.ToIndex()][action, tileNeighbour.rightTileOrientation];
                int rightOrientedTileId =
                    orientedTileIds[tileNeighbour.rightTile.tileId][rightActionOrientation];

                propagator[leftOrientedTileId][(int) direction][rightOrientedTileId] = true;
                direction = Directions.GetOppositeDirection(direction);
                propagator[rightOrientedTileId][(int) direction][leftOrientedTileId] = true;
            };
    }

    public abstract class WFC_2DTile<T> : ScriptableObject
    {
        /* name of the tile */
        public string tileName;

        /* The actual content of the tile (Texture, Mesh, etc.) */
        public T tileContent;

        /* What symmetry axes does the content have */
        public Symmetry2D symmetry = Symmetry2D.X;

        /* Its weight on the distribution of presence of tiles */
        public double weight = 1;

        /* List of neighbours. These include information about the orientation of the tile */
        public List<TileNeighbour<T>> neighbours = new List<TileNeighbour<T>>();

        [NonSerialized] public int tileId;

        /*
         Hold all possible orientations of tile content.
         0, 1, 2 and 3 are 0°, 90°, 180° and 270° anticlockwise rotations.
         4, 5, 6 and 7 are indexes 0, 1, 2 and 3 preceded by a reflection on
         the x axis.
        */
        public T[] orientations;

        /* Should return the content rotated 90 degrees counter-clockwise  */
        protected abstract Func<T, T> GetRotatedContent { get; }

        /* Should return the content mirrored on the x axis */
        protected abstract Func<T, T> GetMirroredContent { get; }

        /* Generates all the possible content orientations for the input tile. */
        public void GenerateOrientations()
        {
            int nbOfPossibleOrientations = WFC_TilessetUtils.NbOfPossibleOrientations(symmetry);
            orientations = new T[nbOfPossibleOrientations];
            orientations[0] = tileContent;
            // Create all needed rotations and reflections for the given symmetry
            // A higher degree of symmetry means less transformations needed since the sides are more and more the same
            switch (symmetry)
            {
                case Symmetry2D.I:
                case Symmetry2D.Diag:
                    orientations[1] = GetRotatedContent(orientations[0]);
                    break;
                case Symmetry2D.T:
                case Symmetry2D.L:
                    orientations[1] = GetRotatedContent(orientations[0]);
                    orientations[2] = GetRotatedContent(orientations[1]);
                    orientations[3] = GetRotatedContent(orientations[2]);
                    break;
                case Symmetry2D.F:
                    orientations[1] = GetRotatedContent(orientations[0]);
                    orientations[2] = GetRotatedContent(orientations[1]);
                    orientations[3] = GetRotatedContent(orientations[2]);
                    orientations[4] = GetMirroredContent(orientations[0]);
                    orientations[5] = GetMirroredContent(orientations[1]);
                    orientations[6] = GetMirroredContent(orientations[2]);
                    orientations[7] = GetMirroredContent(orientations[3]);
                    break;
                case Symmetry2D.X:
                default:
                    break;
            }
        }

        /* Translate the generic WFC result into the image result */
        public abstract T[,] ResultToOutput(int[,] wave, WFC_2DTile<T>[] tiles, (int, int)[] orientedToTileId);
    }
}