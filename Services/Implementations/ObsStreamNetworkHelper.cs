using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace ChyguiSlide.Services.Implementations;

internal static class ObsStreamNetworkHelper
{
    /// <summary>Лучший IPv4 для доступа из LAN (не loopback, не link-local).</summary>
    public static string? GetPreferredLanIPv4()
    {
        foreach (var ip in GetOutboundInterfaceCandidates())
        {
            if (IsUsableLanIPv4(ip))
            {
                return ip;
            }
        }

        return GetAllLanIPv4().FirstOrDefault();
    }

    public static IReadOnlyList<string> GetAllLanIPv4()
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<string>();

        void Add(string? ip)
        {
            if (ip is null || !IsUsableLanIPv4(ip) || !seen.Add(ip))
            {
                return;
            }

            result.Add(ip);
        }

        foreach (var ip in GetOutboundInterfaceCandidates())
        {
            Add(ip);
        }

        try
        {
            var host = Dns.GetHostName();
            foreach (var address in Dns.GetHostAddresses(host))
            {
                if (address.AddressFamily == AddressFamily.InterNetwork)
                {
                    Add(address.ToString());
                }
            }
        }
        catch
        {
            // ignore
        }

        foreach (var networkInterface in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (networkInterface.OperationalStatus != OperationalStatus.Up)
            {
                continue;
            }

            if (networkInterface.NetworkInterfaceType is NetworkInterfaceType.Loopback)
            {
                continue;
            }

            foreach (var unicast in networkInterface.GetIPProperties().UnicastAddresses)
            {
                if (unicast.Address.AddressFamily == AddressFamily.InterNetwork)
                {
                    Add(unicast.Address.ToString());
                }
            }
        }

        return result;
    }

    private static IEnumerable<string> GetOutboundInterfaceCandidates()
    {
        // Маршрут «наружу» — обычно тот же интерфейс, что и для LAN
        foreach (var target in new[] { "8.8.8.8", "1.1.1.1", "192.168.1.1" })
        {
            string? ip = null;
            try
            {
                using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                socket.Connect(target, 65530);
                if (socket.LocalEndPoint is IPEndPoint ep)
                {
                    ip = ep.Address.ToString();
                }
            }
            catch
            {
                // try next target
            }

            if (ip is not null)
            {
                yield return ip;
            }
        }
    }

    internal static bool IsUsableLanIPv4(string ip)
    {
        if (!IPAddress.TryParse(ip, out var address))
        {
            return false;
        }

        if (address.AddressFamily != AddressFamily.InterNetwork)
        {
            return false;
        }

        var bytes = address.GetAddressBytes();
        if (bytes[0] == 127)
        {
            return false;
        }

        // Link-local APIPA
        if (bytes[0] == 169 && bytes[1] == 254)
        {
            return false;
        }

        return true;
    }
}
