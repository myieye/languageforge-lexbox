// TEMPORARY fork-CI probe, never merged.
using System.Runtime.InteropServices;
using FwLiteShared.Auth;
using Microsoft.Extensions.Options;

namespace FwLiteMaui;

public static class TmpProbe
{
    public static void StartIfRequested()
    {
        var mode = Environment.GetEnvironmentVariable("FWLITE_PROBE");
        if (string.IsNullOrEmpty(mode)) return;
        Task.Run(() => Run(mode));
    }

    static void Log(string msg) => Console.WriteLine("PROBE: " + msg);

    static async Task Run(string mode)
    {
        try
        {
            Log($"mode={mode} IsMacCatalyst={OperatingSystem.IsMacCatalyst()} RI.IsOSPlatform(OSX)={RuntimeInformation.IsOSPlatform(OSPlatform.OSX)} AppDataDirectory={FileSystem.AppDataDirectory}");
            IServiceProvider? services = null;
            for (var i = 0; i < 120 && services is null; i++)
            {
                services = IPlatformApplication.Current?.Services;
                if (services is null) await Task.Delay(500);
            }
            if (services is null) { Log("no services"); return; }
            var authConfig = services.GetRequiredService<IOptions<AuthConfig>>().Value;
            var store = authConfig.TokenCacheStore;
            Log($"TokenCacheStore={store?.GetType().Name ?? "<none>"} CacheFileName={authConfig.CacheFileName} servers={string.Join(",", authConfig.LexboxServers.Select(s => s.Authority))}");
            var factory = services.GetRequiredService<OAuthClientFactory>();
            if (mode == "write")
            {
                if (store is null) { Log("no store, nothing to write"); }
                else
                {
                    var host = authConfig.LexboxServers[0].Authority.Host;
                    store.Write(System.Text.Encoding.UTF8.GetBytes(FakeCache(host, authConfig.ClientId)));
                    Log($"wrote fake login for {host}");
                }
            }
            foreach (var server in authConfig.LexboxServers)
            {
                var signedIn = await factory.GetClient(server).IsSignedIn();
                Log($"IsSignedIn({server.Authority.Host})={signedIn}");
            }
            var file = authConfig.CacheFileName;
            if (File.Exists(file))
                Log($"cache file {file}: {new FileInfo(file).Length} bytes, mode {File.GetUnixFileMode(file)}");
            else
                Log($"cache file {file} does not exist");
        }
        catch (Exception e)
        {
            Log("crashed: " + e);
        }
        Log("DONE");
    }

    // A minimal MSAL v3 cache holding one account and refresh token, the way MSAL saves a lexbox login.
    static string FakeCache(string host, string clientId) => $$"""
        {
          "Account": {
            "probe-uid.probe-utid-{{host}}-": {
              "home_account_id": "probe-uid.probe-utid",
              "environment": "{{host}}",
              "realm": "",
              "local_account_id": "probe-uid",
              "username": "probe@example.test",
              "authority_type": "MSSTS"
            }
          },
          "RefreshToken": {
            "probe-uid.probe-utid-{{host}}-refreshtoken-{{clientId}}--": {
              "home_account_id": "probe-uid.probe-utid",
              "environment": "{{host}}",
              "client_id": "{{clientId}}",
              "secret": "probe-not-a-real-token",
              "credential_type": "RefreshToken"
            }
          },
          "IdToken": {},
          "AccessToken": {},
          "AppMetadata": {}
        }
        """;
}
