using UnityEngine;

// The held item and every replicated projectile share the same authored model.
public static class SmallRockModel
{
    static GameObject prefab;
    public static GameObject Create(Transform parent = null)
    {
        if (prefab == null) prefab = Resources.Load<GameObject>("SmallDistractionRock");
        return Object.Instantiate(prefab, parent, false);
    }
}
