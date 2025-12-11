using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.SceneManagement;

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
        // Get the prefab from the static bridge
        GameObject prefab = DictionaryBridge.selectedPrefab;

        if (prefab == null)
        {
            Debug.LogError("No prefab selected! Did you start from the Main Menu?");
            return;
        }

        // A. SPAWN MODEL
        if (modelSpawnPoint != null)
        {
            // Clear old models
            foreach (Transform child in modelSpawnPoint) Destroy(child.gameObject);
            
            // Spawn new model
            GameObject instance = Instantiate(prefab, modelSpawnPoint);
            
            // B. GET DATA
            // Look for the SignData script on the object we just spawned
            SignData data = instance.GetComponent<SignData>();

            if (data != null)
            {
                // Update UI Labels
                if (_lblTitle != null) _lblTitle.text = data.signTitle;
                if (_lblDescription != null) _lblDescription.text = data.signDescription;
            }
            else
            {
                // Fallback if you forgot to add the script to the prefab
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