using System.IdentityModel.Tokens.Jwt;
using FishyFlip.Models;

namespace KaukoBskyFeeds.Shared.Bsky;

public static class BskyAuth
{
    // Sample JWT
    /* {
      "iat": 1731713895,
      "iss": "did:plc:bovxqt3tac2yrzub4paycpvd",
      "aud": "did:web:bsky.biosynth.link",
      "exp": 1731713955,
      "lxm": "app.bsky.feed.getFeedSkeleton",
      "jti": "eb0051d349425a8a8575f6b131127096"
    } */

    /// <summary>
    /// Get the DID from the server-to-server auth header. Does NOT validate!
    /// </summary>
    /// <param name="authorization">Authorization header value</param>
    /// <param name="expectedAudience">Assert an audience</param>
    /// <returns>ATDid of issuer</returns>
    public static ATDid? GetDidFromAuthHeader(
        string? authorization,
        string? expectedLxm,
        string? expectedAudience,
        bool checkValidTo = true
    )
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

        if (checkValidTo && token.ValidTo < DateTime.UtcNow)
        {
            return null;
        }

        var did = token.Issuer.Split("#").FirstOrDefault();
        if (string.IsNullOrEmpty(did) || !did.StartsWith("did:plc:"))
        {
            // If it's not a DID we probably have the wrong thing
            return null;
        }

        if (
            expectedLxm != null
            && !token.Claims.Any(a => a.Type == "lxm" && a.Value == expectedLxm)
        )
        {
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
