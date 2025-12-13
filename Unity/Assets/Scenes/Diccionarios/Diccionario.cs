using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.SceneManagement;
using System.Collections.Generic;

public class DictionaryDetailController : MonoBehaviour
{
    [Header("UI Document")]
    public UIDocument uiDocument;
    public string mainMenuSceneName = "MainMenu";

    [Header("3D Setup")]
    public Transform modelSpawnPoint; // Drag your 'SpawnPoint' object here

    // Internal UI References
    private Label _lblTitle;
    private Label _lblDescription;

    void OnEnable()
    {
        if (uiDocument == null) uiDocument = GetComponent<UIDocument>();
        var root = uiDocument.rootVisualElement;

        // 1. Find UI Elements
        _lblTitle = root.Q<Label>("Lbl_Title");
        _lblDescription = root.Q<Label>("Lbl_Description");
        
        // 2. Setup Buttons
        var btnBack = root.Q<Button>("Btn_Back");
        if(btnBack != null) btnBack.clicked += GoBack;

        var btnHome = root.Q<Button>("Btn_Home");
        if(btnHome != null) btnHome.clicked += GoBack;

        // 3. Load the Content
        LoadData();
    }

    void LoadData()
    {
        // Get the prefab from the static bridge.
        GameObject prefab = DictionaryBridge.selectedPrefab;
        if (prefab == null && !string.IsNullOrWhiteSpace(DictionaryBridge.selectedPrefabResourcesPath))
        {
            prefab = Resources.Load<GameObject>(DictionaryBridge.selectedPrefabResourcesPath);
            DictionaryBridge.selectedPrefab = prefab;
        }

        // Fallback Logic
        if (prefab == null)
        {
            GameObject[] vowels = Resources.LoadAll<GameObject>("Gestures/Letras/vowels");
            if (vowels != null && vowels.Length > 0)
                prefab = vowels[0];
            else
            {
                GameObject[] consonants = Resources.LoadAll<GameObject>("Gestures/Letras/consonants");
                prefab = consonants != null && consonants.Length > 0 ? consonants[0] : null;
            }
            DictionaryBridge.selectedPrefab = prefab;
            DictionaryBridge.selectedPrefabResourcesPath = prefab != null ? $"Gestures/Letras/vowels/{prefab.name}" : null;
        }

        if (prefab == null)
        {
            Debug.LogError("No prefab selected and no lesson letter assets found.");
            return;
        }

        void EnsureAnimationPlays(GameObject instance, string resourcesPath)
        {
            if (instance == null) return;

            var animator = instance.GetComponentInChildren<Animator>(true);
            if (animator == null)
                animator = instance.AddComponent<Animator>();

            if (animator.runtimeAnimatorController == null && !string.IsNullOrWhiteSpace(resourcesPath))
            {
                var baseController = Resources.Load<RuntimeAnimatorController>("Gestures/Letras/vowels/sing_a");
                if (baseController != null)
                {
                    AnimationClip selectedClip = null;
                    var clips = Resources.LoadAll<AnimationClip>(resourcesPath);
                    if (clips != null)
                    {
                        for (int i = 0; i < clips.Length; i++)
                        {
                            var c = clips[i];
                            if (c == null) continue;
                            if (c.name != null && c.name.Contains("__preview__")) continue;
                            selectedClip = c;
                            break;
                        }
                    }

                    if (selectedClip != null)
                    {
                        var overrideController = new AnimatorOverrideController(baseController);
                        var overrides = new List<KeyValuePair<AnimationClip, AnimationClip>>();
                        overrideController.GetOverrides(overrides);
                        for (int i = 0; i < overrides.Count; i++)
                            overrides[i] = new KeyValuePair<AnimationClip, AnimationClip>(overrides[i].Key, selectedClip);
                        overrideController.ApplyOverrides(overrides);
                        animator.runtimeAnimatorController = overrideController;
                    }
                }
            }

            animator.Rebind();
            animator.Update(0f);
            animator.Play(0, 0, 0f);
        }

        // A. SPAWN MODEL
        if (modelSpawnPoint != null)
        {
            // Clear old models
            foreach (Transform child in modelSpawnPoint) Destroy(child.gameObject);
            
            // Spawn new model
            GameObject instance = Instantiate(prefab, modelSpawnPoint);
            
            if (instance != null)
            {
                instance.name = prefab.name;

                // --- KEY FIX START ---
                // Reset Position, Rotation, and Scale to strict defaults
                instance.transform.localPosition = Vector3.zero;
                instance.transform.localRotation = Quaternion.identity;
                instance.transform.localScale = Vector3.one; 
                // --- KEY FIX END ---

                // Setup Animation
                var animator = instance.GetComponentInChildren<Animator>(true);
                if (animator != null)
                {
                    animator.Rebind();
                    animator.Update(0f);
                    animator.Play(0, 0, 0f);
                }

                EnsureAnimationPlays(instance, DictionaryBridge.selectedPrefabResourcesPath);

                // NOTE: I removed the Bounds/Renderer calculation block here.
                // That block was calculating the center of the mesh and moving the object
                // to compensate, which is why your position wasn't 0,0,0 before.
            }
            
            // B. GET DATA
            SignData data = instance.GetComponent<SignData>();

            if (data != null)
            {
                if (_lblTitle != null) _lblTitle.text = data.signTitle;
                if (_lblDescription != null) _lblDescription.text = data.signDescription;
            }
            else
            {
                if (_lblTitle != null) _lblTitle.text = prefab.name;
                if (_lblDescription != null) _lblDescription.text = "No description available.";
            }
        }
    }

    void GoBack()
    {
        SceneManager.LoadScene(mainMenuSceneName);
    }
}