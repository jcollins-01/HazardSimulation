using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Ignis
{
    public class WindRetrieve : MonoBehaviour
    {
        ParticleSystem particlesSystem;
        ParticleSystem.Particle[] particles;
        Vector3 currentWindVelocity = Vector3.zero;
        bool useTVEWind = false;
        bool onUse = true;


        void Start()
        {
            Vector2 tve = Shader.GetGlobalVector("TVE_NoiseSpeed_Vegetation");
            WindZone zone = FindObjectOfType<WindZone>();

            particlesSystem = gameObject.GetComponent<ParticleSystem>();
            if (particlesSystem && zone)
            {
                if (zone)
                {
                    particles = new ParticleSystem.Particle[1];
                    SetupParticleSystem();
                }
            }
            else if (tve != null)
            {
                useTVEWind = true;
                if (particlesSystem)
                    Destroy(particlesSystem);
            }
            else
            {
                onUse = false;
            }

        }

        void Update()
        {
            if (useTVEWind)
            {
                Vector2 wind = Shader.GetGlobalVector("TVE_NoiseSpeed_Vegetation");
                Vector4 multiplier = Shader.GetGlobalVector("TVE_MotionParams");
                currentWindVelocity = new Vector3(-wind.x * multiplier.z * 10, 0, -wind.y * multiplier.z * 10);
            }
            else if (particlesSystem)
            {
                particlesSystem.GetParticles(particles);

                currentWindVelocity = particles[0].velocity;
                particles[0].position = Vector3.zero;
                particles[0].velocity = Vector3.zero;

                particlesSystem.SetParticles(particles, 1);
            }


        }

        public bool OnUse()
        {
            return onUse;
        }

        public Vector3 GetCurrentWindVelocity()
        {
            return currentWindVelocity;
        }

        void SetupParticleSystem()
        {
            var main = particlesSystem.main;
            main.startLifetime = Mathf.Infinity;
            main.startSpeed = 0;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 1;

            var emission = particlesSystem.emission;
            emission.rateOverTime = 0;

            //the below is to start the particle at the center
            particlesSystem.Emit(1);
            particlesSystem.GetParticles(particles);
            particles[0].position = Vector3.zero;
            particlesSystem.SetParticles(particles, 1);
        }
    }
}
