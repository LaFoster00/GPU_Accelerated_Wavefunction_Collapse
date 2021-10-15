using System;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using WFC.Tiling;

[CustomPropertyDrawer(typeof(TileNeighbour<Texture2D>))]
public class Texture2DTileNeighbour_PropertyDrawer : PropertyDrawer
{
    private static GUIStyle s_TempStyle = new GUIStyle();
    
    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        EditorGUI.BeginProperty(position, label, property);

        bool active = EditorGUI.Toggle(
            new Rect(
                position.center.x - EditorGUIUtility.singleLineHeight,
                position.yMin,
                EditorGUIUtility.singleLineHeight, 
                EditorGUIUtility.singleLineHeight),
            property.FindPropertyRelative("active").boolValue);
        property.FindPropertyRelative("active").SetValue(active);
        
        Rect leftTilePos = new Rect(position.x, position.y, position.width / 2, EditorGUIUtility.singleLineHeight);

        var leftTile = EditorGUI.ObjectField(new Rect(leftTilePos.x, leftTilePos.y + EditorGUIUtility.singleLineHeight, 
                leftTilePos.width / 2, leftTilePos.height), 
            property.FindPropertyRelative("leftTile").objectReferenceValue, 
            typeof(WFC_Texture2DTile), false);
        property.FindPropertyRelative("leftTile").SetValue(leftTile);
        
        var leftTileCast = (WFC_Texture2DTile) leftTile;
        if (leftTileCast)
        {
            foreach (var orientation in leftTileCast.orientations)
            {
                if (!orientation)
                {
                    leftTileCast.GenerateOrientations();
                    break;
                }
            }
            if (leftTileCast.orientations.Length == 0) leftTileCast.GenerateOrientations();

            if (GUI.Button(new Rect(leftTilePos.x, leftTilePos.y, leftTilePos.width / 2, leftTilePos.height),
                "Rotate Right"))
            {
                property.FindPropertyRelative("leftTileOrientation")
                    .SetValue((property.FindPropertyRelative("leftTileOrientation").intValue + 1) %
                              leftTileCast.orientations.Length);
            }
            else
            {
                property.FindPropertyRelative("leftTileOrientation")
                    .SetValue((property.FindPropertyRelative("leftTileOrientation").intValue) %
                              leftTileCast.orientations.Length);
            }

            EditorGUI.LabelField(
                new Rect(
                    leftTilePos.x + leftTilePos.width / 2 + 12,
                    leftTilePos.y,
                    leftTilePos.width,
                    leftTilePos.height),
                property.FindPropertyRelative("leftTileOrientation").intValue.ToString());

        }

        Rect rightTilePos = new Rect(position.x + (position.width / 2), position.y, position.width / 2, EditorGUIUtility.singleLineHeight);
        
        var rightTile = EditorGUI.ObjectField(new Rect(rightTilePos.x, rightTilePos.y + EditorGUIUtility.singleLineHeight, 
                rightTilePos.width / 2, rightTilePos.height), 
            property.FindPropertyRelative("rightTile").objectReferenceValue, 
            typeof(WFC_Texture2DTile), false);
        property.FindPropertyRelative("rightTile").SetValue(rightTile);
        
        var rightTileCast = (WFC_Texture2DTile) rightTile;
        if (rightTileCast)
        {
            foreach (var orientation in rightTileCast.orientations)
            {
                if (!orientation)
                {
                    rightTileCast.GenerateOrientations();
                    break;
                }
            }
            if (rightTileCast.orientations.Length == 0) rightTileCast.GenerateOrientations();
            
            if (GUI.Button(new Rect(rightTilePos.x, rightTilePos.y, rightTilePos.width / 2, rightTilePos.height),
                "Rotate Right"))
            {
                property.FindPropertyRelative("rightTileOrientation")
                    .SetValue((property.FindPropertyRelative("rightTileOrientation").intValue + 1) %
                              rightTileCast.orientations.Length);
            }
            else
            {
                property.FindPropertyRelative("rightTileOrientation")
                    .SetValue((property.FindPropertyRelative("rightTileOrientation").intValue) %
                              rightTileCast.orientations.Length);
            }

            EditorGUI.LabelField(
                new Rect(
                    rightTilePos.x + rightTilePos.width / 2 + 12,
                    rightTilePos.y,
                    rightTilePos.width,
                    rightTilePos.height),
                property.FindPropertyRelative("rightTileOrientation").intValue.ToString());
        }

        //if this is not a repaint or the property is null exit now
        if (Event.current.type != EventType.Repaint || property.serializedObject.targetObject == null)
            return;

        if (leftTileCast)
        {
            leftTilePos.y += EditorGUIUtility.singleLineHeight * 2 + 4;
            leftTilePos.width = 128;
            leftTilePos.height = 128;
            s_TempStyle.normal.background =
                leftTileCast.orientations[property.FindPropertyRelative("leftTileOrientation").intValue];
            s_TempStyle.Draw(leftTilePos, GUIContent.none, false, false, false, false);
        }

        if (rightTileCast)
        {
            rightTilePos.y += EditorGUIUtility.singleLineHeight * 2 + 4;
            rightTilePos.width = 128;
            rightTilePos.height = 128;
            s_TempStyle.normal.background =
                rightTileCast.orientations[property.FindPropertyRelative("rightTileOrientation").intValue];
            s_TempStyle.Draw(rightTilePos, GUIContent.none, false, false, false, false);
        }

        EditorGUI.EndProperty();
    }

    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        return EditorGUIUtility.singleLineHeight * 10;
    }
}
