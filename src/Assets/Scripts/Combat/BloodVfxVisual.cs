using UnityEngine;

public static class BloodVfxVisual
{
    public static GameObject Spawn(
        GameObject prefab,
        Vector3 position,
        Quaternion rotation,
        float scale,
        float lifetime,
        uint renderingLayerMask)
    {
        if (prefab == null)
            return null;

        GameObject instance = Object.Instantiate(prefab, position, rotation);
        // Detached effects must receive the same room lighting as the struck monster.
        foreach (Renderer renderer in instance.GetComponentsInChildren<Renderer>(true))
            renderer.renderingLayerMask = renderingLayerMask;

        ParticleSystem[] particles = instance.GetComponentsInChildren<ParticleSystem>(true);
        foreach (ParticleSystem particle in particles)
            particle.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);

        instance.transform.localScale = prefab.transform.localScale * Mathf.Max(0.01f, scale);
        float cleanupDelay = Mathf.Max(0.1f, lifetime);
        foreach (ParticleSystem particle in particles)
        {
            var main = particle.main;
            // The pack mixes Local and Hierarchy scaling. Apply the monster's size
            // consistently without replacing its authored colors or gradients.
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            cleanupDelay = Mathf.Max(cleanupDelay,
                main.startDelay.constantMax + main.duration + main.startLifetime.constantMax);
            if (particle.gameObject.activeInHierarchy)
                particle.Play(false);
        }

        Object.Destroy(instance, cleanupDelay);
        return instance;
    }
}
