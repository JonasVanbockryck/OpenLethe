using System.Text;
using OpenLethe.Data;
using OpenLethe.Server.Auth;

namespace OpenLethe.Server.Login;

public static class SignInAsSteamEndpoint
{
    public static IEndpointRouteBuilder MapSignInAsSteam(this IEndpointRouteBuilder app)
    {
        var packetId = global::PacketRouting.ResolvePacketId<global::ResPacket_SignInAsSteam>();

        app.MapPost("/login/SignInAsSteam", async (HttpContext ctx, AccountStore store, JwtService jwt, IConfiguration cfg, CancellationToken ct) =>
        {
            var req = await System.Text.Json.JsonSerializer.DeserializeAsync<
                global::RequestPacket<global::ReqPacket_SignInAsSteam>>(ctx.Request.Body, global::PacketJson.Options, ct);

            var hex = req?.parameters?.steamToken;
            if (string.IsNullOrEmpty(hex)) return Results.BadRequest();

            byte[] raw;
            try { raw = Convert.FromHexString(hex); }
            catch { return Results.BadRequest(); }

            var token = Encoding.UTF8.GetString(raw);

            // Dev mode (Auth:DevAcceptAnyToken): accept ANY jwt as an identity -
            // read its subject without verifying the signature, auto-create that
            // account, and hand back a token WE signed so later /api/ calls (which
            // the middleware verifies for real) keep working. Off by default.
            string sub;
            int ingameId;
            string authCode;
            if (cfg.GetValue("Auth:DevAcceptAnyToken", false))
            {
                if (!JwtService.TryReadSubjectUnverified(token, out sub)) return Results.BadRequest();
                // Resolve before create. GetOrCreateByUsernameAsync refuses a
                // Discord-linked row so that /auth/login cannot CLAIM someone's
                // account - but here we are identifying, not claiming, and a token
                // minted by our own OAuth carries the snowflake as its subject,
                // which is exactly that row's Username. Without the lookup first,
                // every account that has logged in through Discord gets a 400.
                var account = await store.FindByUsernameAsync(sub, ct)
                    ?? await store.GetOrCreateByUsernameAsync(sub, ct);
                if (account is null) return Results.BadRequest();
                ingameId = account.IngameId;
                authCode = jwt.Mint(sub);
            }
            else
            {
                // Ephemeral is expected here: it is the token Crux hands the game.
                // Only the captcha token is out of place.
                if (!jwt.TryVerifyClaims(token, out var claims) || claims.Captcha)
                {
                    return Results.BadRequest();
                }
                sub = claims.Sub;
                // Every client reconnects through here at once after an outage, so read
                // the one int this needs rather than the whole row.
                if (await store.FindIngameIdAsync(sub, ct) is not int id) return Results.BadRequest();
                ingameId = id;
                authCode = token;
            }

            var result = new global::ResPacket_SignInAsSteam
            {
                userAuth = new global::UserAuthFormat
                {
                    uid = ingameId,
                    public_id = ingameId,
                    db_id = 0,
                    auth_code = authCode,
                    last_login_date = "2025-03-31T15:10:00.000Z",
                    last_update_date = "2025-03-31T15:10:00.000Z",
                    data_version = 16,
                },
                accountInfo = new global::AccountInfoFormat { uid = (ulong)ingameId },
                walletCurrency = "USD",
            };

            return Results.Json(
                global::ResponsePacket<global::ResPacket_SignInAsSteam>.Ok(result, packetId),
                global::PacketJson.Options);
        });

        return app;
    }
}
