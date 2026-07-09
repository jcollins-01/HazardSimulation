using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEditor;

namespace Ignis
{
    /// <summary>
    /// Adds the given define symbols to PlayerSettings define symbols.
    /// Just add your own define symbols to the Symbols property at the below.
    /// </summary>
    [InitializeOnLoad]
    public class GenerateOAVAFlameSymbols : Editor
    {

        /// <summary>
        /// Symbols that will be added to the editor
        /// </summary>
        public static readonly string[] Symbols = new string[] {
        "OAVA_IGNIS"
    };

        /// <summary>
        /// Add define symbols as soon as Unity gets done compiling.
        /// </summary>
        static GenerateOAVAFlameSymbols()
        {
            string definesString = PlayerSettings.GetScriptingDefineSymbolsForGroup(EditorUserBuildSettings.selectedBuildTargetGroup);
            List<string> allDefines = definesString.Split(';').ToList();

            try
            {
                if (!allDefines.Contains(Symbols[0]))
                    IgnisMenu.OpenIgnisMenu();
            } 
            catch (System.Exception e)
            {
                Debug.Log("Welcome window could not be shown. Please read README.txt for Ignis install instructions. Error: " + e);
            }
            

            allDefines.AddRange(Symbols.Except(allDefines));
            PlayerSettings.SetScriptingDefineSymbolsForGroup(
                EditorUserBuildSettings.selectedBuildTargetGroup,
                string.Join(";", allDefines.ToArray()));
        }

    }
}
