using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Ignis
{
    public class FlameLightFlicker : MonoBehaviour
    {
        [Tooltip("How much should the light flicker? This is a multiplier")]
        [Range(0, 1)]
        public float flickerMultiplier = 0.3f;

        [Tooltip("How fast should the light flicker?")]
        public float flickerSpeed = 5;

        [Tooltip("Smoothing when the light is enabled")]
        public float achieveMaxIntensityTime = 2;

        [Tooltip("If you use HDRP and encounter a flash when the light is enabled: disable this and set light intensity to 0")]
        public bool getIntensityFromLight = true;

        [Tooltip("This will be ignored if getIntensityFromLight true")]
        public float targetIntensity = 1;

        Light flickerLight;
        float originalIntensity = 0;
        float currentIntensityWithoutNoise = 0;
        bool disableSlowly = false;
        float enabledTime = 0;

        void Awake()
        {
            flickerLight = GetComponent<Light>();
            if (getIntensityFromLight)
                originalIntensity = flickerLight.intensity;
            else
                originalIntensity = targetIntensity;
            flickerLight.intensity = 0;
        }


        private void OnEnable()
        {
            flickerLight.intensity = 0;
            enabledTime = 0;
        }

        public void SlowDisable()
        {
            disableSlowly = true;
        }

        // Update is called once per frame
        void Update()
        {
            enabledTime += Time.deltaTime;
            if (!disableSlowly)
            {
                currentIntensityWithoutNoise = Mathf.Lerp(0, originalIntensity, enabledTime / achieveMaxIntensityTime);
                flickerLight.intensity = currentIntensityWithoutNoise * ((1 - flickerMultiplier) + Mathf.PerlinNoise(enabledTime * flickerSpeed, 0) * (flickerMultiplier * 2));
            }
            else
            {
                currentIntensityWithoutNoise = Mathf.MoveTowards(currentIntensityWithoutNoise, 0, (1 / achieveMaxIntensityTime) * Time.deltaTime * currentIntensityWithoutNoise);
                flickerLight.intensity = currentIntensityWithoutNoise * ((1 - flickerMultiplier) + Mathf.PerlinNoise(enabledTime * flickerSpeed, 0) * (flickerMultiplier * 2));
                if (flickerLight.intensity <= 0.01f)
                {
                    flickerLight.gameObject.SetActive(false);
                }
            }



        }
    }

}

