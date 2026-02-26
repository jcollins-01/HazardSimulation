using UnityEngine;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;

public class NetDebugUI : MonoBehaviour
{
    public string address = "127.0.0.1";
    public ushort port = 7777;

    void OnGUI()
    {

        var nm = NetworkManager.Singleton;
        GUILayout.BeginArea(new Rect(20, 20, 260, 80), GUI.skin.box);
        GUILayout.Label(nm ? "NGO Debug (NetworkManager found)" : "NGO Debug (NO NetworkManager in scene)");
        GUILayout.EndArea();
        if (!nm) return;

        GUILayout.BeginArea(new Rect(Screen.width / 2 - 130, 20, 260, 160), GUI.skin.box);
        GUILayout.Label("NGO Debug");

        address = GUILayout.TextField(address);
        ushort.TryParse(GUILayout.TextField(port.ToString()), out port);

        var utp = nm.GetComponent<UnityTransport>();
        if (utp) utp.SetConnectionData(address, port);

        if (!nm.IsListening)
        {
            if (GUILayout.Button("Start Host")) nm.StartHost();
            if (GUILayout.Button("Start Client")) nm.StartClient();
        }
        else
        {
            if (GUILayout.Button("Shutdown")) nm.Shutdown();
        }

        GUILayout.EndArea();
    }
}