using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public enum Symmetry2D
{
    L,
    I,
    X,
    T,
    Diag, // : \
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
public struct Neighbour<T>
{
    // The actual neighbouring tile
    public WFC_2DTile<T> neighbour;
    
    // The index of orientation of the tile (see WFC_2DTile<>.orientations)
    public int neighbourOrientation;
}

public abstract class WFC_2DTile<T> : ScriptableObject
 {
     /* name of the tile */
    public string name;
    
    /* The actual content of the tile (Texture, Mesh, etc.) */
    public T tileContent;
    
    /* What symmetry axes does the content have */
    public Symmetry2D symmetry;
    
    /* Its weight on the distribution of presence of tiles */
    public double weight;
    
    /* List of neighbours. These include information about the orientation of the tile */
    public List<Neighbour<T>> neighbours = new List<Neighbour<T>>();
    
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
}
