using System;
using UnityEngine;

[CreateAssetMenu(menuName = "WFC/Input/Texture2DTile", fileName = "New Texture2DTile")]
public class WFC_Texture2DTile : WFC_2DTile<Texture2D>
{
    protected override Func<Texture2D, Texture2D> GetRotatedContent
    {
        get
        {
            return texture2D => texture2D.Rotated();
        }
    }

    protected override Func<Texture2D, Texture2D> GetMirroredContent
    {
        get
        {
            return texture2D => texture2D.Mirrored();
        }
    }
}