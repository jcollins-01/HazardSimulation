using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Ignis
{
    [CreateAssetMenu(fileName = "NewOAVAShaderCompatibility", menuName = "OAVA/OAVAShaderCompatibility", order = 1)]
    public class OAVAShaderCompatibilitySO : ScriptableObject
    {

        [Tooltip("System will check this property from the shader to confirm it is a right type of shader")]
        public string ShaderCheckProperty = "";

        [Tooltip("If ShaderCheckProperty is left empty, the system can compare this string to check if it is a right shader")]
        public string ShaderName = "";

        [Tooltip("Color to animate while burning")]
        public string ShaderMainColorPropertyName = "_MainColor";

        [Tooltip("Emission Color to animate while burning (if material has this)")]
        public string ShaderEmissionColorPropertyName = "";

        [System.Serializable]
        public class ShaderProperty
        {
            [Tooltip("Property name")]
            public string name = "";

            [Tooltip("Target value of the property")]
            public float targetValue = 0;

            [Tooltip("How fast it is animated to change. This is updated with Time.deltaTime")]
            public float speedMultiplier = 1;
        }

        [Tooltip("List of keywords to enable when the fire starts (e.g. Enable emission)")]
        public List<string> onFireStartEnableKeywords = new List<string>();

        [Tooltip("List of flags to enable when the fire starts (e.g. Enable emission)")]
        public List<MaterialGlobalIlluminationFlags> onFireStartEnableIlluminationFlag = new List<MaterialGlobalIlluminationFlags>();

        [Tooltip("List of properties to animate while burning and their target values")]
        public List<ShaderProperty> onFireChangeProperties = new List<ShaderProperty>();

        [Tooltip("List of properties to animate after burnout has started (like stopping the wind affecting)")]
        public List<ShaderProperty> onBurnoutChangeProperties = new List<ShaderProperty>();

        [Tooltip("List of properties to animate when fire touches it (e.g. snow melting on rocks)")]
        public List<ShaderProperty> onTouchChangeProperties = new List<ShaderProperty>();
    }

}
