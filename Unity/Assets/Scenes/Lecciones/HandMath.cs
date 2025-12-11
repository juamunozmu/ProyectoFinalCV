using UnityEngine;
using System.Collections.Generic;

public static class HandMath
{
    // Standard Landmark Indices (MediaPipe/OpenCV Standard)
    // 0=Wrist, 4=ThumbTip, 8=IndexTip, 12=MiddleTip, 16=RingTip, 20=PinkyTip
    // Knuckles (MCP/PIP): 2=ThumbJ, 5=IndexJ, 9=MiddleJ, 13=RingJ, 17=PinkyJ

    public static bool[] AnalyzeFingers(List<Vector2> landmarks)
    {
        if (landmarks == null || landmarks.Count < 21) 
            return new bool[5]; // Return all false if tracking lost

        bool[] states = new bool[5];
        Vector2 wrist = landmarks[0];

        // --- 1. THUMB (Comparison against Index Knuckle or Pinky Base) ---
        // A simple check: Is the Thumb Tip further from the Pinky Knuckle (17) than the Thumb Joint (2) is?
        float thumbTipDist = Vector2.Distance(landmarks[4], landmarks[17]);
        float thumbJointDist = Vector2.Distance(landmarks[2], landmarks[17]);
        states[0] = thumbTipDist > thumbJointDist; 

        // --- 2. INDEX FINGER ---
        // Compare Distance(Wrist -> Tip) vs Distance(Wrist -> Knuckle)
        states[1] = Vector2.Distance(wrist, landmarks[8]) > Vector2.Distance(wrist, landmarks[5]);

        // --- 3. MIDDLE FINGER ---
        states[2] = Vector2.Distance(wrist, landmarks[12]) > Vector2.Distance(wrist, landmarks[9]);

        // --- 4. RING FINGER ---
        states[3] = Vector2.Distance(wrist, landmarks[16]) > Vector2.Distance(wrist, landmarks[13]);

        // --- 5. PINKY FINGER ---
        states[4] = Vector2.Distance(wrist, landmarks[20]) > Vector2.Distance(wrist, landmarks[17]);

        return states;
    }

    public static Vector2 GetHandCenter(List<Vector2> landmarks)
    {
        if (landmarks == null || landmarks.Count == 0) return Vector2.zero;
        // Usually, the Knuckle (9) is a good approximation of the hand center
        return landmarks[9];
    }
}