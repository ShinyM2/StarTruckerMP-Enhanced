using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using StarTruckMP.Server.Server;
using StarTruckMP.Server.Server.Services;

namespace StarTruckMP.Server.Controllers;

/// <summary>
/// A small page for whoever runs the server: who is on, where, with what ping; the chat; the
/// log; and a kick button. One HTML page with a little script that polls the JSON below it.
///
/// Guarded by HTTP Basic auth against <c>ApiAdminUsername</c> / <c>ApiAdminPassword</c> from
/// server.json, and refused outright while the password is still the default — a page that can
/// kick players must not open on a word everyone knows.
/// </summary>
[ApiController]
[Route("admin")]
public sealed class AdminController : ControllerBase
{
    private readonly ServerSettings _settings;
    private readonly PlayerContainer _players;
    private readonly ServerManager _server;

    public AdminController(ServerSettings settings, PlayerContainer players, ServerManager server)
    {
        _settings = settings;
        _players = players;
        _server = server;
    }

    [HttpGet("")]
    public IActionResult Page()
    {
        if (Guard() is { } refusal) return refusal;
        return Content(Html, "text/html; charset=utf-8");
    }

    [HttpGet("api/state")]
    public IActionResult State()
    {
        if (Guard() is { } refusal) return refusal;

        var now = DateTime.UtcNow;
        var players = _players.SnapshotPlayers()
            .Where(p => p.HandshakeCompleted)
            .OrderBy(p => p.Id)
            .Select(p => new
            {
                id = p.Id,
                name = string.IsNullOrWhiteSpace(p.Name) ? $"Player #{p.Id}" : p.Name,
                sector = p.Sector,
                ping = p.Ping,
                online = (int)(now - p.ConnectedAt).TotalSeconds
            });

        return Ok(new
        {
            version = typeof(AdminController).Assembly.GetName().Version?.ToString(3),
            uptime = (int)(now - Process.GetCurrentProcess().StartTime.ToUniversalTime()).TotalSeconds,
            port = _settings.Port,
            players,
            chat = _server.RecentChat().Select(c => new { at = c.At, name = c.Name, message = c.Message, sectorOnly = c.SectorOnly }),
            log = MemoryLogSink.Instance.Lines()
        });
    }

    [HttpPost("api/kick/{id:int}")]
    public IActionResult Kick(int id)
    {
        if (Guard() is { } refusal) return refusal;

        _server.Kick(id);
        return Ok();
    }

    /// <summary>Null when the caller may proceed, otherwise the response to send instead.</summary>
    private IActionResult? Guard()
    {
        if (string.IsNullOrWhiteSpace(_settings.ApiAdminPassword) ||
            string.Equals(_settings.ApiAdminPassword, "changeme", StringComparison.Ordinal))
        {
            return StatusCode(StatusCodes.Status403Forbidden,
                "The admin page is disabled until ApiAdminPassword in server.json is changed from the default.");
        }

        if (Request.Headers.TryGetValue("Authorization", out var header))
        {
            var value = header.ToString();
            if (value.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(value[6..].Trim()));
                    var colon = decoded.IndexOf(':');
                    if (colon > 0)
                    {
                        var user = decoded[..colon];
                        var pass = decoded[(colon + 1)..];
                        if (SameString(user, _settings.ApiAdminUsername) && SameString(pass, _settings.ApiAdminPassword))
                            return null;
                    }
                }
                catch (FormatException)
                {
                    // Not base64; fall through to the challenge.
                }
            }
        }

        Response.Headers.WWWAuthenticate = "Basic realm=\"StarTruckMP\", charset=\"UTF-8\"";
        return Unauthorized();
    }

    private static bool SameString(string a, string b)
    {
        var x = Encoding.UTF8.GetBytes(a);
        var y = Encoding.UTF8.GetBytes(b);
        return x.Length == y.Length && CryptographicOperations.FixedTimeEquals(x, y);
    }

    private const string Html = """
<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<title>StarTruckMP server</title>
<meta name="viewport" content="width=device-width, initial-scale=1">
<style>
  :root { --amber:#efc806; --text:#e9e7de; --muted:#9b978a; --panel:rgba(10,12,15,.9); --line:rgba(239,200,6,.35); }
  body { margin:0; background:#0b0d10; color:var(--text); font:14px/1.45 Bahnschrift,"Segoe UI",system-ui,sans-serif; }
  header { display:flex; gap:18px; align-items:baseline; padding:14px 20px; border-bottom:1px solid var(--line); }
  h1 { margin:0; font-size:15px; letter-spacing:.18em; text-transform:uppercase; color:var(--amber); }
  header span { color:var(--muted); font-size:12px; }
  main { display:grid; grid-template-columns: 1.1fr 1fr; gap:16px; padding:16px 20px; }
  section { background:var(--panel); border:1px solid var(--line); padding:12px 14px; min-height:120px; }
  section h2 { margin:0 0 8px; font-size:11px; letter-spacing:.16em; text-transform:uppercase; color:var(--amber); }
  table { width:100%; border-collapse:collapse; }
  td, th { text-align:left; padding:5px 6px; border-bottom:1px solid rgba(255,255,255,.06); }
  th { color:var(--muted); font-size:11px; letter-spacing:.1em; text-transform:uppercase; font-weight:normal; }
  button { border:1px solid #d8703f; color:#d8703f; background:none; padding:3px 10px; font:inherit; font-size:11px; letter-spacing:.1em; text-transform:uppercase; cursor:pointer; }
  button:hover { background:#d8703f; color:#1a0d07; }
  pre { margin:0; max-height:360px; overflow:auto; font:12px/1.4 Consolas,monospace; white-space:pre-wrap; color:#cfcbbf; }
  .chat p { margin:0 0 4px; } .chat .who { color:var(--amber); } .chat .t { color:var(--muted); font-size:11px; margin-right:6px; }
  .log { grid-column: 1 / -1; }
  .empty { color:var(--muted); }
</style>
</head>
<body>
<header><h1>StarTruckMP server</h1><span id="meta">…</span></header>
<main>
  <section><h2>Players</h2><table><thead><tr><th>#</th><th>Name</th><th>Sector</th><th>Ping</th><th>Online</th><th></th></tr></thead><tbody id="players"></tbody></table></section>
  <section class="chat"><h2>Chat</h2><div id="chat"></div></section>
  <section class="log"><h2>Log</h2><pre id="log"></pre></section>
</main>
<script>
  const esc = s => String(s ?? '').replace(/[&<>"]/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;'}[c]));
  const dur = s => s >= 3600 ? `${Math.floor(s/3600)} h ${Math.floor(s%3600/60)} min` : s >= 60 ? `${Math.floor(s/60)} min` : `${s} s`;
  const sector = s => !s || s === 'none' ? '—' : s.replace(/^Sector[_-]?\d*[_-]?/i,'').replace(/[_-]+/g,' ').replace(/([a-z\d])([A-Z])/g,'$1 $2');
  async function kick(id) { if (!confirm('Kick player #' + id + '?')) return; await fetch('api/kick/' + id, { method: 'POST' }); refresh(); }
  async function refresh() {
    try {
      const r = await fetch('api/state', { cache: 'no-store' }); if (!r.ok) throw new Error(r.status);
      const s = await r.json();
      document.getElementById('meta').textContent = `v${s.version} · port ${s.port} · up ${dur(s.uptime)} · ${s.players.length} online`;
      document.getElementById('players').innerHTML = s.players.length ? s.players.map(p =>
        `<tr><td>${p.id}</td><td>${esc(p.name)}</td><td>${esc(sector(p.sector))}</td><td>${p.ping >= 0 ? p.ping + ' ms' : ''}</td><td>${dur(p.online)}</td><td><button onclick="kick(${p.id})">kick</button></td></tr>`).join('')
        : '<tr><td colspan="6" class="empty">nobody</td></tr>';
      document.getElementById('chat').innerHTML = s.chat.length ? s.chat.slice(-60).map(c =>
        `<p><span class="t">${new Date(c.at).toLocaleTimeString()}</span><span class="who">${esc(c.name)}</span>${c.sectorOnly ? '' : ' <span class="t">all</span>'}: ${esc(c.message)}</p>`).join('')
        : '<p class="empty">no messages yet</p>';
      const log = document.getElementById('log'); const stick = log.scrollTop + log.clientHeight >= log.scrollHeight - 8;
      log.textContent = s.log.join('\n'); if (stick) log.scrollTop = log.scrollHeight;
    } catch (e) { document.getElementById('meta').textContent = 'no answer from the server (' + e.message + ')'; }
  }
  refresh(); setInterval(refresh, 3000);
</script>
</body>
</html>
""";
}
