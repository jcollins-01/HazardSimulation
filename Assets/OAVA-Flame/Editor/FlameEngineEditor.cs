using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Ignis
{
    [CustomEditor(typeof(FlameEngine))]
    public class FlameEngineEditor : Editor
    {
        public Texture logo;
        private FlameEngine eng;

        private void OnEnable()
        {
            eng = (FlameEngine)target;
        }
        public override void OnInspectorGUI()
        {
            GUILayout.Box(logo);
            DrawDefaultInspector();
            EditorGUILayout.HelpBox("Culling distance is calculated from a camera with the MainCamera tag", MessageType.Info);
            EditorGUILayout.Space(20);

            if (eng.UnityTerrainCompatible)
            {
                if (!eng.transform.GetComponent<IgnisUnityTerrain>())
                {
                    eng.gameObject.AddComponent<IgnisUnityTerrain>();
                }
            } 
            else
            {
                if(eng.transform.GetComponent<IgnisUnityTerrain>())
                {
                    DestroyImmediate(eng.gameObject.GetComponent<IgnisUnityTerrain>());
                }
            }

            if (!eng.pause)
            {

                if (GUILayout.Button("Pause flames"))
                {
                    eng.PauseFlames();
                }
            }
            else
            {
                if (GUILayout.Button("Resume flames"))
                {
                    eng.ResumeFlames();
                }
            }
        }
    }
}
