using System;
using UnityEngine;

public enum Rotation
{
    R0,
    R90,
    R180,
    R270,
}

public abstract class WFC_2DTile<T> : ScriptableObject
{
    public string name;
    public T tileContent;
    public Symmetry2D symmetry;

    /*
     First index is reflection-index.
     Second index is rotation-index. Can be retrieved from Rotation via Rotation.ToIndex()
     Holds reference to the rotated output asset which will can be used for preview and later resolve
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
