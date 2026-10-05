using System.Collections.Concurrent;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;
using HomeInventory.Client;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;

namespace HomeInventory;

/// <summary>Persisted network-access settings. The PIN is only ever stored as a salted PBKDF2 hash.</summary>
public sealed record NetworkSettings(bool Enabled = false, int Port = 5068, string? PinHash = null, string? PinSalt = null, int Version = 0)
{
    public bool HasPin => PinHash is not null && PinSalt is not null;
}

/// <summary>Loads and saves <see cref="NetworkSettings"/> as JSON (default <c>%LOCALAPPDATA%\HomeInventory\network.json</c>).</summary>
public sealed class NetworkSettingsStore(string path)
{
    const int Iterations = 210_000;
    static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };
    readonly Lock gate = new();
    NetworkSettings? current;

    public NetworkSettings Current
    {
        get
        {
            lock (gate) return current ??= Load(path);
        }
    }

    public static NetworkSettings Load(string path)
    {
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize<NetworkSettings>(File.ReadAllText(path)) ?? new() : new();
        }
        catch (JsonException)
        {
            return new();
        }
    }

    public NetworkSettings Update(bool enabled, int port, string? newPin)
    {
        lock (gate)
        {
            var settings = (current ??= Load(path)) with { Enabled = enabled, Port = port };
            if (newPin is not null)
            {
                var salt = RandomNumberGenerator.GetBytes(16);
                // A new PIN bumps the version, which invalidates every device's sign-in cookie.
                settings = settings with { PinSalt = Convert.ToBase64String(salt), PinHash = Convert.ToBase64String(Hash(newPin, salt)), Version = settings.Version + 1 };
            }
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(settings, Indented));
            return current = settings;
        }
    }

    public bool VerifyPin(string? pin)
    {
        var settings = Current;
        if (pin is null || !settings.HasPin) return false;
        return CryptographicOperations.FixedTimeEquals(Hash(pin, Convert.FromBase64String(settings.PinSalt!)), Convert.FromBase64String(settings.PinHash!));
    }

    static byte[] Hash(string pin, byte[] salt) => Rfc2898DeriveBytes.Pbkdf2(pin, salt, Iterations, HashAlgorithmName.SHA256, 32);
}

/// <summary>
/// Lets other devices on the home network use the app behind a shared PIN. Requests from this computer are always
/// allowed; requests from public (internet) addresses are always refused; home-network requests need network access
/// turned on and a PIN sign-in cookie for the current PIN.
/// </summary>
public static class NetworkAccess
{
    public const string PinVersionClaim = "pin-version";
    const int MaxFailures = 5;
    static readonly TimeSpan Lockout = TimeSpan.FromMinutes(5);
    static readonly ConcurrentDictionary<string, (int Failures, DateTime LockedUntil)> Attempts = new();

    public static bool IsLocal(HttpContext context) => context.Connection.RemoteIpAddress is not { } ip || IPAddress.IsLoopback(ip);

    /// <summary>Private (home/office) address ranges only; CGNAT and public addresses are excluded.</summary>
    public static bool IsHomeNetwork(IPAddress ip)
    {
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        if (ip.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = ip.GetAddressBytes();
            return b[0] == 10 || b[0] == 172 && b[1] >= 16 && b[1] <= 31 || b[0] == 192 && b[1] == 168 || b[0] == 169 && b[1] == 254;
        }
        return ip.IsIPv6LinkLocal || ip.IsIPv6UniqueLocal;
    }

    public static IServiceCollection AddNetworkAccess(this IServiceCollection services, string settingsPath)
    {
        services.AddSingleton(new NetworkSettingsStore(settingsPath));
        services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(options =>
        {
            options.Cookie.Name = "home-inventory-pin";
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.ExpireTimeSpan = TimeSpan.FromDays(30);
            options.SlidingExpiration = true;
            options.LoginPath = "/pin";
        });
        return services;
    }

    public static IApplicationBuilder UseNetworkAccess(this IApplicationBuilder app) => app.Use(async (context, next) =>
    {
        if (IsLocal(context))
        {
            await next();
            return;
        }
        var ip = context.Connection.RemoteIpAddress!;
        var settings = context.RequestServices.GetRequiredService<NetworkSettingsStore>().Current;
        if (!IsHomeNetwork(ip) || !settings.Enabled || !settings.HasPin)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsync(IsHomeNetwork(ip) ? "Network access is turned off on the Home Inventory computer." : "Home Inventory is only available on the home network.");
            return;
        }
        if (context.Request.Path.StartsWithSegments("/pin"))
        {
            await next();
            return;
        }
        var result = await context.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        if (result.Succeeded && result.Principal.FindFirstValue(PinVersionClaim) == settings.Version.ToString(System.Globalization.CultureInfo.InvariantCulture))
        {
            await next();
            return;
        }
        if (context.Request.Path.StartsWithSegments("/api"))
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        else
            context.Response.Redirect($"/pin?returnUrl={Uri.EscapeDataString(context.Request.Path + context.Request.QueryString)}");
    });

    public static void MapNetworkAccess(this IEndpointRouteBuilder app)
    {
        app.MapGet("/pin", (string? returnUrl, string? error) => Results.Content(PinPage(returnUrl, error), "text/html"));
        app.MapPost("/pin", async (HttpContext context, NetworkSettingsStore store) =>
        {
            var form = await context.Request.ReadFormAsync();
            var returnUrl = form["returnUrl"].ToString();
            if (!returnUrl.StartsWith('/') || returnUrl.StartsWith("//", StringComparison.Ordinal)) returnUrl = "/"; // only local paths, never another site
            var client = context.Connection.RemoteIpAddress?.ToString() ?? "local";
            var now = DateTime.UtcNow;
            if (Attempts.TryGetValue(client, out var attempt) && attempt.LockedUntil > now)
                return Results.Content(PinPage(returnUrl, "Too many attempts. Try again in a few minutes."), "text/html", statusCode: StatusCodes.Status429TooManyRequests);
            if (!store.VerifyPin(form["pin"]))
            {
                var failures = attempt.Failures + 1;
                Attempts[client] = failures >= MaxFailures ? (0, now + Lockout) : (failures, DateTime.MinValue);
                return Results.Content(PinPage(returnUrl, "That PIN is not correct."), "text/html", statusCode: StatusCodes.Status401Unauthorized);
            }
            Attempts.TryRemove(client, out _);
            var identity = new ClaimsIdentity([new Claim(PinVersionClaim, store.Current.Version.ToString(System.Globalization.CultureInfo.InvariantCulture))], CookieAuthenticationDefaults.AuthenticationScheme);
            await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity), new AuthenticationProperties { IsPersistent = true });
            return Results.Redirect(returnUrl);
        }).DisableAntiforgery();

        // Settings can only be read or changed from this computer.
        app.MapGet("/api/network", (HttpContext context, NetworkSettingsStore store, IServer server) =>
            IsLocal(context) ? Results.Ok(Status(store.Current, server)) : Results.StatusCode(StatusCodes.Status403Forbidden));
        app.MapPut("/api/network", (HttpContext context, NetworkSettingsInput input, NetworkSettingsStore store, IServer server) =>
        {
            if (!IsLocal(context)) return Results.StatusCode(StatusCodes.Status403Forbidden);
            var errors = new Dictionary<string, string[]>();
            var pin = string.IsNullOrWhiteSpace(input.Pin) ? null : input.Pin.Trim();
            if (pin is not null && (pin.Length < 4 || pin.Length > 12)) errors["pin"] = ["The PIN must be 4 to 12 characters."];
            if (input.Port is < 1024 or > 65535) errors["port"] = ["The port must be between 1024 and 65535."];
            if (input.Enabled && pin is null && !store.Current.HasPin) errors["pin"] = ["Set a PIN before turning network access on."];
            if (errors.Count > 0) return Results.ValidationProblem(errors);
            return Results.Ok(Status(store.Update(input.Enabled, input.Port ?? store.Current.Port, pin), server));
        });
    }

    static NetworkStatusDto Status(NetworkSettings settings, IServer server)
    {
        var listening = server.Features.Get<IServerAddressesFeature>()?.Addresses.Any(a => a.Contains("0.0.0.0", StringComparison.Ordinal) || a.Contains("[::]", StringComparison.Ordinal) || a.Contains("://*", StringComparison.Ordinal) || a.Contains("://+", StringComparison.Ordinal)) ?? false;
        var addresses = NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .SelectMany(n => n.GetIPProperties().UnicastAddresses)
            .Select(a => a.Address)
            .Where(a => a.AddressFamily == AddressFamily.InterNetwork && IsHomeNetwork(a) && !a.ToString().StartsWith("169.254.", StringComparison.Ordinal))
            .Select(a => $"http://{a}:{settings.Port}/")
            .Distinct()
            .ToList();
        return new NetworkStatusDto(settings.Enabled, settings.Port, settings.HasPin, listening, addresses, settings.Enabled != listening);
    }

    static string PinPage(string? returnUrl, string? error)
    {
        var encoder = HtmlEncoder.Default;
        return $$"""
            <!DOCTYPE html>
            <html lang="en">
            <head>
                <meta charset="utf-8" /><meta name="viewport" content="width=device-width, initial-scale=1" />
                <title>Home Inventory – enter PIN</title>
                <style>
                    body { font-family: system-ui, sans-serif; display: grid; place-items: center; min-height: 100vh; margin: 0; background: linear-gradient(180deg, #052767, #3a0647); }
                    form { background: white; padding: 2rem; border-radius: .5rem; width: min(20rem, 90vw); box-shadow: 0 1rem 2rem rgba(0,0,0,.3); }
                    h1 { font-size: 1.4rem; margin-top: 0; } input, button { width: 100%; font-size: 1.2rem; padding: .6rem; box-sizing: border-box; margin-top: .5rem; }
                    button { background: #1b6ec2; color: white; border: 0; border-radius: .3rem; } .error { color: #b02a37; }
                </style>
            </head>
            <body>
                <form method="post" action="/pin">
                    <h1>Home Inventory</h1>
                    <p>Enter the PIN set on the Home Inventory computer.</p>
                    {{(error is null ? "" : $"<p class=\"error\">{encoder.Encode(error)}</p>")}}
                    <input type="password" name="pin" inputmode="numeric" autocomplete="current-password" autofocus required />
                    <input type="hidden" name="returnUrl" value="{{encoder.Encode(returnUrl ?? "/")}}" />
                    <button type="submit">Open</button>
                </form>
            </body>
            </html>
            """;
    }
}
