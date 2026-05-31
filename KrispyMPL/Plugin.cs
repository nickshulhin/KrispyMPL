using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Text;
using System.Threading;
using UnityEngine;

namespace KrispyMPL
{
    [KSPAddon(KSPAddon.Startup.Instantly, true)]
    public class KerbalMultiplayerPlugin : MonoBehaviour
    {
        private const float UPDATE_INTERVAL = 0.5f;
        private const string CONFIG_PATH = "GameData/KrispyMPL/config.txt";

        private Rect _windowRect = new Rect(20, 20, 320, 230);
        private MultiplayerClient _client;
        private ConcurrentQueue<string> _incomingMessages = new ConcurrentQueue<string>();
        private volatile bool _connected;
        private float _lastPositionUpdate;
        private string _playerName;
        private string _serverHost = "localhost";
        private string _serverPort = "8080";
        private string _serverPassword = "";
        private bool _useTls;
        private string _roomId = "";
        private int _roomCount;
        private string _statusMessage;
        private bool _showConfig;
        private Dictionary<string, RemotePlayer> _remotePlayers = new Dictionary<string, RemotePlayer>();

        private class RemotePlayer
        {
            public string Name;
            public Vector3 Position;
            public string Body;
        }

        public void Awake()
        {
            DontDestroyOnLoad(this);
            Debug.Log("[KrispyMPL] Awake");
            LoadConfig();
            if (string.IsNullOrEmpty(_playerName) || _playerName.StartsWith("Player_"))
                _playerName = GetSteamName();
            GameEvents.onGameSceneLoadRequested.Add(OnSceneChange);
        }

        private static string GetSteamName()
        {
            try
            {
                var name = Steamworks.SteamFriends.GetPersonaName();
                if (!string.IsNullOrEmpty(name))
                    return name;
            }
            catch { }
            return "Player_" + UnityEngine.Random.Range(1000, 9999);
        }

        public void OnDestroy()
        {
            GameEvents.onGameSceneLoadRequested.Remove(OnSceneChange);
            Disconnect();
        }

        private void OnSceneChange(GameScenes scene)
        {
            _showConfig = false;
            if (scene == GameScenes.MAINMENU || scene == GameScenes.SETTINGS || scene == GameScenes.CREDITS)
                Disconnect();
        }

        private bool WindowVisible()
        {
            var scene = HighLogic.LoadedScene;
            return scene == GameScenes.SPACECENTER ||
                   scene == GameScenes.FLIGHT ||
                   scene == GameScenes.TRACKSTATION ||
                   scene == GameScenes.EDITOR;
        }

        private void ConnectToServer()
        {
            if (!int.TryParse(_serverPort, out int port))
            {
                _statusMessage = "Invalid port number";
                return;
            }

            _client = new MultiplayerClient();
            _client.OnMessage += (msg) => _incomingMessages.Enqueue(msg);
            _client.OnDisconnected += () => { _connected = false; _statusMessage = "Disconnected"; };

            try
            {
                _client.Connect(_serverHost, port, _useTls);
                _connected = true;
                _statusMessage = "Connected";
                _client.Send($"{{\"type\":\"join\",\"name\":\"{_playerName}\",\"password\":\"{_serverPassword}\"}}");
                SaveConfig();
                Debug.Log($"[KrispyMPL] Connected to {_serverHost}:{port} as {_playerName}");
            }
            catch (Exception ex)
            {
                _statusMessage = $"Error: {ex.Message}";
                Debug.LogError($"[KrispyMPL] Failed to connect: {ex.Message}");
            }
        }

        private void Disconnect()
        {
            if (_client != null)
            {
                try { _client.Send("{\"type\":\"leave\"}"); } catch { }
                _client.Close();
                _client = null;
            }
            _connected = false;
            _statusMessage = "Disconnected";
            _roomId = "";
            _roomCount = 0;
            _remotePlayers.Clear();
        }

        public void Update()
        {
            ProcessMessages();

            if (!WindowVisible()) return;

            if (_connected && HighLogic.LoadedScene == GameScenes.FLIGHT &&
                Time.time - _lastPositionUpdate > UPDATE_INTERVAL)
                SendPosition();
        }

        private void ProcessMessages()
        {
            while (_incomingMessages.TryDequeue(out string msg))
            {
                try
                {
                    var dict = ParseJson(msg);
                    if (dict == null) continue;

                    string type = GetString(dict, "type");

                    switch (type)
                    {
                        case "joined":
                            _roomId = GetString(dict, "roomId") ?? "";
                            _roomCount = GetInt(dict, "roomCount");
                            Debug.Log($"[KrispyMPL] Joined room {_roomId}, {_roomCount} rooms total");
                            break;

                        case "player_update":
                            var players = GetList(dict, "players");
                            if (players != null)
                            {
                                foreach (var p in players)
                                {
                                    var pd = p as Dictionary<string, object>;
                                    if (pd == null) continue;
                                    string name = GetString(pd, "name");
                                    if (name == _playerName) continue;

                                    if (!_remotePlayers.TryGetValue(name, out var rp))
                                    {
                                        rp = new RemotePlayer { Name = name };
                                        _remotePlayers[name] = rp;
                                    }
                                    rp.Position = new Vector3(
                                        GetFloat(pd, "x"), GetFloat(pd, "y"), GetFloat(pd, "z"));
                                    rp.Body = GetString(pd, "body");
                                }
                            }
                            break;

                        case "player_left":
                            string leftName = GetString(dict, "name");
                            if (leftName != null && _remotePlayers.ContainsKey(leftName))
                            {
                                _remotePlayers.Remove(leftName);
                                Debug.Log($"[KrispyMPL] Player left: {leftName}");
                            }
                            break;

                        case "auth_error":
                            _statusMessage = "Auth error: " + (GetString(dict, "name") ?? "Unknown");
                            Disconnect();
                            break;
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[KrispyMPL] Message error: {ex.Message}");
                }
            }
        }

        private void SendPosition()
        {
            _lastPositionUpdate = Time.time;
            if (FlightGlobals.ActiveVessel == null) return;

            var vessel = FlightGlobals.ActiveVessel;
            var pos = vessel.transform.position;
            string msg = $"{{\"type\":\"pos\",\"x\":{pos.x:F2},\"y\":{pos.y:F2},\"z\":{pos.z:F2},\"body\":\"{vessel.mainBody.name}\"}}";
            _client.Send(msg);
        }

        public void OnGUI()
        {
            GUI.skin = HighLogic.Skin;

            if (!_showConfig && WindowVisible())
            {
                float btnWidth = 90f;
                float btnHeight = 30f;
                Rect btnRect = new Rect(Screen.width - btnWidth - 10, Screen.height - btnHeight - 100, btnWidth, btnHeight);
                if (GUI.Button(btnRect, "KrispyMPL"))
                    _showConfig = true;
            }

            if (_showConfig && WindowVisible())
            {
                _windowRect = GUILayout.Window(424242, _windowRect, DrawWindow, "Krispy Multiplayer");
            }
        }

        private void DrawWindow(int windowId)
        {
            string status = _connected ? "<color=green>Connected</color>" : "<color=red>Disconnected</color>";

            GUILayout.Label($"Status: {status}");
            GUILayout.Label($"Name: {_playerName}");
            if (_connected)
                GUILayout.Label($"Room: {_roomId}  |  Active rooms: {_roomCount}");

            if (!_connected)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("Host:", GUILayout.Width(40));
                _serverHost = GUILayout.TextField(_serverHost, GUILayout.Width(140));
                GUILayout.Label("Port:", GUILayout.Width(35));
                _serverPort = GUILayout.TextField(_serverPort, GUILayout.Width(60));
                GUILayout.EndHorizontal();
                GUILayout.BeginHorizontal();
                GUILayout.Label("Pass:", GUILayout.Width(40));
                _serverPassword = GUILayout.PasswordField(_serverPassword, '*', GUILayout.Width(190));
                GUILayout.EndHorizontal();
                _useTls = GUILayout.Toggle(_useTls, " Use WSS (secure)");
            }

            if (!string.IsNullOrEmpty(_statusMessage) && !_connected)
                GUILayout.Label($"<color=yellow>{_statusMessage}</color>");

            GUILayout.Label($"Online: {_remotePlayers.Count}");

            if (!_connected)
            {
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Join Room"))
                    ConnectToServer();
                if (GUILayout.Button("Create Room"))
                    ConnectToServer();
                GUILayout.EndHorizontal();
            }
            else
            {
                if (GUILayout.Button("Disconnect"))
                    Disconnect();
            }
            if (GUILayout.Button("Close"))
                _showConfig = false;

            foreach (var kvp in _remotePlayers)
            {
                var rp = kvp.Value;
                GUILayout.Label($"  {rp.Name} - {rp.Body} ({rp.Position.x:F0}, {rp.Position.y:F0}, {rp.Position.z:F0})");
            }

            GUI.DragWindow();
        }

        public void OnRenderObject()
        {
            if (!WindowVisible() || _remotePlayers.Count == 0) return;

            foreach (var kvp in _remotePlayers)
            {
                var rp = kvp.Value;
                DrawMarker(rp.Position, Color.green);
            }
        }

        private static void DrawMarker(Vector3 pos, Color color)
        {
            GL.PushMatrix();
            GL.Begin(GL.LINES);
            GL.Color(color);

            float size = 2f;
            GL.Vertex(pos + Vector3.up * size);
            GL.Vertex(pos - Vector3.up * size);
            GL.Vertex(pos + Vector3.right * size);
            GL.Vertex(pos - Vector3.right * size);
            GL.Vertex(pos + Vector3.forward * size);
            GL.Vertex(pos - Vector3.forward * size);

            GL.End();
            GL.PopMatrix();
        }

        #region Config Persistence

        private void LoadConfig()
        {
            try
            {
                string fullPath = System.IO.Path.Combine(KSPUtil.ApplicationRootPath, CONFIG_PATH);
                if (System.IO.File.Exists(fullPath))
                {
                    string[] lines = System.IO.File.ReadAllLines(fullPath);
                    foreach (var line in lines)
                    {
                        int eq = line.IndexOf('=');
                        if (eq > 0)
                        {
                            string key = line.Substring(0, eq).Trim();
                            string val = line.Substring(eq + 1).Trim();
                            if (key == "host") _serverHost = val;
                            else if (key == "port") _serverPort = val;
                            else if (key == "password") _serverPassword = val;
                            else if (key == "tls") _useTls = val == "true";
                            else if (key == "name") _playerName = val;
                        }
                    }
                    Debug.Log($"[KrispyMPL] Loaded config: {_serverHost}:{_serverPort} as {_playerName}");
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[KrispyMPL] Failed to load config: {ex.Message}");
            }
        }

        private void SaveConfig()
        {
            try
            {
                string fullPath = System.IO.Path.Combine(KSPUtil.ApplicationRootPath, CONFIG_PATH);
                string dir = System.IO.Path.GetDirectoryName(fullPath);
                if (!System.IO.Directory.Exists(dir))
                    System.IO.Directory.CreateDirectory(dir);
                System.IO.File.WriteAllText(fullPath,
                    $"host={_serverHost}\nport={_serverPort}\npassword={_serverPassword}\ntls={_useTls}\nname={_playerName}\n");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[KrispyMPL] Failed to save config: {ex.Message}");
            }
        }

        #endregion

        #region Minimal JSON Parser

        private static Dictionary<string, object> ParseJson(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            json = json.Trim();
            if (!json.StartsWith("{")) return null;

            var result = new Dictionary<string, object>();
            int i = 1;
            while (i < json.Length)
            {
                while (i < json.Length && (json[i] == ' ' || json[i] == ',' || json[i] == '\n' || json[i] == '\r')) i++;
                if (i >= json.Length || json[i] == '}') break;

                if (json[i] != '"') { i++; continue; }
                int keyEnd = json.IndexOf('"', i + 1);
                if (keyEnd < 0) break;
                string key = json.Substring(i + 1, keyEnd - i - 1);
                i = keyEnd + 1;

                while (i < json.Length && (json[i] == ' ' || json[i] == ':')) i++;
                if (i >= json.Length) break;

                switch (json[i])
                {
                    case '"':
                        int strEnd = json.IndexOf('"', i + 1);
                        if (strEnd < 0) { i++; continue; }
                        result[key] = json.Substring(i + 1, strEnd - i - 1);
                        i = strEnd + 1;
                        break;
                    case '{':
                        int objDepth = 1;
                        int objStart = i;
                        i++;
                        while (i < json.Length && objDepth > 0)
                        {
                            if (json[i] == '{') objDepth++;
                            else if (json[i] == '}') objDepth--;
                            i++;
                        }
                        result[key] = json.Substring(objStart, i - objStart);
                        break;
                    case '[':
                        int arrDepth = 1;
                        int arrStart = i;
                        i++;
                        while (i < json.Length && arrDepth > 0)
                        {
                            if (json[i] == '[') arrDepth++;
                            else if (json[i] == ']') arrDepth--;
                            i++;
                        }
                        string arrJson = json.Substring(arrStart, i - arrStart);
                        result[key] = ParseArray(arrJson);
                        break;
                    default:
                        int numEnd = i;
                        while (numEnd < json.Length && json[numEnd] != ',' && json[numEnd] != '}' && json[numEnd] != ' ' && json[numEnd] != '\n' && json[numEnd] != '\r')
                            numEnd++;
                        string numStr = json.Substring(i, numEnd - i);
                        if (double.TryParse(numStr, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double d))
                            result[key] = d;
                        else
                            result[key] = numStr;
                        i = numEnd;
                        break;
                }
            }
            return result;
        }

        private static List<object> ParseArray(string json)
        {
            var list = new List<object>();
            json = json.Trim();
            if (!json.StartsWith("[")) return list;

            int i = 1;
            while (i < json.Length)
            {
                while (i < json.Length && (json[i] == ' ' || json[i] == ',' || json[i] == '\n' || json[i] == '\r')) i++;
                if (i >= json.Length || json[i] == ']') break;

                if (json[i] == '{')
                {
                    int depth = 1;
                    int start = i;
                    i++;
                    while (i < json.Length && depth > 0)
                    {
                        if (json[i] == '{') depth++;
                        else if (json[i] == '}') depth--;
                        i++;
                    }
                    var obj = ParseJson(json.Substring(start, i - start));
                    if (obj != null) list.Add(obj);
                }
                else if (json[i] == '"')
                {
                    int end = json.IndexOf('"', i + 1);
                    if (end > 0) { list.Add(json.Substring(i + 1, end - i - 1)); i = end + 1; }
                    else i++;
                }
                else
                {
                    int end = i;
                    while (end < json.Length && json[end] != ',' && json[end] != ']' && json[end] != ' ' && json[end] != '\n' && json[end] != '\r')
                        end++;
                    string val = json.Substring(i, end - i);
                    if (double.TryParse(val, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double d))
                        list.Add(d);
                    else
                        list.Add(val);
                    i = end;
                }
            }
            return list;
        }

        private static string GetString(Dictionary<string, object> dict, string key)
        {
            if (dict.TryGetValue(key, out var val))
                return val?.ToString();
            return null;
        }

        private static List<object> GetList(Dictionary<string, object> dict, string key)
        {
            if (dict.TryGetValue(key, out var val) && val is List<object> list)
                return list;
            return null;
        }

        private static int GetInt(Dictionary<string, object> dict, string key)
        {
            if (dict.TryGetValue(key, out var val) && val is double d)
                return (int)d;
            return 0;
        }

        private static float GetFloat(Dictionary<string, object> dict, string key)
        {
            if (dict.TryGetValue(key, out var val) && val is double d)
                return (float)d;
            return 0f;
        }

        #endregion
    }

    public class MultiplayerClient
    {
        public event Action<string> OnMessage;
        public event Action OnDisconnected;

        private TcpClient _tcp;
        private Stream _stream;
        private Thread _readThread;
        private volatile bool _running;
        private readonly object _sendLock = new object();
        private string _wsKey;

        public void Connect(string host, int port, bool useTls = false)
        {
            _tcp = new TcpClient();
            _tcp.Connect(host, port);
            _stream = _tcp.GetStream();

            if (useTls)
            {
                var ssl = new SslStream(_stream, false);
                ssl.AuthenticateAsClient(host);
                _stream = ssl;
            }

            _wsKey = Convert.ToBase64String(Guid.NewGuid().ToByteArray());
            string upgrade = $"GET /ws HTTP/1.1\r\n" +
                            $"Host: {host}:{port}\r\n" +
                            "Upgrade: websocket\r\n" +
                            "Connection: Upgrade\r\n" +
                            $"Sec-WebSocket-Key: {_wsKey}\r\n" +
                            "Sec-WebSocket-Version: 13\r\n" +
                            "\r\n";

            byte[] req = Encoding.ASCII.GetBytes(upgrade);
            _stream.Write(req, 0, req.Length);

            byte[] respBuf = new byte[1024];
            int read = _stream.Read(respBuf, 0, respBuf.Length);
            string resp = Encoding.ASCII.GetString(respBuf, 0, read);
            if (!resp.Contains("101"))
                throw new Exception($"WebSocket handshake failed: {resp}");

            _running = true;
            _readThread = new Thread(ReadLoop);
            _readThread.IsBackground = true;
            _readThread.Start();
        }

        public void Send(string message)
        {
            if (_stream == null || !_running) return;
            var data = Encoding.UTF8.GetBytes(message);
            lock (_sendLock)
            {
                SendFrame(data, _stream);
            }
        }

        public void Close()
        {
            _running = false;
            try { _stream?.Close(); } catch { }
            try { _tcp?.Close(); } catch { }
            _readThread = null;
            _stream = null;
            _tcp = null;
        }

        private void ReadLoop()
        {
            var buffer = new byte[65536];
            var msgBuffer = new MemoryStream();

            try
            {
                while (_running && _stream != null)
                {
                    int bytesRead = _stream.Read(buffer, 0, buffer.Length);
                    if (bytesRead <= 0) break;

                    int offset = 0;
                    while (offset < bytesRead)
                    {
                        if (bytesRead - offset < 2) break;

                        byte opcode = (byte)(buffer[offset] & 0x0F);
                        int payloadLen = buffer[offset + 1] & 0x7F;
                        offset += 2;

                        if (payloadLen == 126)
                        {
                            if (bytesRead - offset < 2) break;
                            payloadLen = (buffer[offset] << 8) | buffer[offset + 1];
                            offset += 2;
                        }
                        else if (payloadLen == 127)
                        {
                            if (bytesRead - offset < 8) break;
                            payloadLen = (int)(((long)buffer[offset + 4] << 24) | ((long)buffer[offset + 5] << 16) | ((long)buffer[offset + 6] << 8) | buffer[offset + 7]);
                            offset += 8;
                        }

                        if (bytesRead - offset < payloadLen) break;

                        if (opcode == 0x01)
                        {
                            string msg = Encoding.UTF8.GetString(buffer, offset, payloadLen);
                            OnMessage?.Invoke(msg);
                        }

                        offset += payloadLen;
                    }
                }
            }
            catch (Exception) { }
            finally
            {
                _running = false;
                OnDisconnected?.Invoke();
            }
        }

        private static void SendFrame(byte[] data, Stream stream)
        {
            byte[] frame;
            int idx = 0;

            if (data.Length < 126)
            {
                frame = new byte[2 + data.Length];
                frame[0] = 0x81;
                frame[1] = (byte)data.Length;
                idx = 2;
            }
            else if (data.Length < 65536)
            {
                frame = new byte[4 + data.Length];
                frame[0] = 0x81;
                frame[1] = 126;
                frame[2] = (byte)((data.Length >> 8) & 0xFF);
                frame[3] = (byte)(data.Length & 0xFF);
                idx = 4;
            }
            else
            {
                frame = new byte[10 + data.Length];
                frame[0] = 0x81;
                frame[1] = 127;
                long len = data.Length;
                frame[2] = (byte)((len >> 56) & 0xFF);
                frame[3] = (byte)((len >> 48) & 0xFF);
                frame[4] = (byte)((len >> 40) & 0xFF);
                frame[5] = (byte)((len >> 32) & 0xFF);
                frame[6] = (byte)((len >> 24) & 0xFF);
                frame[7] = (byte)((len >> 16) & 0xFF);
                frame[8] = (byte)((len >> 8) & 0xFF);
                frame[9] = (byte)(len & 0xFF);
                idx = 10;
            }

            Buffer.BlockCopy(data, 0, frame, idx, data.Length);
            stream.Write(frame, 0, frame.Length);
        }
    }
}
