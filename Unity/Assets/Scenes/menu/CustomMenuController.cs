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
    public string resourceRootFolder = "Gestures"; // The root folder inside Resources

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

    private void SpawnDailySign()
    {
        // A. Load ALL prefabs from "Assets/Resources/Gestures" and subfolders
        GameObject[] allSigns = Resources.LoadAll<GameObject>(resourceRootFolder);

        if (allSigns.Length == 0)
        {
            Debug.LogWarning($"No signs found in Resources/{resourceRootFolder}");
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
        var sortedSigns = allSigns.OrderBy(x => x.name).ToArray();

        // E. Pick the winner
        int randomIndex = dailyRng.Next(0, sortedSigns.Length);
        GameObject signOfTheDay = sortedSigns[randomIndex];

        Debug.Log($"Today's Seed: {dailySeed} | Sign: {signOfTheDay.name}");

        // F. Spawn it in the Studio
        if (spawnPoint != null)
        {
            // Clear old objects first
            foreach(Transform child in spawnPoint) Destroy(child.gameObject);

            // Spawn new
            GameObject instance = Instantiate(signOfTheDay, spawnPoint);
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
                _views[i].BringToFront(); 
            }
            else
            {
                _buttons[i].RemoveFromClassList("active");
                _views[i].RemoveFromClassList("view-active");
            }
        }
    }
}