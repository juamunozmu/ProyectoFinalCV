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

    [Header("Filtering")]
    [Tooltip("If true, the dictionary shows only the lesson letters (vowels + consonants).")]
    public bool onlyLessonVowels = true;

    [Tooltip("Legacy folder setting (kept for backwards compatibility).")]
    public string lessonVowelsFolder = "Letras/vowels";

    [Tooltip("Header title displayed for the restricted list.")]
    public string lessonVowelsHeaderTitle = "Letras";

    // Internal State
    private VisualElement _listContainer;
    private TextField _searchInput;
    private List<Button> _allGeneratedButtons = new List<Button>();

    private static readonly HashSet<string> _letters = new HashSet<string>(
        Enumerable.Range('A', 26).Select(i => ((char)i).ToString()),
        System.StringComparer.OrdinalIgnoreCase);

    private static readonly string[] _lessonLetterFolders = new[] { "Gestures/Letras/vowels", "Gestures/Letras/consonants" };

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

        // Show only lesson letters (vowels + consonants) to avoid Cube/Cylinder/etc.
        onlyLessonVowels = true;
        lessonVowelsHeaderTitle = "Letras";

        GenerateDictionary();
    }

    static bool TryExtractSingleLetter(string text, out string letter)
    {
        letter = "";
        if (string.IsNullOrWhiteSpace(text)) return false;

        string trimmed = text.Trim();
        if (trimmed.Length == 1 && char.IsLetter(trimmed[0]))
        {
            letter = char.ToUpperInvariant(trimmed[0]).ToString();
            return true;
        }

        char[] separators = new[] { ' ', '\t', '\n', '\r', '-', '_', ':', ';', ',', '.', '/', '\\', '(', ')', '[', ']', '{', '}', '|', '+' };
        string[] tokens = trimmed.Split(separators, System.StringSplitOptions.RemoveEmptyEntries);
        for (int i = tokens.Length - 1; i >= 0; i--)
        {
            string t = tokens[i].Trim();
            if (t.Length == 1 && char.IsLetter(t[0]))
            {
                letter = char.ToUpperInvariant(t[0]).ToString();
                return true;
            }
        }

        return false;
    }

    bool TryGetLessonLetter(GameObject prefab, out string letter)
    {
        letter = "";
        if (prefab == null) return false;

        // Prefer SignData title if present.
        var data = prefab.GetComponent<SignData>();
        if (data != null && TryExtractSingleLetter(data.signTitle, out letter))
            return true;

        return TryExtractSingleLetter(prefab.name, out letter);
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

        // 2. Loop through categories (or restrict to vowel lessons)
        if (onlyLessonVowels)
        {
            var entries = new List<(GameObject Prefab, string Path)>();
            foreach (string folder in _lessonLetterFolders)
            {
                GameObject[] loaded = Resources.LoadAll<GameObject>(folder);
                if (loaded == null) continue;
                for (int i = 0; i < loaded.Length; i++)
                {
                    var p = loaded[i];
                    if (p == null) continue;
                    entries.Add((p, $"{folder}/{p.name}"));
                }
            }

            if (entries.Count == 0)
            {
                Debug.LogWarning("No lesson letter assets found at Resources/Gestures/Letras/(vowels|consonants). ");
                return;
            }

            Label header = new Label(string.IsNullOrEmpty(lessonVowelsHeaderTitle) ? "Letras" : lessonVowelsHeaderTitle);
            header.AddToClassList("card-title");
            header.style.marginTop = 20;
            _listContainer.Add(header);

            VisualElement groupCard = new VisualElement();
            groupCard.AddToClassList("card");
            _listContainer.Add(groupCard);

            // Keep only A-Z letters and dedupe by letter.
            // Prefer entries with SignData and/or an AnimatorController.
            var bestByLetter = entries
                .Select(x => new
                {
                    x.Prefab,
                    x.Path,
                    HasLetter = TryGetLessonLetter(x.Prefab, out var l),
                    Letter = l,
                    HasSignData = x.Prefab != null && x.Prefab.GetComponent<SignData>() != null,
                    HasAnimatorController = x.Prefab != null && x.Prefab.GetComponentInChildren<Animator>(true) != null && x.Prefab.GetComponentInChildren<Animator>(true).runtimeAnimatorController != null
                })
                .Where(x => x.HasLetter && _letters.Contains(x.Letter))
                .GroupBy(x => x.Letter, System.StringComparer.OrdinalIgnoreCase)
                .Select(g => g
                    .OrderByDescending(x => (x.HasSignData ? 2 : 0) + (x.HasAnimatorController ? 1 : 0))
                    .ThenBy(x => x.Prefab.name)
                    .First())
                .OrderBy(x => x.Letter)
                .ThenBy(x => x.Prefab.name)
                .ToArray();

            foreach (var entry in bestByLetter)
                CreateButtonForPrefab(entry.Prefab, entry.Path, groupCard);

            return;
        }

        foreach (string category in categoryFolders)
        {
            string loadPath = $"Gestures/{category}";
            GameObject[] loadedPrefabs = Resources.LoadAll<GameObject>(loadPath);

            if (loadedPrefabs.Length > 0)
            {
                Label header = new Label(category);
                header.AddToClassList("card-title");
                header.style.marginTop = 20;
                _listContainer.Add(header);

                VisualElement groupCard = new VisualElement();
                groupCard.AddToClassList("card");
                _listContainer.Add(groupCard);

                var sortedPrefabs = loadedPrefabs.OrderBy(p => p.name).ToArray();
                foreach (GameObject prefab in sortedPrefabs)
                    CreateButtonForPrefab(prefab, $"{loadPath}/{prefab.name}", groupCard);
            }
            else
            {
                Debug.LogWarning($"No prefabs found at path: Resources/{loadPath}. Check folder name!");
            }
        }
    }

    void CreateButtonForPrefab(GameObject prefab, string resourcesPath, VisualElement parent)
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
        btn.clicked += () => OpenDetailScene(prefab, resourcesPath);

        // 4. Add to UI
        parent.Add(btn);
        _allGeneratedButtons.Add(btn);
    }

    void OpenDetailScene(GameObject prefabToLoad, string resourcesPath)
    {
        // Pass the prefab to the static bridge so the next scene can read it
        DictionaryBridge.selectedPrefab = prefabToLoad;

        // Store a Resources path fallback for robustness (also used to load animation clips from FBX subassets).
        if (prefabToLoad != null)
            DictionaryBridge.selectedPrefabResourcesPath = string.IsNullOrWhiteSpace(resourcesPath)
                ? $"Gestures/Letras/vowels/{prefabToLoad.name}"
                : resourcesPath;

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