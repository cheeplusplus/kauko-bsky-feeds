using System.IdentityModel.Tokens.Jwt;
using FishyFlip.Models;

namespace KaukoBskyFeeds.Shared.Bsky;

public static class BskyAuth
{
    /// <summary>
    /// Get the DID from the server-to-server auth header. Does NOT validate!
    /// </summary>
    /// <param name="authorization">Authorization header value</param>
    /// <param name="expectedAudience">Assert an audience</param>
    /// <returns>ATDid of issuer</returns>
    public static ATDid? GetDidFromAuthHeader(string? authorization, string? expectedAudience)
    {
        if (string.IsNullOrWhiteSpace(authorization) || !authorization.StartsWith("Bearer "))
        {
            return null;
        }

        var tokenStr = authorization[7..];

        var handler = new JwtSecurityTokenHandler();
        var token = handler.ReadJwtToken(tokenStr);
        if (token == null)
        {
            return null;
        }

        var did = token.Issuer.Split("#").FirstOrDefault();
        if (string.IsNullOrEmpty(did) || !did.StartsWith("did:plc:"))
        {
            // If it's not a DID we probably have the wrong thing
            return null;
        }
        if (expectedAudience != null && !token.Audiences.Contains(expectedAudience))
        {
            // Check against an audience (usually "us")
            return null;
        }

        return ATDid.Create(did);
    }
}
