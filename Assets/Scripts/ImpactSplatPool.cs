using UnityEngine;

// Short (sub-second) Paint Impact Splat shown at every Spray hit point -
// distinct from PaintDecalPool's PERMANENT ground/wall marks. Pre-builds a
// fixed pool of ParticleSystems at Awake and reuses them round-robin, same
// pattern as PaintDecalPool: no runtime Instantiate/Destroy.
public class ImpactSplatPool : MonoBehaviour
{
    [SerializeField] private Material splatMaterial;
    [SerializeField] private int poolSize = 12;

    [SerializeField] private float lifetime = 0.25f;
    [SerializeField] private float startSize = 0.3f;
    [SerializeField] private int burstCount = 6;

    private ParticleSystem[] pool;
    private int nextIndex;

    void Awake()
    {
        pool = new ParticleSystem[poolSize];

        for (int i = 0; i < poolSize; i++)
        {
            var go = new GameObject("ImpactSplat_" + i);
            // Inactive before AddComponent: ParticleSystem.playOnAwake
            // defaults to true, and OnEnable (which would start it playing)
            // only fires on an active GameObject. Configuring main/emission
            // while inactive avoids "Setting the duration while system is
            // still playing is not supported", then we activate at the end
            // with playOnAwake already false.
            go.SetActive(false);
            go.transform.SetParent(transform, false);

            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.duration = lifetime;
            main.loop = false;
            main.playOnAwake = false;
            main.startLifetime = lifetime;
            main.startSpeed = 0.3f;
            main.startSize = new ParticleSystem.MinMaxCurve(startSize * 0.7f, startSize * 1.2f);
            main.startColor = Color.white;
            main.maxParticles = burstCount * 2;
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            var emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)burstCount) });

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 20f;
            shape.radius = 0.02f;

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            if (splatMaterial != null) renderer.sharedMaterial = splatMaterial;

            pool[i] = ps;
            go.SetActive(true);
        }
    }

    public void PlayImpact(Vector3 point, Vector3 normal)
    {
        if (pool == null || pool.Length == 0) return;

        var ps = pool[nextIndex];
        nextIndex = (nextIndex + 1) % pool.Length;

        // Defensive: under normal fire-rate limits a full pool cycle always
        // outlasts one splat's lifetime, so this shouldn't trigger - but
        // guard it anyway rather than risk "set duration while playing".
        if (ps.isPlaying) ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var t = ps.transform;
        t.position = point;
        t.rotation = Quaternion.LookRotation(normal);

        ps.Play();
    }
}
