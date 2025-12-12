using UnityEngine;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

public class OpenCVConnector : MonoBehaviour
{
    // Singleton instance for easy access
    public static OpenCVConnector Instance;

    // The calculated data that LessonController reads
    public bool[] currentFingerStates = new bool[5];
    public Vector2 currentHandPosition = Vector2.zero;

    [Header("UDP Settings")]
    [Tooltip("UDP port Unity listens on. Must match Python --port")]
    public int listenPort = 5005;

    [Tooltip("Local address to bind. Use 0.0.0.0 to listen on all interfaces")]
    public string listenAddress = "0.0.0.0";

    [Tooltip("If no packets arrive for this time, data is considered stale")]
    public float staleTimeoutSeconds = 0.75f;

    [Header("Latest Prediction")]
    public bool handDetected;
    public string currentLetter = "";
    [Range(0f, 1f)]
    public float currentConfidence;

    private UdpClient _udp;
    private Thread _receiveThread;
    private volatile bool _isReceiving;
    private readonly ConcurrentQueue<byte[]> _pendingPackets = new ConcurrentQueue<byte[]>();
    private float _lastPacketTime;

    [Serializable]
    private class HandTrackerMessage
    {
        public bool hand_detected;
        public float[] landmarks;
        public string letter;
        public float confidence;
        public double timestamp;
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    private void OnEnable()
    {
        StartUdp();
    }

    private void OnDisable()
    {
        StopUdp();
        if (Instance == this) Instance = null;
    }

    private void StartUdp()
    {
        if (_isReceiving) return;

        try
        {
            var ip = IPAddress.Parse(string.IsNullOrWhiteSpace(listenAddress) ? "0.0.0.0" : listenAddress);
            _udp = new UdpClient(new IPEndPoint(ip, listenPort));
            _udp.Client.ReceiveTimeout = 1000; // lets thread exit reasonably quickly

            _isReceiving = true;
            _receiveThread = new Thread(ReceiveLoop)
            {
                IsBackground = true,
                Name = "OpenCVConnector-UDP"
            };
            _receiveThread.Start();
        }
        catch (Exception ex)
        {
            Debug.LogError($"[OpenCVConnector] Failed to start UDP listener on {listenAddress}:{listenPort} - {ex.Message}");
            _isReceiving = false;
        }
    }

    private void StopUdp()
    {
        _isReceiving = false;
        try
        {
            _udp?.Close();
            _udp?.Dispose();
        }
        catch { }
        _udp = null;

        try
        {
            if (_receiveThread != null && _receiveThread.IsAlive)
                _receiveThread.Join(250);
        }
        catch { }

        _receiveThread = null;
    }

    private void ReceiveLoop()
    {
        var remote = new IPEndPoint(IPAddress.Any, 0);
        while (_isReceiving)
        {
            try
            {
                byte[] data = _udp.Receive(ref remote);
                if (data != null && data.Length > 0)
                    _pendingPackets.Enqueue(data);
            }
            catch (SocketException)
            {
                // ReceiveTimeout or socket closed
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch
            {
                // Ignore malformed packets / transient errors
            }
        }
    }

    void Update()
    {
        // Drain pending UDP packets and apply the most recent valid message.
        while (_pendingPackets.TryDequeue(out var packet))
        {
            TryApplyPacket(packet);
        }

        // Mark data as stale if packets stop arriving.
        if (_lastPacketTime > 0f && (Time.realtimeSinceStartup - _lastPacketTime) > staleTimeoutSeconds)
        {
            handDetected = false;
            currentLetter = "";
            currentConfidence = 0f;
            currentFingerStates = new bool[5];
            currentHandPosition = Vector2.zero;
            _lastPacketTime = 0f;
        }
    }

    private void TryApplyPacket(byte[] packet)
    {
        string json;
        try
        {
            json = Encoding.UTF8.GetString(packet);
        }
        catch
        {
            return;
        }

        HandTrackerMessage msg;
        try
        {
            msg = JsonUtility.FromJson<HandTrackerMessage>(json);
        }
        catch
        {
            return;
        }

        if (msg == null) return;

        _lastPacketTime = Time.realtimeSinceStartup;
        handDetected = msg.hand_detected;
        currentLetter = msg.letter ?? "";
        currentConfidence = Mathf.Clamp01(msg.confidence);

        if (!handDetected || msg.landmarks == null || msg.landmarks.Length < 63)
        {
            currentFingerStates = new bool[5];
            currentHandPosition = Vector2.zero;
            return;
        }

        // Convert 21 (x,y,z) points into 21 Vector2 points.
        var points = new List<Vector2>(21);
        for (int i = 0; i + 2 < msg.landmarks.Length && points.Count < 21; i += 3)
        {
            float x = msg.landmarks[i];
            float y = 1f - msg.landmarks[i + 1]; // MediaPipe/OpenCV y grows downward; Unity gameplay expects upward
            points.Add(new Vector2(x, y));
        }

        if (points.Count == 21)
        {
            currentFingerStates = HandMath.AnalyzeFingers(points);
            currentHandPosition = HandMath.GetHandCenter(points);
        }
    }
}