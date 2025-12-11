using UnityEngine;
using UnityEngine.UIElements;

public class ThreeDVisualizer : MonoBehaviour
{
    [Header("UI Setup")]
    public UIDocument uiDocument;
    public string visualElementName = "Model3D_View";

    [Header("3D Setup")]
    public RenderTexture renderTexture;
    
    [Tooltip("The empty GameObject where models are spawned. If null, it auto-searches for 'SpawnPoint'.")]
    public Transform modelContainer; 

    public float rotationSpeed = 0.5f;

    // Internal state
    private Image _previewImage;
    private bool _isDragging = false;
    private VisualElement _container;

    void OnEnable()
    {
        if (uiDocument == null) uiDocument = GetComponent<UIDocument>();

        if (modelContainer == null)
        {
            GameObject obj = GameObject.Find("SpawnPoint");
            if (obj != null) modelContainer = obj.transform;
            else Debug.LogWarning("ThreeDVisualizer: No 'SpawnPoint' found in scene!");
        }

        var root = uiDocument.rootVisualElement;
        if (root != null) SetupVisualizer(root);
        else Invoke(nameof(RetrySetup), 0.1f);
    }

    void RetrySetup() => SetupVisualizer(uiDocument.rootVisualElement);

    void SetupVisualizer(VisualElement root)
    {
        _previewImage = root.Q<Image>(visualElementName);

        if (_previewImage == null) return;
        
        _previewImage.image = renderTexture;
        _previewImage.scaleMode = ScaleMode.ScaleToFit;

        _container = _previewImage.parent;
        _container.RegisterCallback<GeometryChangedEvent>(OnGeometryChanged);

        // --- FIXED REGISTRATION ---
        _previewImage.RegisterCallback<PointerDownEvent>(OnPointerDown);
        _previewImage.RegisterCallback<PointerUpEvent>(OnPointerUp);
        _previewImage.RegisterCallback<PointerMoveEvent>(OnPointerMove);
        _previewImage.RegisterCallback<PointerLeaveEvent>(OnPointerLeave); // Uses new method below
        
        UpdateAspectRatio();
    }

    private void OnGeometryChanged(GeometryChangedEvent evt)
    {
        if (evt.oldRect.width != evt.newRect.width) UpdateAspectRatio();
    }

    private void UpdateAspectRatio()
    {
        if (_container == null || renderTexture == null || float.IsNaN(_container.resolvedStyle.width)) return;
        float ratio = (float)renderTexture.width / (float)renderTexture.height;
        _container.style.height = _container.resolvedStyle.width / ratio;
    }

    // --- INTERACTION LOGIC ---

    private void OnPointerDown(PointerDownEvent evt)
    {
        if (evt.button == 0) 
        {
            _isDragging = true;
            _previewImage.CapturePointer(evt.pointerId);
        }
    }

    private void OnPointerUp(PointerUpEvent evt)
    {
        _isDragging = false;
        _previewImage.ReleasePointer(evt.pointerId);
    }

    // NEW METHOD: Specifically handles leaving the area
    private void OnPointerLeave(PointerLeaveEvent evt)
    {
        _isDragging = false;
        _previewImage.ReleasePointer(evt.pointerId);
    }

    private void OnPointerMove(PointerMoveEvent evt)
    {
        if (_isDragging && modelContainer != null)
        {
            if (modelContainer.childCount > 0)
            {
                Transform targetObject = modelContainer.GetChild(0);
                float rotAmount = -evt.deltaPosition.x * rotationSpeed;
                targetObject.Rotate(Vector3.up, rotAmount);
            }
        }
    }
}