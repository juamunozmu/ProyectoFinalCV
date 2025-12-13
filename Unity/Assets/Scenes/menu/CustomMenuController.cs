using UnityEngine;
using UnityEngine.UIElements;
using System.Collections.Generic;
using System; // Needed for DateTime
using System.Linq; // Needed to sort the list (consistency is key!)

public class CustomMenuController : MonoBehaviour
{
    [Header("UI Setup")]
    public UIDocument uiDocument;

    [Header("Sign of the Day Setup")]
    public Transform spawnPoint; // The "Studio" location (0, -500, 0)
    public ThreeDVisualizer visualizerScript; // Reference to the rotation script
    [Tooltip("Resources path to load models from. To use only the 5 vowel lessons, keep 'Gestures/Letras/vowels'.")]
    public string resourceRootFolder = "Gestures/Letras/vowels"; // The folder inside Resources

    [Header("Filtering")]
    [Tooltip("If true, uses only the lesson letters (vowels + consonants) regardless of Inspector values.")]
    public bool onlyLessonVowels = true;

    [Tooltip("Resources folder used when onlyLessonVowels is enabled (kept for backwards compatibility).")]
    public string lessonVowelsFolder = "Gestures/Letras/vowels";

    private static readonly string[] _lessonLetterFolders = new[] { "Gestures/Letras/vowels", "Gestures/Letras/consonants" };

    private List<Button> _buttons;
    private List<VisualElement> _views;

    // Names match the UXML
    private readonly string[] _viewNames = { "View_Home", "View_Dictionary", "View_Lessons", "View_Practice" };
    private readonly string[] _btnNames = { "Btn_Home", "Btn_Dictionary", "Btn_Lessons", "Btn_Practice" };

    private void OnEnable()
    {
        if (uiDocument == null) uiDocument = GetComponent<UIDocument>();
        var root = uiDocument.rootVisualElement;

        // 1. SET DATE LABEL
        var dateLabel = root.Q<Label>("Lbl_Date");
        if (dateLabel != null)
        {
            dateLabel.text = DateTime.Now.ToString("dd/MM/yyyy");
        }

        // 2. SPAWN SIGN OF THE DAY
        SpawnDailySign();

        // 3. SETUP TABS (Standard Logic)
        _buttons = new List<Button>();
        _views = new List<VisualElement>();

        for (int i = 0; i < _btnNames.Length; i++)
        {
            var view = root.Q<VisualElement>(_viewNames[i]);
            var btn = root.Q<Button>(_btnNames[i]);
            
            if(view == null || btn == null) {
                Debug.LogError($"Could not find element: {_viewNames[i]} or {_btnNames[i]}");
                continue;
            }

            _views.Add(view);
            _buttons.Add(btn);

            int index = i; 
            btn.clicked += () => SwitchTab(index);
        }

        SwitchTab(0);
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
        var data = prefab.GetComponent<SignData>();
        if (data != null && TryExtractSingleLetter(data.signTitle, out letter))
            return true;
        return TryExtractSingleLetter(prefab.name, out letter);
    }

    static void EnsureAnimationPlays(GameObject instance, string resourcesPath)
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
                        // Skip Unity preview clips if present.
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

    private void SpawnDailySign()
    {
        // A. Load signs.
        var candidates = new List<(GameObject Prefab, string Path)>();

        if (onlyLessonVowels)
        {
            foreach (string folder in _lessonLetterFolders)
            {
                GameObject[] loaded = Resources.LoadAll<GameObject>(folder);
                if (loaded == null) continue;
                for (int i = 0; i < loaded.Length; i++)
                {
                    var p = loaded[i];
                    if (p == null) continue;
                    candidates.Add((p, $"{folder}/{p.name}"));
                }
            }
        }
        else
        {
            if (string.IsNullOrWhiteSpace(resourceRootFolder))
                resourceRootFolder = "Gestures";

            GameObject[] loaded = Resources.LoadAll<GameObject>(resourceRootFolder);
            if (loaded != null)
            {
                for (int i = 0; i < loaded.Length; i++)
                {
                    var p = loaded[i];
                    if (p == null) continue;
                    candidates.Add((p, $"{resourceRootFolder}/{p.name}"));
                }
            }
        }

        if (candidates.Count == 0)
        {
            Debug.LogWarning("No lesson letters found to spawn (vowels/consonants). ");
            return;
        }

        // Keep only items that map to a single letter and dedupe by letter.
        var bestByLetter = candidates
            .Select(x => new { x.Prefab, x.Path, HasLetter = TryGetLessonLetter(x.Prefab, out var l), Letter = l })
            .Where(x => x.HasLetter)
            .GroupBy(x => x.Letter, System.StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderBy(x => x.Prefab.name).First())
            .ToArray();

        if (bestByLetter.Length == 0)
        {
            Debug.LogWarning("No letter signs found after filtering.");
            return;
        }

        // B. Generate the "Daily Seed"
        // We create an integer like 20251230 (Year + Month + Day)
        // This number is unique to today.
        int dailySeed = (DateTime.Now.Year * 10000) + (DateTime.Now.Month * 100) + DateTime.Now.Day;

        // C. Initialize a separate Random generator with that seed
        // We use System.Random instead of UnityEngine.Random to avoid affecting game physics/logic
        System.Random dailyRng = new System.Random(dailySeed);

        // D. Sort the list first! 
        // Important: "Resources.LoadAll" order is not guaranteed. Sorting by name ensures
        // that "Apple" is always index 0 and "Zebra" is always index 50 on every device.
        var sortedSigns = bestByLetter.OrderBy(x => x.Prefab.name).ToArray();

        // E. Pick the winner
        int randomIndex = dailyRng.Next(0, sortedSigns.Length);
        GameObject signOfTheDay = sortedSigns[randomIndex].Prefab;
        string signPath = sortedSigns[randomIndex].Path;

        Debug.Log($"Today's Seed: {dailySeed} | Sign: {signOfTheDay.name}");

        // F. Spawn it in the Studio
        if (spawnPoint != null)
        {
            // Clear old objects first
            foreach(Transform child in spawnPoint) Destroy(child.gameObject);

            // Spawn new
            GameObject instance = Instantiate(signOfTheDay, spawnPoint);
            if (instance != null)
            {
                instance.name = signOfTheDay.name;
                instance.transform.localPosition = Vector3.zero;
                instance.transform.localRotation = Quaternion.identity;
                EnsureAnimationPlays(instance, signPath);
            }
        }
    }

    private void SwitchTab(int activeIndex)
    {
        for (int i = 0; i < _buttons.Count; i++)
        {
            if (i == activeIndex)
            {
                _buttons[i].AddToClassList("active");
                _views[i].AddToClassList("view-active");
                _views[i].style.display = DisplayStyle.Flex;
                _views[i].BringToFront(); 
            }
            else
            {
                _buttons[i].RemoveFromClassList("active");
                _views[i].RemoveFromClassList("view-active");
                _views[i].style.display = DisplayStyle.None;
            }
        }
    }
}