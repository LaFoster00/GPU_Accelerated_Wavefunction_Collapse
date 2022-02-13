using UnityEngine;
using WFC.Tiling;

public class WFC_TextureTileTester : MonoBehaviour
{
    public WFC_Texture2DTile[] tiles;

    private void Start()
    {
        foreach (var tile in tiles)
        {
            tile.GenerateOrientations();
        }
    }

    private void OnGUI()
    {
        foreach (var tile in tiles)
        {
            int i = 0;
            foreach (var orientation in tile.orientations)
            {
                GUI.DrawTexture(Rect.MinMaxRect(128 * i, 0, 128 + 128 * i, 128), orientation);
                i++;
            }
        }
    }
}
