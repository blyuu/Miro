using UnityEngine;

public class AutoDestroyTimed : MonoBehaviour
{
    [SerializeField] private float lifetime = 0.6f;

    void Start()
    {
        Destroy(gameObject, lifetime);
    }
}
