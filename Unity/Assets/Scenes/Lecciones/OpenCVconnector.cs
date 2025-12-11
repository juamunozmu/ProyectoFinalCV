using UnityEngine;
using System.Collections.Generic;

public class OpenCVConnector : MonoBehaviour
{
    // Singleton instance for easy access
    public static OpenCVConnector Instance;

    // The calculated data that LessonController reads
    public bool[] currentFingerStates = new bool[5];
    public Vector2 currentHandPosition = Vector2.zero;

    private void Awake()
    {
        Instance = this;
    }

    void Update()
    {
        // 1. GET RAW LANDMARKS FROM YOUR ASSET
        // This line depends on what asset you are using!
        // Example: List<Vector2> points = MediaPipeManager.GetLandmarks();
        List<Vector2> points = MockGetLandmarks(); // <--- REPLACE THIS

        if (points != null && points.Count == 21)
        {
            // 2. DO THE MATH
            currentFingerStates = HandMath.AnalyzeFingers(points);
            currentHandPosition = HandMath.GetHandCenter(points);
        }
    }

    // --- MOCKUP FUNCTION (Delete this when you connect your real asset) ---
    List<Vector2> MockGetLandmarks()
    {
        // If you don't have the camera running yet, this returns null
        return null; 
    }
}