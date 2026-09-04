using System.Collections.Generic;

namespace StarTruckMP.Shared.Dto.Api;

/// <summary>
/// What a server says about itself to anyone who asks, before they sign in: enough for a menu
/// to show that the server is there and who is on it. Served at <c>GET /api/status</c>.
/// </summary>
public class ServerStatusDto
{
    public string? Version { get; set; }

    /// <summary>Players who have completed the handshake, i.e. are really in.</summary>
    public int Players { get; set; }

    public int MaxPlayers { get; set; }

    public List<ServerStatusPlayerDto> Names { get; set; } = new();
}

public class ServerStatusPlayerDto
{
    public string Name { get; set; } = string.Empty;
    public string Sector { get; set; } = string.Empty;
}
