using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Ignis
{
    /// <summary>
    /// Ignis window menu items
    /// </summary>
    class IgnisMenu : EditorWindow
    {
        public Texture tex;

        [MenuItem("Window/OAVA/Ignis/Help")]
        public static void OpenIgnisMenu()
        {
            // Get existing open window or if none, make a new one:
            IgnisMenu window = (IgnisMenu)EditorWindow.GetWindow(typeof(IgnisMenu));
            window.minSize = new Vector2(350, 450);
            window.maxSize = new Vector2(350, 450);

            window.Show();
        }

        [MenuItem("Window/OAVA/Ignis/Write a Review")]
        static void OpenReviewWrite()
        {
            Application.OpenURL("https://assetstore.unity.com/packages/tools/particles-effects/ignis-interactive-fire-system-181079#reviews");
        }

        void OnGUI()
        {
            GUILayout.Space(20);
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (!tex)
                tex = (Texture2D)Resources.Load("/OAVA-Flame/Images/Ignis_logo");

            GUILayout.Box(tex);
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            GUILayout.Label("Welcome and thank you for choosing Ignis!", EditorStyles.largeLabel);
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            GUILayout.Label("First-Time Setup", EditorStyles.largeLabel);
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            GUILayout.Space(10);
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            GUIStyle style = new GUIStyle(GUI.skin.label);
            style.alignment = TextAnchor.MiddleCenter;
            style.wordWrap = true;

            GUILayout.Label("Please read setup guide in README or in User Instructions PDF. Ignis will not work if you have not followed the instructions.", style);
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            GUILayout.FlexibleSpace();
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            GUILayout.Label("Documentation", EditorStyles.largeLabel);
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
            GUILayout.Space(5);
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            GUI.backgroundColor = Color.white;
            if (GUILayout.Button("README"))
            {
                Application.OpenURL(Application.dataPath + "/OAVA-Flame/README.txt");
            }
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
            GUILayout.Space(5);
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("User Instructions and FAQ PDF"))
            {
                Application.OpenURL(Application.dataPath + "/OAVA-Flame/Documentation/User_Instructions.pdf");
            }
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
            GUILayout.Space(5);
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("API Documentation PDF"))
            {
                Application.OpenURL(Application.dataPath + "/OAVA-Flame/Documentation/Ignis_API-Documentation.pdf");
            }
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            GUILayout.FlexibleSpace();
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("support@oava.mostlygoodblog.com");
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Close"))
            {
                this.Close();
            }
            EditorGUILayout.EndHorizontal();
        }

    }
}
