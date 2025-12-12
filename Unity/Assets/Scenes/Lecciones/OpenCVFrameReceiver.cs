using UnityEngine;
using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Threading;

public class OpenCVFrameReceiver : MonoBehaviour
{
    [Header("UDP Settings")]
    [Tooltip("UDP port where Python sends JPEG frames")]
    public int listenPort = 5006;

    [Tooltip("Local address to bind. Use 0.0.0.0 to listen on all interfaces")]
    public string listenAddress = "0.0.0.0";

    [Header("Runtime")]
    public Texture2D currentTexture;

    private UdpClient _udp;
    private Thread _receiveThread;
    private volatile bool _isReceiving;
    private readonly ConcurrentQueue<byte[]> _pendingFrames = new ConcurrentQueue<byte[]>();

    private void OnEnable()
    {
        StartUdp();
    }

    private void OnDisable()
    {
        StopUdp();
    }

    private void StartUdp()
    {
        if (_isReceiving) return;

        try
        {
            var ip = IPAddress.Parse(string.IsNullOrWhiteSpace(listenAddress) ? "0.0.0.0" : listenAddress);
            _udp = new UdpClient(new IPEndPoint(ip, listenPort));
            _udp.Client.ReceiveTimeout = 1000;

            _isReceiving = true;
            _receiveThread = new Thread(ReceiveLoop)
            {
                IsBackground = true,
                Name = "OpenCVFrameReceiver-UDP"
            };
            _receiveThread.Start();
        }
        catch (Exception ex)
        {
            Debug.LogError($"[OpenCVFrameReceiver] Failed to start UDP listener on {listenAddress}:{listenPort} - {ex.Message}");
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
                    _pendingFrames.Enqueue(data);
            }
            catch (SocketException)
            {
                // timeout or closed
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch
            {
                // ignore
            }
        }
    }

    private void Update()
    {
        // Apply only the latest frame available this tick
        byte[] latest = null;
        while (_pendingFrames.TryDequeue(out var frame))
            latest = frame;

        if (latest == null) return;

        if (currentTexture == null)
            currentTexture = new Texture2D(2, 2, TextureFormat.RGB24, false);

        try
        {
            // latest is a JPEG byte[]
            currentTexture.LoadImage(latest, false);
        }
        catch
        {
            // ignore corrupted frames
        }
    }
}
