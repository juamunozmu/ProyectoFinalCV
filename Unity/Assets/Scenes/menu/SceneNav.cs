using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.SceneManagement;
using System.Collections.Generic;

public class SceneNavigator : MonoBehaviour
{
    public UIDocument uiDocument;

    // This struct allows you to map buttons to scenes in the Inspector
    [System.Serializable]
    public struct SceneLink
    {
        public string buttonName; // The name you gave in UXML (e.g., "Btn_Lesson_1")
        public string sceneName;  // The actual Scene name in Unity (e.g., "Lesson01_Scene")
    }

    // A list of all your links
    public List<SceneLink> navigationLinks;

    void OnEnable()
    {
        if (uiDocument == null) uiDocument = GetComponent<UIDocument>();
        var root = uiDocument.rootVisualElement;

        // Loop through the list you set up in the Inspector
        foreach (var link in navigationLinks)
        {
            // Find the button by name
            var btn = root.Q<Button>(link.buttonName);

            if (btn != null)
            {
                // Assign the click event
                btn.clicked += () => LoadGameScene(link.sceneName);
            }
            else
            {
                Debug.LogWarning($"SceneNavigator: Could not find button named '{link.buttonName}' in the UI.");
            }
        }
    }

    void LoadGameScene(string sceneName)
    {
        // Optional: Check if scene exists in build settings to prevent crashes
        if (Application.CanStreamedLevelBeLoaded(sceneName))
        {
            SceneManager.LoadScene(sceneName);
        }
        else
        {
            Debug.LogError($"Scene '{sceneName}' cannot be loaded. Did you add it to 'Build Settings'?");
        }
    }
}