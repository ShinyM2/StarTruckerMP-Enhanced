using UnityEngine;

namespace StarTruckMP.Client;

public static class PlayerState
{
    /// <summary>
    /// This will be populated automatic if we have successfully authenticated with Xbox Live token
    /// or Steam Authentication token.
    /// </summary>
    public static string Token { get; set; } = "";

    /// <summary>
    /// Server ephemeral P-256 public key (SubjectPublicKeyInfo DER bytes) received during HTTPS auth.
    /// Used by the client to derive the shared ChaCha20-Poly1305 session key via ECDH + HKDF.
    /// Kept for the whole session so that a reconnect can derive a fresh key without a new
    /// HTTPS round trip; replaced whenever authentication runs again.
    /// </summary>
    public static byte[]? ServerPublicKey { get; set; }

    #region Game State

    /// <summary>Our own display name, taken from the platform (Steam persona / gamertag).</summary>
    public static string Name { get; set; } = "";

    public static string Sector { get; set; } = "";

    /// <summary>The player's own truck, once a save is loaded. Game thread.</summary>
    public static GameObject Truck { get; set; }

    #endregion
}
