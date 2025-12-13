using UnityEngine;

public static class DictionaryBridge
{
    // This variable stays alive even when scene changes
    public static GameObject selectedPrefab;

    // Resources path of the selected prefab (e.g. "Gestures/Letras/vowels/sign_a").
    // This survives cases where the static reference is lost (domain reload, entering scene directly).
    public static string selectedPrefabResourcesPath;
}