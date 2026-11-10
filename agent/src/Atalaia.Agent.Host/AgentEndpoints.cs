// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Pedro Serôdio

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Atalaia.Agent.Core;

namespace Atalaia.Agent.Host;

/// <summary>Rotas do agente: <c>/ws</c> (entrega o socket ao hub) e <c>GET /cover/{hash}</c>.</summary>
public static class AgentEndpoints
{
    public static void MapAgent(this WebApplication app, AgentHub hub, BlobStore blobs)
    {
        app.UseWebSockets();

        app.Map("/ws", async (HttpContext ctx) =>
        {
            if (!ctx.WebSockets.IsWebSocketRequest) { ctx.Response.StatusCode = StatusCodes.Status400BadRequest; return; }
            using var ws = await ctx.WebSockets.AcceptWebSocketAsync();
            await hub.HandleAsync(ws, ctx.RequestAborted, ctx.Connection.RemoteIpAddress);
        });

        // CORS: uma página aberta de outro lugar (file://, por exemplo) precisa conseguir buscar a capa.
        // O acesso continua exigindo o token no cabeçalho; não há cookie nem credencial de navegador.
        app.MapMethods("/cover/{hash}", ["OPTIONS"], (HttpContext ctx) =>
        {
            Cors(ctx);
            return Results.NoContent();
        });

        app.MapGet("/cover/{hash}", (HttpContext ctx, string hash) =>
        {
            Cors(ctx);
            switch (hub.Authenticate(BearerToken(ctx.Request), ctx.Connection.RemoteIpAddress))
            {
                case AuthResult.Locked: return Results.StatusCode(StatusCodes.Status429TooManyRequests);
                case AuthResult.Wrong: return Results.StatusCode(StatusCodes.Status401Unauthorized);
            }
            var bytes = blobs.Get(hash);
            if (bytes is null) return Results.NotFound();
            ctx.Response.Headers.CacheControl = "private, max-age=86400, immutable";
            return Results.Bytes(bytes, ContentType(bytes));
        });
    }

    private static void Cors(HttpContext ctx)
    {
        ctx.Response.Headers.AccessControlAllowOrigin = "*";
        ctx.Response.Headers.AccessControlAllowHeaders = "Authorization";
        ctx.Response.Headers.AccessControlAllowMethods = "GET, OPTIONS";
    }

    private static string? BearerToken(HttpRequest request)
    {
        const string prefix = "Bearer ";
        var h = request.Headers.Authorization.ToString();
        return h.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? h[prefix.Length..].Trim() : null;
    }

    private static string ContentType(byte[] b) => b switch
    {
        [0xFF, 0xD8, 0xFF, ..] => "image/jpeg",
        [0x89, 0x50, 0x4E, 0x47, ..] => "image/png",
        _ => "application/octet-stream",
    };
}
