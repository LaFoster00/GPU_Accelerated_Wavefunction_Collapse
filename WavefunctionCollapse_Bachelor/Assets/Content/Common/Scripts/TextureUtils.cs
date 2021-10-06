using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public static class TextureUtils 
{
    /* Mirrors a texture along the x axis */
    public static Texture2D Mirrored(this Texture2D original)
    {
        int width = original.width;
        int height = original.height;
        Color[] pixels = new Color[width * height];
        
        int xN = width;
        int yN = height;

        for (int x = width - 1; x >= 0; x--)
        {
            for (int y = 0; y < height; y++)
            {
                pixels[x + y * width] = original.GetPixel(width - 1 - x, y, 0);
            }
        }
        
        Texture2D mirrored = new Texture2D(original.width, original.height);
        mirrored.SetPixels(pixels);
        mirrored.Apply();
         
        return mirrored;
    }
    
    /* Rotates a texture 90 degrees counter clockwise */
    public static Texture2D Rotated(this Texture2D original)
    {
        Color[,] pixels = original.GetPixels().Make2DArray(original.height, original.width);
        int height = original.height;
        int width = original.width;
        Color[,] rotatedPixels = new Color[width, height];
        for (int y = 0; y < width; y++)
        {
            for (int x = 0; x < height; x++)
            {
                rotatedPixels[y, x] = pixels[x, width - 1 - y];
            }
        }

        Texture2D result = new Texture2D(original.width, original.height);
        result.SetPixels(rotatedPixels.Cast<Color>().ToArray());
        result.Apply();
        return result;
    }
}
