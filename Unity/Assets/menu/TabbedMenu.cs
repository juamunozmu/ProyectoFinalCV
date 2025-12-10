using UnityEngine;
using UnityEngine.UIElements;
using System.Collections.Generic;

public class CustomMenuController : MonoBehaviour
{
    public UIDocument uiDocument;

    private List<Button> _buttons;
    private List<VisualElement> _views;

    // Names match the UXML
    private readonly string[] _viewNames = { "View_Home", "View_Dictionary", "View_Lessons", "View_Practice" };
    private readonly string[] _btnNames = { "Btn_Home", "Btn_Dictionary", "Btn_Lessons", "Btn_Practice" };

    private void OnEnable()
    {
        if (uiDocument == null) uiDocument = GetComponent<UIDocument>();
        var root = uiDocument.rootVisualElement;

        _buttons = new List<Button>();
        _views = new List<VisualElement>();

        // Gather elements and assign clicks
        for (int i = 0; i < _btnNames.Length; i++)
        {
            var view = root.Q<VisualElement>(_viewNames[i]);
            var btn = root.Q<Button>(_btnNames[i]);
            
            // Safety check
            if(view == null || btn == null) {
                Debug.LogError($"Could not find element: {_viewNames[i]} or {_btnNames[i]}");
                continue;
            }

            _views.Add(view);
            _buttons.Add(btn);

            int index = i; // Capture index for lambda
            btn.clicked += () => SwitchTab(index);
        }

        // Initialize state
        SwitchTab(0);
    }

    private void SwitchTab(int activeIndex)
    {
        for (int i = 0; i < _buttons.Count; i++)
        {
            // 1. Handle Button Styling
            if (i == activeIndex)
                _buttons[i].AddToClassList("active");
            else
                _buttons[i].RemoveFromClassList("active");

            // 2. Handle View Animation
            // We do NOT use display:none anymore. We toggle the class that handles Opacity/Translate.
            if (i == activeIndex)
            {
                _views[i].AddToClassList("view-active");
                // Ensure it draws on top of fading out views
                _views[i].BringToFront(); 
            }
            else
            {
                _views[i].RemoveFromClassList("view-active");
            }
        }
    }
}