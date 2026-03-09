using UnityEngine;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;

public class NetUI : MonoBehaviour
{
    // 🔴 PUT YOUR HOST MACHINE IP HERE
    private string hostIP = "192.168.1.23";  

    public void StartHost()
    {
        Debug.Log("Starting Host...");
        NetworkManager.Singleton.StartHost();
    }

    public void StartClient()
    {
        Debug.Log("Starting Client...");

        var utp = NetworkManager.Singleton.GetComponent<UnityTransport>();
        utp.ConnectionData.Address = hostIP;
        utp.ConnectionData.Port = 7777;

        NetworkManager.Singleton.StartClient();
    }
}