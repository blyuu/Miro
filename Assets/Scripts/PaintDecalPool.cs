using UnityEngine;
using UnityEngine.Rendering.Universal;

public class PaintDecalPool : MonoBehaviour
{
    [SerializeField] private Material decalMaterial;
    [SerializeField] private int poolSize = 120;
    [SerializeField] private float minDecalSize = 0.28f;
    [SerializeField] private float maxDecalSize = 0.42f;
    [SerializeField] private float decalDepth = 0.25f;
    [SerializeField] private float surfaceOffset = 0.02f;

    private DecalProjector[] pool;
    private int nextIndex;

    void Awake()
    {
        pool = new DecalProjector[poolSize];

        for (int i = 0; i < poolSize; i++)
        {
            var go = new GameObject("PaintDecal_" + i);
            go.transform.SetParent(transform, false);

            var projector = go.AddComponent<DecalProjector>();
            projector.material = decalMaterial;
            projector.fadeFactor = 1f;

            go.SetActive(false);
            pool[i] = projector;
        }
    }

    public void PlacePaint(Vector3 point, Vector3 normal)
    {
        PlacePaint(point, normal, 1f);
    }

    public void PlacePaint(Vector3 point, Vector3 normal, float sizeMultiplier)
    {
        if (pool == null || pool.Length == 0) return;

        var projector = pool[nextIndex];
        nextIndex = (nextIndex + 1) % pool.Length;

        var t = projector.transform;
        t.position = point + normal * surfaceOffset;

        Vector3 upHint = Mathf.Abs(Vector3.Dot(normal, Vector3.up)) > 0.95f ? Vector3.forward : Vector3.up;
        t.rotation = Quaternion.LookRotation(-normal, upHint) * Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));

        float size = Random.Range(minDecalSize, maxDecalSize) * sizeMultiplier;
        projector.size = new Vector3(size, size, decalDepth);
        projector.pivot = new Vector3(0f, 0f, decalDepth * 0.5f);

        projector.gameObject.SetActive(true);
    }
}