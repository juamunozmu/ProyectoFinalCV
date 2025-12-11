using UnityEngine;

// --- IMPORTANT: Define the Enum OUTSIDE the class brackets ---
public enum GestureMovement
{
    Static, // No movement (A, B, C...)
    Up,
    Down,
    Left,
    Right,
    Circle,
    Wave
}
// -------------------------------------------------------------

public class SignData : MonoBehaviour
{
    [Header("Basic Info")]
    public string signTitle;
    [TextArea(3, 5)]
    public string signDescription;

    [Header("Hand Shape (The Answer Key)")]
    public bool isThumbOpen;
    public bool isIndexOpen;
    public bool isMiddleOpen;
    public bool isRingOpen;
    public bool isPinkyOpen;

    [Header("Movement Requirements")]
    // Now this line will work because the Enum is public and global
    public GestureMovement requiredMovement = GestureMovement.Static;
    
    [Tooltip("How fast/far must they move? (0.1 is usually good)")]
    public float minMovementThreshold = 0.15f; 
}