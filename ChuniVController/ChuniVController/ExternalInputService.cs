using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Text.RegularExpressions;

namespace ChuniVController
{
    // Simple UDP JSON listener for external input (e.g., hand-tracking server).
    // Expected JSON messages: { "zone": 0..5, "state": "blocked"|"unblocked" }
    public class ExternalInputService : IDisposable
    {
        private readonly ChuniIO _io;
        private readonly int _port;
        private UdpClient _udp;
        private CancellationTokenSource _cts;
        private bool[] _irBlocked = new bool[6];

        public ExternalInputService(ChuniIO io, int port = 24865)
        {
            _io = io;
            _port = port;
        }

        public bool Start()
        {
            try
            {
                _udp = new UdpClient(_port);
                _cts = new CancellationTokenSource();
                Task.Run(() => ListenLoop(_cts.Token));
                Console.WriteLine($"External input listener started on UDP port {_port}");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine("Failed to start external input listener: " + ex.Message);
                return false;
            }
        }

        public void Stop()
        {
            try
            {
                _cts?.Cancel();
                _udp?.Close();
                _udp = null;
                Console.WriteLine("External input listener stopped");
            }
            catch { }
        }

        private async Task ListenLoop(CancellationToken token)
        {
            IPEndPoint remote = new IPEndPoint(IPAddress.Any, 0);
            while (!token.IsCancellationRequested)
            {
                try
                {
                    var result = await _udp.ReceiveAsync().ConfigureAwait(false);
                    string text = Encoding.UTF8.GetString(result.Buffer);
                    // Very small, permissive JSON parsing to avoid adding dependencies.
                    // Expecting: { "zone": 0, "state": "blocked" }
                    var mZone = Regex.Match(text, "\"zone\"\\s*:\\s*(\\d+)");
                    var mState = Regex.Match(text, "\"state\"\\s*:\\s*\"(blocked|unblocked)\"", RegexOptions.IgnoreCase);
                    if (!mZone.Success || !mState.Success) continue;
                    int zone = int.Parse(mZone.Groups[1].Value);
                    string state = mState.Groups[1].Value.ToLowerInvariant();

                    if (zone < 0 || zone >= 6) continue;

                    if (state == "blocked" && !_irBlocked[zone])
                    {
                        _irBlocked[zone] = true;
                        SendIrMessage((byte)zone, ChuniMessageTypes.IrBlocked);
                    }
                    else if (state == "unblocked" && _irBlocked[zone])
                    {
                        _irBlocked[zone] = false;
                        SendIrMessage((byte)zone, ChuniMessageTypes.IrUnblocked);
                    }
                }
                catch (ObjectDisposedException) { break; }
                catch (Exception ex)
                {
                    Console.WriteLine("External input receive error: " + ex.Message);
                    await Task.Delay(100, token).ConfigureAwait(false);
                }
            }
        }

        private void SendIrMessage(byte target, ChuniMessageTypes type)
        {
            var message = new ChuniIoMessage
            {
                Source = (byte)ChuniMessageSources.Controller,
                Type = (byte)type,
                Target = target
            };
            _io.Send(message);
            Console.WriteLine($"IR {target} {(type == ChuniMessageTypes.IrBlocked ? "BLOCKED" : "UNBLOCKED")}");
        }

        public void Dispose()
        {
            Stop();
        }
    }
}
