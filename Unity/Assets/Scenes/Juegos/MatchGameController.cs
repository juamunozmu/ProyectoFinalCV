using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.SceneManagement;
using System.Collections.Generic;
using System.Linq;

public class DragGameController : MonoBehaviour
{
    [Header("UI Setup")]
    public UIDocument uiDocument;
    public string mainMenuScene = "MenuScene";

    [Header("Auto-Load Settings")]
    [Tooltip("The name of the folder inside 'Assets/Resources/Gestures/' to load items from.")]
    public string resourceFolderName = "MatchItems"; 

    [Header("3D Resources")]
    public List<GameObject> itemPrefabs = new List<GameObject>(); 
    
    public List<RenderTexture> slotTextures; 
    public List<Transform> spawnPoints;
    public float rotationSpeed = 0.5f;

    // Internal State
    private VisualElement _root;
    private Label _feedbackLabel;
    
    private bool _isDraggingWord = false;
    private VisualElement _draggedWord;
    private int _matchesFound = 0;
    private Dictionary<VisualElement, string> _slotCorrectAnswers = new Dictionary<VisualElement, string>();

    private bool _isRotatingSlot = false;
    private VisualElement _rotatingElement;
    private Transform _rotatingTarget;

    void OnEnable()
    {
        if (uiDocument == null) uiDocument = GetComponent<UIDocument>();
        _root = uiDocument.rootVisualElement;
        _feedbackLabel = _root.Q<Label>("Lbl_Feedback");

        var btnExit = _root.Q<Button>("Btn_Exit");
        if(btnExit != null) btnExit.clicked += () => SceneManager.LoadScene(mainMenuScene);

        LoadPrefabsFromFolder();
        SetupGame();
    }

    void LoadPrefabsFromFolder()
    {
        itemPrefabs.Clear();
        string fullPath = "Gestures/" + resourceFolderName;
        GameObject[] loadedObjects = Resources.LoadAll<GameObject>(fullPath);

        if (loadedObjects.Length > 0)
        {
            itemPrefabs.AddRange(loadedObjects);
            Debug.Log($"Loaded {loadedObjects.Length} items from 'Resources/{fullPath}'");
        }
        else
        {
            Debug.LogError($"No items found in 'Assets/Resources/{fullPath}'");
        }
    }

    void SetupGame()
    {
        _matchesFound = 0;
        
        if (itemPrefabs.Count < 4) 
        { 
            if(_feedbackLabel != null) _feedbackLabel.text = "Error: Need at least 4 items!";
            return; 
        }

        // 1. Pick 4 random items
        var selectedItems = itemPrefabs.OrderBy(x => Random.value).Take(4).ToList();
        List<string> wordList = new List<string>();

        // 2. Setup Slots
        for (int i = 0; i < 4; i++)
        {
            var slot = _root.Q($"Slot_{i}");
            var slotImage = slot.Q<Image>();
            
            slotImage.image = slotTextures[i];

            // Clear old models
            foreach(Transform child in spawnPoints[i]) Destroy(child.gameObject);
            
            // Spawn new model
            GameObject modelInstance = Instantiate(selectedItems[i], spawnPoints[i]);

            // Setup Rotation
            Transform currentModelTransform = modelInstance.transform; 
            slotImage.RegisterCallback<PointerDownEvent>(evt => StartRotateSlot(evt, slotImage, currentModelTransform));
            slotImage.RegisterCallback<PointerMoveEvent>(evt => UpdateRotateSlot(evt, currentModelTransform));
            slotImage.RegisterCallback<PointerUpEvent>(evt => StopRotateSlot(evt, slotImage));
            slotImage.RegisterCallback<PointerLeaveEvent>(evt => StopRotateSlot(evt, slotImage));

            // --- CHANGED LOGIC HERE ---
            // Default to the filename
            string displayName = selectedItems[i].name;

            // Check if the spawned object has SignData
            SignData data = modelInstance.GetComponent<SignData>();
            if (data != null && !string.IsNullOrEmpty(data.signTitle))
            {
                // Use the nice title (e.g., "Manzana")
                displayName = data.signTitle;
            }
            // --------------------------

            _slotCorrectAnswers[slot] = displayName;
            wordList.Add(displayName);
        }

        // 3. Setup Draggable Words
        var shuffledWords = wordList.OrderBy(x => Random.value).ToList();
        for (int i = 0; i < 4; i++)
        {
            var dragLabel = _root.Q<Label>($"Drag_{i}");
            dragLabel.text = shuffledWords[i];
            
            dragLabel.RemoveFromClassList("draggable-matched");
            dragLabel.style.display = DisplayStyle.Flex;
            dragLabel.transform.position = Vector3.zero;

            dragLabel.RegisterCallback<PointerDownEvent>(OnWordPointerDown);
            dragLabel.RegisterCallback<PointerMoveEvent>(OnWordPointerMove);
            dragLabel.RegisterCallback<PointerUpEvent>(OnWordPointerUp);
        }
    }

    // --- INTERACTION LOGIC ---

    private void StartRotateSlot(PointerDownEvent evt, VisualElement clickedImage, Transform targetModel)
    {
        if (_isDraggingWord) return;
        if (evt.button == 0) {
            _isRotatingSlot = true;
            _rotatingElement = clickedImage;
            _rotatingTarget = targetModel;
            clickedImage.CapturePointer(evt.pointerId);
        }
    }

    private void UpdateRotateSlot(PointerMoveEvent evt, Transform targetModel)
    {
        if (_isRotatingSlot && _rotatingTarget == targetModel) {
            float rotAmount = -evt.deltaPosition.x * rotationSpeed;
            targetModel.Rotate(Vector3.up, rotAmount);
        }
    }

    private void StopRotateSlot(IPointerEvent evt, VisualElement clickedImage)
    {
        if (_isRotatingSlot && _rotatingElement == clickedImage) {
            _isRotatingSlot = false;
            _rotatingElement.ReleasePointer(evt.pointerId);
            _rotatingElement = null;
            _rotatingTarget = null;
        }
    }

    private void OnWordPointerDown(PointerDownEvent evt)
    {
        var target = evt.target as VisualElement;
        if (_matchesFound >= 4 || target.ClassListContains("draggable-matched") || _isRotatingSlot) return;

        _isDraggingWord = true;
        _draggedWord = target;
        target.CapturePointer(evt.pointerId);
        target.AddToClassList("draggable-dragging");
    }

    private void OnWordPointerMove(PointerMoveEvent evt)
    {
        if (!_isDraggingWord || _draggedWord == null) return;
        _draggedWord.transform.position += (Vector3)evt.deltaPosition;
    }

    private void OnWordPointerUp(PointerUpEvent evt)
    {
        if (!_isDraggingWord || _draggedWord == null) return;
        _isDraggingWord = false;
        _draggedWord.ReleasePointer(evt.pointerId);
        _draggedWord.RemoveFromClassList("draggable-dragging");
        CheckDrop(_draggedWord);
        _draggedWord = null;
    }

    private void CheckDrop(VisualElement draggedItem)
    {
        Label label = draggedItem as Label;
        if (label == null) return;

        VisualElement hitSlot = null;
        for (int i = 0; i < 4; i++) {
            var slot = _root.Q($"Slot_{i}");
            if (slot.worldBound.Overlaps(draggedItem.worldBound)) {
                hitSlot = slot;
                break;
            }
        }

        if (hitSlot != null && label.text == _slotCorrectAnswers[hitSlot]) {
            OnMatchSuccess(label, hitSlot);
        } else {
            draggedItem.transform.position = Vector3.zero;
            _feedbackLabel.text = "Incorrecto...";
        }
    }

    private void OnMatchSuccess(Label label, VisualElement slot)
    {
        _matchesFound++;
        _feedbackLabel.text = _matchesFound == 4 ? "¡JUEGO COMPLETADO!" : "¡Correcto!";
        slot.AddToClassList("slot-correct");
        label.AddToClassList("draggable-matched");
        label.transform.position = Vector3.zero;
        slot.Add(label);
    }
}