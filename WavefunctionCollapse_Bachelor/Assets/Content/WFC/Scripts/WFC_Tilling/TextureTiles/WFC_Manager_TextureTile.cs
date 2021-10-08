#if UNITY_EDITOR
using UnityEditor;
#endif
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.WSA;

namespace WFC.Tiling
{
    [ExecuteInEditMode]
    public class WFC_Manager : MonoBehaviour
    {
        public WFC_Texture2DTile[] tiles;

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
            Dictionary<WFC_Texture2DTile, int> tile_ids = new Dictionary<WFC_Texture2DTile, int>(tiles.Length);
            int tileIndex = 1;
            foreach (var tile in tiles)
            {
                tile_ids.Add(tile, tileIndex++);
            }

            foreach (var tile in tiles)
            {
                foreach (var neighbour in tile.neighbours)
                {

                }

            }
        }
    }
}