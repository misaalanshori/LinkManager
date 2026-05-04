using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace LinkManager.Network;

/// <summary>
/// Resolves adapter identifiers (IP, Name, or Description) to live
/// NetworkInterface objects. Called every poll tick — never caches results.
/// </summary>
public static class AdapterResolver
{
    /// <summary>
    /// Returns the current IPv4 address of the adapter matching <paramref name="identifier"/>,
    /// or null if the adapter is absent, unplugged, or still negotiating DHCP.
    /// </summary>
    public static string? ResolveIp(string identifier)
    {
        var iface = ResolveInterface(identifier);
        if (iface == null) return null;

        // If identifier is a direct IP, return it as-is (already validated in ResolveInterface)
        if (IPAddress.TryParse(identifier, out var parsedIp))
        {
            return parsedIp.ToString();
        }

        // Otherwise return the first IPv4 assigned to this interface
        return GetFirstIpv4(iface);
    }

    /// <summary>
    /// Returns the live <see cref="NetworkInterface"/> matching <paramref name="identifier"/>,
    /// or null if not found or not Up.
    /// </summary>
    public static NetworkInterface? ResolveInterface(string identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier)) return null;

        bool isDirectIp = IPAddress.TryParse(identifier, out var parsedIp);

        foreach (var iface in NetworkInterface.GetAllNetworkInterfaces())
        {
            // Skip adapters that are not operational
            if (iface.OperationalStatus != OperationalStatus.Up) continue;

            var props = iface.GetIPProperties();

            if (isDirectIp)
            {
                // Match by IP: check if this adapter currently holds the given IP
                foreach (var unicast in props.UnicastAddresses)
                {
                    if (unicast.Address.Equals(parsedIp))
                        return iface;
                }
            }
            else
            {
                // Match by Name (exact) or Description (contains, case-insensitive)
                bool nameMatch = iface.Name.Equals(identifier, StringComparison.OrdinalIgnoreCase);
                bool descMatch = iface.Description.Contains(identifier, StringComparison.OrdinalIgnoreCase);

                if (nameMatch || descMatch)
                    return iface;
            }
        }

        return null;
    }

    /// <summary>
    /// Returns the IPv4 interface index used by netsh commands, or -1 if not found.
    /// </summary>
    public static int ResolveInterfaceIndex(string identifier)
    {
        var iface = ResolveInterface(identifier);
        if (iface == null) return -1;

        try
        {
            return iface.GetIPProperties().GetIPv4Properties()?.Index ?? -1;
        }
        catch
        {
            return -1;
        }
    }

    /// <summary>
    /// Returns the first IPv4 unicast address from an interface, or null.
    /// </summary>
    private static string? GetFirstIpv4(NetworkInterface iface)
    {
        foreach (var unicast in iface.GetIPProperties().UnicastAddresses)
        {
            if (unicast.Address.AddressFamily == AddressFamily.InterNetwork)
                return unicast.Address.ToString();
        }
        return null;
    }
}
