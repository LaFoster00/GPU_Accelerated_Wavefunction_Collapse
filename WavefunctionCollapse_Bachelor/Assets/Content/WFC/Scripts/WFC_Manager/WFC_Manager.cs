#if UNITY_EDITOR
using UnityEditor;
#endif
using UnityEngine;

[ExecuteInEditMode]
public class WFC_Manager : MonoBehaviour
{
    public WFC_Input_Tileset[] inputs;
    
    private void OnEnable()
    {
#if UNITY_EDITOR
        AssetDatabase.importPackageCompleted += OnImportPackageCompleted;
#endif
    }

    private void OnDisable()
    {
#if UNITY_EDITOR
        AssetDatabase.importPackageCompleted -= OnImportPackageCompleted;
#endif
    }

    private void OnImportPackageCompleted(string packagename)
    {
        print("HelloWorld");
        string[] inputBitmaps = AssetDatabase.FindAssets("t:WFC_Input_Bitmap");
        inputs = new WFC_Input_Tileset[inputBitmaps.Length];
        int index = 0;
        foreach (var inputBitmap in inputBitmaps)
        {
            inputs[index] = AssetDatabase.LoadAssetAtPath<WFC_Input_Tileset>(AssetDatabase.GUIDToAssetPath(inputBitmap));
            index++;
        }
        
    }
}
