using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.SceneManagement;
using System.Collections.Generic;
using System.Linq;

public class DictionaryLoader : MonoBehaviour
{
    [Header("UI Document")]
    public UIDocument uiDocument;
    public string detailSceneName = "DiccionarioScene";

    [Header("Data Source")]
    [Tooltip("Folder names inside 'Assets/Resources/Gestures/'. Example: 'Letras', 'Saludos'")]
    public List<string> categoryFolders; 

    // Internal State
    private VisualElement _listContainer;
    private TextField _searchInput;
    private List<Button> _allGeneratedButtons = new List<Button>();

    void OnEnable()
    {
        if (uiDocument == null) uiDocument = GetComponent<UIDocument>();
        var root = uiDocument.rootVisualElement;

        // Find the empty container we made in UXML
        _listContainer = root.Q<VisualElement>("Dictionary_List_Container");
        _searchInput = root.Q<TextField>("Search_Input");

        // Setup Search Listener
        if (_searchInput != null)
            _searchInput.RegisterValueChangedCallback(evt => FilterList(evt.newValue));

        GenerateDictionary();
    }

    void GenerateDictionary()
    {
        if (_listContainer == null) {
            Debug.LogError("Could not find 'Dictionary_List_Container' in UXML. Did you add it?");
            return;
        }

        // 1. Reset UI
        _listContainer.Clear();
        _allGeneratedButtons.Clear();

        // 2. Loop through categories
        foreach (string category in categoryFolders)
        {
            // PATH FIX: Resources.Load uses path relative to Resources folder.
            // If actual folder is "Assets/Resources/Gestures/Letras", load path is "Gestures/Letras"
            string loadPath = $"Gestures/{category}";

            GameObject[] loadedPrefabs = Resources.LoadAll<GameObject>(loadPath);

            if (loadedPrefabs.Length > 0)
            {
                // A. Create Header (e.g. "Letras")
                Label header = new Label(category);
                header.AddToClassList("card-title"); 
                header.style.marginTop = 20; 
                _listContainer.Add(header);

                // B. Create Card Container
                VisualElement groupCard = new VisualElement();
                groupCard.AddToClassList("card");
                _listContainer.Add(groupCard);

                // C. Sort A-Z
                var sortedPrefabs = loadedPrefabs.OrderBy(p => p.name).ToArray();

                // D. Create Buttons
                foreach (GameObject prefab in sortedPrefabs)
                {
                    CreateButtonForPrefab(prefab, groupCard);
                }
            }
            else
            {
                Debug.LogWarning($"No prefabs found at path: Resources/{loadPath}. Check folder name!");
            }
        }
    }

    void CreateButtonForPrefab(GameObject prefab, VisualElement parent)
    {
        // 1. Get Title
        string buttonText = prefab.name; 
        SignData data = prefab.GetComponent<SignData>();
        
        if (data != null && !string.IsNullOrEmpty(data.signTitle))
        {
            buttonText = data.signTitle;
        }

        // 2. Create Button
        Button btn = new Button();
        btn.text = buttonText;
        btn.AddToClassList("list-item"); // Apply USS style
        
        // 3. Logic: Click -> Bridge Data -> Change Scene
        btn.clicked += () => OpenDetailScene(prefab);

        // 4. Add to UI
        parent.Add(btn);
        _allGeneratedButtons.Add(btn);
    }

    void OpenDetailScene(GameObject prefabToLoad)
    {
        // Pass the prefab to the static bridge so the next scene can read it
        DictionaryBridge.selectedPrefab = prefabToLoad;
        SceneManager.LoadScene(detailSceneName);
    }

    void FilterList(string searchText)
    {
        string searchLower = searchText.ToLower();

        foreach (var btn in _allGeneratedButtons)
        {
            if (string.IsNullOrEmpty(searchText) || btn.text.ToLower().Contains(searchLower))
                btn.style.display = DisplayStyle.Flex; // Show
            else
                btn.style.display = DisplayStyle.None; // Hide
        }
    }
}