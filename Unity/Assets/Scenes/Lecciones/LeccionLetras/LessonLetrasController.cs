using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.SceneManagement;
using System.Collections.Generic;
using System.Linq;
using System.Text;

public class LessonLetrasController : MonoBehaviour
{
    [Header("UI Setup")]
    public UIDocument uiDocument;
    public string mainMenuScene = "MainMenu";

    [Header("UI Mode")]
    [Tooltip("If true, hides the 3D guide, lesson text, progress bar and sidebar; shows only the camera feed.")]
    public bool cameraOnlyMode = false;

    [Header("Content Settings")]
    [Tooltip("Folder name inside 'Assets/Resources/Gestures/' (e.g. 'Letras')")]
    public string categoryFolder = "Letras";

    [Header("3D Guide Setup")]
    public Transform guideSpawnPoint; // Drag the "SpawnPoint" object here
    public RenderTexture guideRenderTexture; // Drag your 'RT_ModelView' here
    public float rotationSpeed = 0.5f;

    [Header("Guide Rendering Fixes")]
    [Tooltip("If true, replaces non-URP materials with an Unlit URP material so textures show consistently.")]
    public bool forceUnlitGuideMaterials = true;

    [Tooltip("If > 0, auto-scales the guide so its largest dimension fits this size (in Unity units).")]
    public float guideTargetSize = 0.6f;

    [Tooltip("If true, auto-frames the camera that renders to guideRenderTexture to fit the current guide.")]
    public bool autoFrameGuideCamera = true;

    [Tooltip("Extra distance multiplier for the guide camera framing.")]
    public float guideCameraPadding = 1.25f;

    [Tooltip("Background color for the RenderTexture camera (matches UI background by default).")]
    public Color guideCameraBackgroundColor = new Color(243f / 255f, 245f / 255f, 249f / 255f, 1f);

    [Header("Detection Settings")]
    public int historyFrameCount = 30; 

    [Header("OpenCV / UDP Auto Setup")]
    [Tooltip("Auto-create UDP receiver components in the scene if missing")]
    public bool autoSetupOpenCV = true;

    [Tooltip("UDP port for hand landmark/prediction JSON")]
    public int udpJsonPort = 5005;

    [Tooltip("UDP port for JPEG video frames")]
    public int udpVideoPort = 5006;
    
    // UI Elements
    private VisualElement _sidebar;
    private ScrollView _sidebarList;
    private VisualElement _menuOverlay;
    private Button _btnToggle;
    private Label _lblTarget;
    private Label _lblFeedback;
    private Label _lblDetectedLetter;
    private VisualElement _progressFill;
    private Image _guideImage; // Reference to the 3D View Image
    private Image _imgOpenCVFeed;

    [Header("OpenCV Feed")]
    public OpenCVFrameReceiver frameReceiver;

    [Header("Letter Model Settings")]
    [Range(0f, 1f)]
    public float correctConfidenceThreshold = 0.80f;

    [Tooltip("Confidence required to show an 'EXCELENTE' feedback (e.g. 0.80 = 80%).")]
    [Range(0f, 1f)]
    public float excellentConfidenceThreshold = 0.80f;

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
        if (autoSetupOpenCV)
        {
            EnsureOpenCVServices();
        }

        if (uiDocument == null) uiDocument = GetComponent<UIDocument>();
        var root = uiDocument.rootVisualElement;

        // 1. Bind UI Elements
        _sidebar = root.Q<VisualElement>("Sidebar");
        _sidebarList = root.Q<ScrollView>("Sidebar_List");
        _menuOverlay = root.Q<VisualElement>("MenuOverlay");
        _btnToggle = root.Q<Button>("Btn_ToggleMenu");
        
        _lblTarget = root.Q<Label>("Lbl_CurrentTarget");
        _lblFeedback = root.Q<Label>("Lbl_Feedback");
        _lblDetectedLetter = root.Q<Label>("Lbl_DetectedLetter");
        _progressFill = root.Q<VisualElement>("Progress_Fill");

        _imgOpenCVFeed = root.Q<Image>("Img_OpenCVFeed");

        if (cameraOnlyMode)
        {
            // Hide non-camera UI. Landmarks/points remain if they are drawn into the camera frames.
            if (_guideImage != null && _guideImage.parent != null)
                _guideImage.parent.style.display = DisplayStyle.None;

            if (_lblTarget != null && _lblTarget.parent != null)
                _lblTarget.parent.style.display = DisplayStyle.None;

            if (_sidebar != null) _sidebar.style.display = DisplayStyle.None;
            if (_menuOverlay != null) _menuOverlay.style.display = DisplayStyle.None;
            if (_btnToggle != null) _btnToggle.style.display = DisplayStyle.None;
        }

        if (frameReceiver == null)
            frameReceiver = FindObjectOfType<OpenCVFrameReceiver>();

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

    void EnsureOpenCVServices()
    {
        // Ensure OpenCVConnector exists (JSON receiver + landmark math)
        var connector = FindObjectOfType<OpenCVConnector>();
        if (connector == null)
        {
            var go = new GameObject("OpenCVConnector(Auto)");
            connector = go.AddComponent<OpenCVConnector>();
            connector.listenAddress = "0.0.0.0";
            connector.listenPort = udpJsonPort;
        }

        // Ensure OpenCVFrameReceiver exists (JPEG frames)
        var receiver = FindObjectOfType<OpenCVFrameReceiver>();
        if (receiver == null)
        {
            var go = new GameObject("OpenCVFrameReceiver(Auto)");
            receiver = go.AddComponent<OpenCVFrameReceiver>();
            receiver.listenAddress = "0.0.0.0";
            receiver.listenPort = udpVideoPort;
        }

        if (frameReceiver == null)
            frameReceiver = receiver;
    }

    void Update()
    {
        // In camera-only mode we intentionally show only the camera feed.
        if (cameraOnlyMode)
        {
            UpdateOpenCVFeedUI();
            return;
        }

        UpdateDetectedLetterUI();
        UpdateOpenCVFeedUI();

        // Only run the classic finger-shape + movement lesson logic if SignData exists.
        if (_currentTargetData != null)
        {
            (bool[] fingers, Vector2 handPos) = GetOpenCVData();
            TrackMovement(handPos);
            CompareGesture(fingers);
        }
    }

    void UpdateDetectedLetterUI()
    {
        if (_lblDetectedLetter == null) return;

        if (OpenCVConnector.Instance == null)
        {
            _lblDetectedLetter.text = "Detectado: -";
            return;
        }

        if (!OpenCVConnector.Instance.handDetected)
        {
            _lblDetectedLetter.text = "Detectado: -";
            return;
        }

        string letter = OpenCVConnector.Instance.currentLetter;
        float conf = OpenCVConnector.Instance.currentConfidence;

        if (string.IsNullOrWhiteSpace(letter) || letter == "?")
        {
            _lblDetectedLetter.text = "Detectado: ?";
            return;
        }

        _lblDetectedLetter.text = $"Detectado: {letter} ({Mathf.RoundToInt(conf * 100f)}%)";
    }

    void UpdateOpenCVFeedUI()
    {
        if (_imgOpenCVFeed == null) return;
        if (frameReceiver == null) return;
        if (frameReceiver.currentTexture == null) return;

        _imgOpenCVFeed.image = frameReceiver.currentTexture;
    }

    void ApplyModelCorrectnessOverride()
    {
        if (OpenCVConnector.Instance == null) return;
        if (!OpenCVConnector.Instance.handDetected) return;

        string predicted = OpenCVConnector.Instance.currentLetter;
        float conf = OpenCVConnector.Instance.currentConfidence;
        if (string.IsNullOrWhiteSpace(predicted) || predicted == "?") return;

        // Treat correctConfidenceThreshold as the minimum to consider a prediction "correct".
        // When confidence reaches 80% (or the configured excellent threshold), show "¡EXCELENTE!".
        float excellentThreshold = Mathf.Max(excellentConfidenceThreshold, correctConfidenceThreshold);
        if (conf < correctConfidenceThreshold) return;

        string expected = GetExpectedLetterFromTarget();
        if (string.IsNullOrWhiteSpace(expected)) return;

        if (string.Equals(predicted.Trim(), expected.Trim(), System.StringComparison.OrdinalIgnoreCase))
        {
            if (conf >= excellentThreshold)
                UpdateFeedback("¡EXCELENTE!", Color.green, 1f);
            else
                UpdateFeedback("Correcta", Color.green, 1f);
        }
    }

    string GetExpectedLetterFromTarget()
    {
        // Tries to extract a single letter A-Z from current SignData title or prefab name.
        if (_currentTargetData != null)
        {
            string title = _currentTargetData.signTitle;
            string fromTitle = ExtractSingleLetter(title);
            if (!string.IsNullOrEmpty(fromTitle)) return fromTitle;
        }

        if (_currentGuideObject != null)
        {
            string fromName = ExtractSingleLetter(_currentGuideObject.name);
            if (!string.IsNullOrEmpty(fromName)) return fromName;
        }

        return "";
    }

    string ExtractSingleLetter(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";

        string trimmed = text.Trim();
        if (trimmed.Length == 1 && char.IsLetter(trimmed[0]))
            return char.ToUpperInvariant(trimmed[0]).ToString();

        // Prefer explicit single-letter tokens, e.g. "Letra A" -> "A".
        // We avoid returning the first letter of words like "Letra".
        char[] separators = new[] { ' ', '\t', '\n', '\r', '-', '_', ':', ';', ',', '.', '/', '\\', '(', ')', '[', ']', '{', '}', '|', '+' };
        string[] tokens = trimmed.Split(separators, System.StringSplitOptions.RemoveEmptyEntries);
        for (int i = tokens.Length - 1; i >= 0; i--)
        {
            string t = tokens[i].Trim();
            if (t.Length == 1 && char.IsLetter(t[0]))
                return char.ToUpperInvariant(t[0]).ToString();
        }

        return "";
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

        // Prefer letter lessons (e.g. "A", "sign_a") over placeholder shapes (Cube/Cylinder).
        // Also dedupe letters: if multiple prefabs map to the same single-letter key, keep the first.
        var sorted = prefabs
            .OrderByDescending(p => !string.IsNullOrEmpty(GetLetterForAsset(p)))
            .ThenBy(p => GetLetterForAsset(p))
            .ThenBy(p => p.name)
            .ToArray();

        var unique = new List<GameObject>(sorted.Length);
        var seenLetters = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
        foreach (var p in sorted)
        {
            string letterKey = GetLetterForAsset(p);
            if (!string.IsNullOrEmpty(letterKey))
            {
                if (seenLetters.Contains(letterKey))
                    continue;
                seenLetters.Add(letterKey);
            }
            unique.Add(p);
        }

        _sidebarList.Clear();

        foreach (var p in unique)
        {
            Button btn = new Button();
            
            string name = p.name;
            var data = p.GetComponent<SignData>();
            if (data != null && !string.IsNullOrEmpty(data.signTitle)) name = data.signTitle;

            // If there is no SignData, but the asset name encodes a letter (e.g. sign_a), show it nicely.
            if (data == null)
            {
                string letter = GetLetterForAsset(p);
                if (!string.IsNullOrEmpty(letter))
                    name = $"Letra {letter}";
            }

            btn.text = name;
            btn.AddToClassList("sidebar-item"); 
            
            btn.clicked += () => {
                SetActiveTarget(p);
                CloseMenu(); 
            };

            _sidebarList.Add(btn);
        }

        if (unique.Count > 0) SetActiveTarget(unique[0]);
    }

    void SetActiveTarget(GameObject prefab)
    {
        if (guideSpawnPoint != null)
        {
            foreach (Transform child in guideSpawnPoint) Destroy(child.gameObject);
            _currentGuideObject = Instantiate(prefab, guideSpawnPoint);

            _currentTargetData = _currentGuideObject.GetComponent<SignData>();
            if (_currentTargetData == null)
            {
                // Allow using imported models (FBX) as lessons even if the prefab doesn't have SignData yet.
                _currentTargetData = _currentGuideObject.AddComponent<SignData>();
                string letter = GetLetterForAsset(prefab);
                _currentTargetData.signTitle = !string.IsNullOrEmpty(letter) ? letter : prefab.name;
                _currentTargetData.requiredMovement = GestureMovement.Static;
                _currentTargetData.minMovementThreshold = 0.15f;
            }

            FitGuideObjectToSpawn(_currentGuideObject);
            FixGuideMaterialsForURP(_currentGuideObject);
            FrameGuideCameraToObject(_currentGuideObject);
        }

        _positionHistory.Clear();
        _holdTimer = 0;
        UpdateFeedback("Prepara tu mano...", Color.white, 0f);

        if (_lblTarget != null)
        {
            string expected = GetExpectedLetterFromTarget();
            if (!string.IsNullOrEmpty(expected))
                _lblTarget.text = $"Letra {expected}";
            else if (_currentTargetData != null && !string.IsNullOrEmpty(_currentTargetData.signTitle))
                _lblTarget.text = _currentTargetData.signTitle;
        }
    }

    void FitGuideObjectToSpawn(GameObject guide)
    {
        if (guide == null) return;
        if (guideSpawnPoint == null) return;
        if (guideTargetSize <= 0f) return;

        // Make transform deterministic relative to spawn point.
        guide.transform.SetParent(guideSpawnPoint, true);
        guide.transform.localRotation = Quaternion.identity;

        var renderers = guide.GetComponentsInChildren<Renderer>(true);
        if (renderers == null || renderers.Length == 0) return;

        // 1) Center (initial)
        Bounds bounds = CalculateWorldBounds(renderers);
        guide.transform.position += (guideSpawnPoint.position - bounds.center);

        // 2) Uniform scale to fit
        bounds = CalculateWorldBounds(renderers);
        float maxSize = Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z));
        if (maxSize <= 0.0001f) return;
        float scaleFactor = guideTargetSize / maxSize;
        guide.transform.localScale *= scaleFactor;

        // 3) Re-center after scaling (critical for FBX pivots)
        bounds = CalculateWorldBounds(renderers);
        guide.transform.position += (guideSpawnPoint.position - bounds.center);
    }

    Bounds CalculateWorldBounds(Renderer[] renderers)
    {
        Bounds b = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
            b.Encapsulate(renderers[i].bounds);
        return b;
    }

    void FrameGuideCameraToObject(GameObject guide)
    {
        if (!autoFrameGuideCamera) return;
        if (guide == null) return;
        if (guideRenderTexture == null) return;

        Camera cam = FindGuideCamera();
        if (cam == null) return;

        var renderers = guide.GetComponentsInChildren<Renderer>(true);
        if (renderers == null || renderers.Length == 0) return;

        Bounds bounds = CalculateWorldBounds(renderers);
        Vector3 center = bounds.center;

        // Distance to fit bounds in view.
        float fov = Mathf.Max(1f, cam.fieldOfView) * Mathf.Deg2Rad;
        float radius = bounds.extents.magnitude;
        float dist = (radius / Mathf.Sin(fov * 0.5f)) * Mathf.Max(1.0f, guideCameraPadding);

        // Place camera in front of the object, looking at it.
        Vector3 forward = cam.transform.forward;
        if (forward.sqrMagnitude < 0.0001f) forward = Vector3.forward;
        cam.transform.position = center - forward.normalized * dist;
        cam.transform.LookAt(center);

        // Make sure clipping planes include the object.
        cam.nearClipPlane = Mathf.Max(0.01f, dist - radius * 2f);
        cam.farClipPlane = dist + radius * 4f;

        // Reduce perceived over-brightness in the RenderTexture.
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = guideCameraBackgroundColor;
    }

    Camera FindGuideCamera()
    {
        var cameras = FindObjectsOfType<Camera>();
        foreach (var cam in cameras)
        {
            if (cam != null && cam.targetTexture == guideRenderTexture)
                return cam;
        }
        return null;
    }

    void FixGuideMaterialsForURP(GameObject guide)
    {
        if (guide == null) return;

        Shader urpUnlit = Shader.Find("Universal Render Pipeline/Unlit");
        Shader urpLit = Shader.Find("Universal Render Pipeline/Lit");
        Shader fallbackUnlit = Shader.Find("Unlit/Texture");

        var renderers = guide.GetComponentsInChildren<Renderer>(true);
        foreach (var r in renderers)
        {
            if (r == null) continue;
            var mats = r.materials;
            bool changed = false;

            for (int i = 0; i < mats.Length; i++)
            {
                Material src = mats[i];
                if (src == null)
                {
                    // Assign a safe default.
                    Shader s = (urpUnlit != null) ? urpUnlit : fallbackUnlit;
                    if (s == null) continue;
                    mats[i] = new Material(s);
                    changed = true;
                    continue;
                }

                string shaderName = src.shader != null ? src.shader.name : "";
                bool isAlreadyURP = shaderName.StartsWith("Universal Render Pipeline", System.StringComparison.Ordinal);
                if (isAlreadyURP) continue;

                // Convert/override non-URP materials.
                Shader targetShader = null;
                if (forceUnlitGuideMaterials)
                    targetShader = urpUnlit != null ? urpUnlit : fallbackUnlit;
                else
                    targetShader = urpLit;

                if (targetShader == null) continue;

                var dst = new Material(targetShader);
                dst.name = src.name + "_URP";

                // Try to preserve the main texture.
                Texture mainTex = src.mainTexture;
                if (mainTex != null)
                {
                    if (dst.HasProperty("_BaseMap")) dst.SetTexture("_BaseMap", mainTex);
                    else if (dst.HasProperty("_MainTex")) dst.SetTexture("_MainTex", mainTex);
                }

                // Try to preserve color.
                Color c = Color.white;
                if (src.HasProperty("_Color")) c = src.color;
                if (dst.HasProperty("_BaseColor")) dst.SetColor("_BaseColor", c);
                else if (dst.HasProperty("_Color")) dst.SetColor("_Color", c);

                mats[i] = dst;
                changed = true;
            }

            if (changed)
                r.materials = mats;
        }
    }

    string GetLetterForAsset(GameObject asset)
    {
        if (asset == null) return "";
        var data = asset.GetComponent<SignData>();
        if (data != null)
        {
            string fromTitle = ExtractSingleLetter(data.signTitle);
            if (!string.IsNullOrEmpty(fromTitle)) return fromTitle;
        }
        return ExtractSingleLetter(asset.name);
    }

    // --- GAMEPLAY LOGIC ---

    void TrackMovement(Vector2 currentPos)
    {
        _positionHistory.Enqueue(currentPos);
        if (_positionHistory.Count > historyFrameCount) _positionHistory.Dequeue();
    }

    void CompareGesture(bool[] userFingers)
    {
        // Do not allow progress/success feedback without an actual detected hand.
        // Otherwise, default "all false" finger state can match some targets and auto-complete.
        if (OpenCVConnector.Instance == null || !OpenCVConnector.Instance.handDetected)
        {
            _holdTimer = 0;
            UpdateFeedback("Prepara tu mano...", Color.white, 0f);
            return;
        }

        // En lecciones por letra, valida SOLO si el modelo está detectando la letra esperada.
        // Esto evita que se marque correcto por otra letra/gesto.
        string expected = GetExpectedLetterFromTarget();
        string predicted = OpenCVConnector.Instance.currentLetter;
        float conf = OpenCVConnector.Instance.currentConfidence;

        if (!string.IsNullOrEmpty(expected))
        {
            bool hasPrediction = !string.IsNullOrWhiteSpace(predicted) && predicted != "?";
            bool confidentEnough = conf >= correctConfidenceThreshold;
            bool matchesExpected = hasPrediction && string.Equals(predicted.Trim(), expected.Trim(), System.StringComparison.OrdinalIgnoreCase);

            if (!confidentEnough || !matchesExpected)
            {
                _holdTimer = 0;
                UpdateFeedback($"Corrige: haz la letra {expected}", Color.white, 0f);
                return;
            }
        }

        bool movementMatch = CheckMovement(_currentTargetData.requiredMovement);

        if (movementMatch)
        {
            _holdTimer += Time.deltaTime;
            float progress = Mathf.Clamp01(_holdTimer / _requiredHoldTime);

            if (_holdTimer >= _requiredHoldTime)
            {
                float excellentThreshold = Mathf.Max(excellentConfidenceThreshold, correctConfidenceThreshold);
                if (OpenCVConnector.Instance.currentConfidence >= excellentThreshold)
                    UpdateFeedback("¡EXCELENTE!", Color.green, 1f);
                else
                    UpdateFeedback("Correcta", Color.green, 1f);
            }
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