using UnityEngine;


[CreateAssetMenu(menuName = "WFC/Input/Bitmap", fileName = "New_WFCBitmap")]
public class WFC_Input_Tileset : ScriptableObject
{
    public string name = "Input";
    public WFC_2DTileOld<Texture2D>[] tiles;
    public bool periodic = false;
    public int width = 48;
    public int height = 48;
}
