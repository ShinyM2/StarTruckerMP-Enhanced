using Microsoft.AspNetCore.Mvc;
using StarTruckMP.Server.Server.Services;
using StarTruckMP.Shared.Dto.Api;

namespace StarTruckMP.Server.Controllers;

/// <summary>
/// The lobby line: how many are on the server and who, for a client that has not signed in yet.
///
/// No token is asked for on purpose. The menu polls this from the main menu, before the game
/// has loaded a save and joined, so a player can see that the server answers and that their
/// friends are already on it — the difference between "nothing happens" and "it works, load
/// a save". Names and sectors are what everyone on the server sees anyway.
/// </summary>
[ApiController]
[Route("api/status")]
public sealed class StatusController : ControllerBase
{
    private readonly ServerSettings _settings;
    private readonly PlayerContainer _players;

    public StatusController(ServerSettings settings, PlayerContainer players)
    {
        _settings = settings;
        _players = players;
    }

    [HttpGet("")]
    [ProducesResponseType<ServerStatusDto>(StatusCodes.Status200OK)]
    public IActionResult Get()
    {
        var joined = _players.SnapshotPlayers()
            .Where(p => p.HandshakeCompleted)
            .OrderBy(p => p.Id)
            .ToArray();

        var status = new ServerStatusDto
        {
            Version = typeof(StatusController).Assembly.GetName().Version?.ToString(3),
            Players = joined.Length,
            MaxPlayers = _settings.MaxPlayers
        };

        foreach (var p in joined)
        {
            status.Names.Add(new ServerStatusPlayerDto
            {
                Name = string.IsNullOrWhiteSpace(p.Name) ? $"Player #{p.Id}" : p.Name,
                Sector = p.Sector
            });
        }

        return Ok(status);
    }
}
