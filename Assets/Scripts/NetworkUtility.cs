using Unity.Netcode;

public class NetworkUtility
{
    public static int GetLocalTick()
    {
        return NetworkManager.Singleton.NetworkTickSystem.LocalTime.Tick;
    }
    
    public static uint GetLocalTickRate()
    {
        return NetworkManager.Singleton.NetworkTickSystem.LocalTime.TickRate;
    }

    public static ulong GetCurrentRtt(ulong clientId)
    {
        return NetworkManager.Singleton.NetworkConfig.NetworkTransport.GetCurrentRtt(clientId);
    }
}