using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace StreamHelper.Shared.Network;

public static class NetworkUtils
{
    public static bool IsUdpPortInUse(int port)
    {
        if (port is < 1 or > 65535) return true;

        try
        {
            var ipGlobalProperties = IPGlobalProperties.GetIPGlobalProperties();
            var udpListeners = ipGlobalProperties.GetActiveUdpListeners();
            if (udpListeners.Any(endpoint => endpoint.Port == port))
            {
                return true;
            }
        }
        catch
        {
        }

        try
        {
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            socket.ExclusiveAddressUse = true;
            socket.Bind(new IPEndPoint(IPAddress.Any, port));
            return false;
        }
        catch (SocketException ex) when (ex.SocketErrorCode == SocketError.AddressAlreadyInUse || ex.SocketErrorCode == SocketError.AccessDenied)
        {
            return true;
        }
        catch
        {
            return true;
        }
    }

    public static string GetLocalHostName()
    {
        try
        {
            return Dns.GetHostName();
        }
        catch
        {
            return "localhost";
        }
    }
}
