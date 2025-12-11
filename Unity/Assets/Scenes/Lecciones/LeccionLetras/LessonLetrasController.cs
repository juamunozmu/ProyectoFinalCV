using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.SceneManagement;
using System.Collections.Generic;
using System.Linq;

public class LessonLetrasController : MonoBehaviour
{
    [Header("UI Setup")]
    public UIDocument uiDocument;
    public string mainMenuScene = "MainMenu";

    [Header("Content Settings")]
    [Tooltip("Folder name inside 'Assets/Resources/Gestures/' (e.g. 'Letras')")]
    public string categoryFolder = "Letras";

    [Header("3D Guide Setup")]
    public Transform guideSpawnPoint; // Drag the "SpawnPoint" object here
    public RenderTexture guideRenderTexture; // Drag your 'RT_ModelView' here
    public float rotationSpeed = 0.5f;

    [Header("Detection Settings")]
    public int historyFrameCount = 30; 
    
    // UI Elements
    private VisualElement _sidebar;
    private ScrollView _sidebarList;
    private VisualElement _menuOverlay;
    private Button _btnToggle;
    private Label _lblTarget;
    private Label _lblFeedback;
    private VisualElement _progressFill;
    private Image _guideImage; // Reference to the 3D View Image

    // Internal State
    private bool _isMenuOpen = false;
    private GameObject _currentGuideObject;
    private SignData _currentTargetData;
    
    // Logic State
    private float _holdTimer = 0f;
    private float _requiredHoldTime = 1.0f; 
    private Queue<Vector2> _positionHistory = new Queue<Vector2>();

    // Rotation State
    private bool _isRotating = false;

    void OnEnable()
    {
        if (uiDocument == null) uiDocument = GetComponent<UIDocument>();
        var root = uiDocument.rootVisualElement;

        // 1. Bind UI Elements
        _sidebar = root.Q<VisualElement>("Sidebar");
        _sidebarList = root.Q<ScrollView>("Sidebar_List");
        _menuOverlay = root.Q<VisualElement>("MenuOverlay");
        _btnToggle = root.Q<Button>("Btn_ToggleMenu");
        
        _lblTarget = root.Q<Label>("Lbl_CurrentTarget");
        _lblFeedback = root.Q<Label>("Lbl_Feedback");
        _progressFill = root.Q<VisualElement>("Progress_Fill");

        // 2. Bind 3D Guide View & Logic
        _guideImage = root.Q<Image>("Guide_3D_View");
        if (_guideImage != null)
        {
            // Assign Texture
            if(guideRenderTexture != null) _guideImage.image = guideRenderTexture;

            // Register Rotation Events
            _guideImage.RegisterCallback<PointerDownEvent>(OnPointerDown);
            _guideImage.RegisterCallback<PointerMoveEvent>(OnPointerMove);
            _guideImage.RegisterCallback<PointerUpEvent>(OnPointerUp);
            _guideImage.RegisterCallback<PointerLeaveEvent>(OnPointerLeave);
        }

        // 3. Button Logic
        if (_btnToggle != null) _btnToggle.clicked += ToggleMenu;
        
        var btnBack = root.Q<Button>("Btn_Back");
        if(btnBack != null) btnBack.clicked += () => SceneManager.LoadScene(mainMenuScene);

        // 4. Overlay Logic
        if (_menuOverlay != null)
        {
            _menuOverlay.RegisterCallback<PointerDownEvent>(evt => CloseMenu());
        }

        // 5. Load Content
        LoadLessonItems();
    }

    void Update()
    {
        if (_currentTargetData != null)
        {
            (bool[] fingers, Vector2 handPos) = GetOpenCVData();
            TrackMovement(handPos);
            CompareGesture(fingers);
        }
    }

    // --- ROTATION LOGIC (New) ---

    private void OnPointerDown(PointerDownEvent evt)
    {
        if (evt.button == 0) // Left click
        {
            _isRotating = true;
            _guideImage.CapturePointer(evt.pointerId);
        }
    }

    private void OnPointerUp(PointerUpEvent evt)
    {
        _isRotating = false;
        _guideImage.ReleasePointer(evt.pointerId);
    }

    private void OnPointerLeave(PointerLeaveEvent evt)
    {
        _isRotating = false;
        _guideImage.ReleasePointer(evt.pointerId);
    }

    private void OnPointerMove(PointerMoveEvent evt)
    {
        // Only rotate if dragging AND we have an object to rotate
        if (_isRotating && _currentGuideObject != null)
        {
            float rotAmount = -evt.deltaPosition.x * rotationSpeed;
            _currentGuideObject.transform.Rotate(Vector3.up, rotAmount);
        }
    }

    // --- UI & CONTENT LOADING ---

    void ToggleMenu()
    {
        if (_isMenuOpen) CloseMenu();
        else OpenMenu();
    }

    void OpenMenu()
    {
        _isMenuOpen = true;
        _sidebar.RemoveFromClassList("sidebar-closed");
        _sidebar.AddToClassList("sidebar-open");
        
        if (_menuOverlay != null) _menuOverlay.RemoveFromClassList("overlay-hidden");
        if (_btnToggle != null) _btnToggle.text = "✕";
    }

    void CloseMenu()
    {
        _isMenuOpen = false;
        _sidebar.RemoveFromClassList("sidebar-open");
        _sidebar.AddToClassList("sidebar-closed");
        
        if (_menuOverlay != null) _menuOverlay.AddToClassList("overlay-hidden");
        if (_btnToggle != null) _btnToggle.text = "☰";
    }

    void LoadLessonItems()
    {
        if (_sidebarList == null) return;

        string path = $"Gestures/{categoryFolder}";
        GameObject[] prefabs = Resources.LoadAll<GameObject>(path);

        if (prefabs.Length == 0) 
        {
            Debug.LogError($"No lessons found in Resources/{path}");
            return;
        }

        var sorted = prefabs.OrderBy(x => x.name).ToArray();

        _sidebarList.Clear();

        foreach (var p in sorted)
        {
            Button btn = new Button();
            
            string name = p.name;
            var data = p.GetComponent<SignData>();
            if (data != null && !string.IsNullOrEmpty(data.signTitle)) name = data.signTitle;

            btn.text = name;
            btn.AddToClassList("sidebar-item"); 
            
            btn.clicked += () => {
                SetActiveTarget(p);
                CloseMenu(); 
            };

            _sidebarList.Add(btn);
        }

        if (sorted.Length > 0) SetActiveTarget(sorted[0]);
    }

    void SetActiveTarget(GameObject prefab)
    {
        if (guideSpawnPoint != null)
        {
            foreach (Transform child in guideSpawnPoint) Destroy(child.gameObject);
            _currentGuideObject = Instantiate(prefab, guideSpawnPoint);
            _currentTargetData = _currentGuideObject.GetComponent<SignData>();
        }

        _positionHistory.Clear();
        _holdTimer = 0;
        UpdateFeedback("Prepara tu mano...", Color.white, 0f);

        if (_currentTargetData != null)
        {
            _lblTarget.text = _currentTargetData.signTitle;
        }
    }

    // --- GAMEPLAY LOGIC ---

    void TrackMovement(Vector2 currentPos)
    {
        _positionHistory.Enqueue(currentPos);
        if (_positionHistory.Count > historyFrameCount) _positionHistory.Dequeue();
    }

    void CompareGesture(bool[] userFingers)
    {
        bool shapeMatch = CheckFingerShape(userFingers);

        if (!shapeMatch)
        {
            _holdTimer = 0;
            UpdateFeedback("Corrige la forma de la mano", Color.white, 0f);
            return; 
        }

        bool movementMatch = CheckMovement(_currentTargetData.requiredMovement);

        if (movementMatch)
        {
            _holdTimer += Time.deltaTime;
            float progress = Mathf.Clamp01(_holdTimer / _requiredHoldTime);

            if (_holdTimer >= _requiredHoldTime)
                UpdateFeedback("¡EXCELENTE!", Color.green, 1f);
            else
                UpdateFeedback("¡Bien! Manténlo...", Color.yellow, progress);
        }
        else
        {
            _holdTimer = 0;
            string moveName = _currentTargetData.requiredMovement.ToString();
            UpdateFeedback($"Mueve la mano: {moveName}", Color.yellow, 0f);
        }
    }

    bool CheckFingerShape(bool[] fingers)
    {
        return fingers[0] == _currentTargetData.isThumbOpen &&
               fingers[1] == _currentTargetData.isIndexOpen &&
               fingers[2] == _currentTargetData.isMiddleOpen &&
               fingers[3] == _currentTargetData.isRingOpen &&
               fingers[4] == _currentTargetData.isPinkyOpen;
    }

    bool CheckMovement(GestureMovement type)
    {
        if (type == GestureMovement.Static) return true; 
        if (_positionHistory.Count < 10) return false; 

        Vector2 start = _positionHistory.Peek();
        Vector2 end = _positionHistory.Last();
        Vector2 delta = end - start;
        
        float distance = delta.magnitude;
        if (distance < _currentTargetData.minMovementThreshold) return false;

        switch (type)
        {
            case GestureMovement.Up: return delta.y > Mathf.Abs(delta.x);
            case GestureMovement.Down: return delta.y < -Mathf.Abs(delta.x);
            case GestureMovement.Left: return delta.x < -Mathf.Abs(delta.y);
            case GestureMovement.Right: return delta.x > Mathf.Abs(delta.y);
        }
        return false;
    }

    void UpdateFeedback(string msg, Color col, float progress01)
    {
        if (_lblFeedback != null)
        {
            _lblFeedback.text = msg;
            _lblFeedback.style.color = col;
        }
        if (_progressFill != null)
        {
            _progressFill.style.width = Length.Percent(progress01 * 100);
        }
    }

    (bool[], Vector2) GetOpenCVData()
    {
        if (OpenCVConnector.Instance != null)
        {
            return (OpenCVConnector.Instance.currentFingerStates, OpenCVConnector.Instance.currentHandPosition);
        }
        return (new bool[5], new Vector2(0.5f, 0.5f));
    }
}