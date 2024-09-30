using UnityEngine;

[ExecuteAlways]
public class KillZeroSpeedParticles : MonoBehaviour
{
    ParticleSystem ps;
    ParticleSystem.Particle[] particles;

    void Update() {
        ps = GetComponent<ParticleSystem>();
        if(particles == null || particles.Length != ps.main.maxParticles)
            particles = new ParticleSystem.Particle[ps.main.maxParticles];
        
        int particleCount = ps.GetParticles(particles);
        
        for (int i = 0; i < particleCount; i++)
        {
            if (particles[i].velocity == Vector3.zero)
            {
                particles[i].remainingLifetime = 0; // Set remaining lifetime to zero to kill it
            }
        }

        // Apply the particle changes back to the Particle System
        ps.SetParticles(particles, particleCount);
    }
}
/*
using UnityEngine;
using Unity.Jobs;
using Unity.Collections;

public class KillZeroSpeedParticles : MonoBehaviour
{
    private ParticleSystem ps;
    private ParticleSystem.Particle[] particles;

    void Start()
    {
        ps = GetComponent<ParticleSystem>();
        particles = new ParticleSystem.Particle[ps.main.maxParticles];
    }

    void LateUpdate()
    {
        int particleCount = ps.GetParticles(particles);

        NativeArray<ParticleSystem.Particle> nativeParticles =
            new NativeArray<ParticleSystem.Particle>(particles, Allocator.TempJob);

        var job = new KillParticlesJob
        {
            particles = nativeParticles
        };

        JobHandle handle = job.Schedule(particleCount, 64);
        handle.Complete();

        nativeParticles.CopyTo(particles);
        ps.SetParticles(particles, particleCount);

        nativeParticles.Dispose();
    }

    struct KillParticlesJob : IJobParallelFor
    {
        public NativeArray<ParticleSystem.Particle> particles;

        public void Execute(int index)
        {
            ParticleSystem.Particle particle = particles[index];
            if (particle.velocity == Vector3.zero)
            {
                particle.remainingLifetime = 0;
                particles[index] = particle;
            }
        }
    }
}
*/