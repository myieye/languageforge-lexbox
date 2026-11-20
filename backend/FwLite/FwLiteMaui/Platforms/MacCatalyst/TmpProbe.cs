// TEMPORARY fork-CI probe, never merged.
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Identity.Client.Extensions.Msal;

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

    static void Run(string mode)
    {
        try
        {
            Log($"mode={mode} IsMacOS={OperatingSystem.IsMacOS()} IsMacCatalyst={OperatingSystem.IsMacCatalyst()} IsIOS={OperatingSystem.IsIOS()} RI.IsOSPlatform(OSX)={RuntimeInformation.IsOSPlatform(OSPlatform.OSX)} OSDescription={RuntimeInformation.OSDescription}");
            Log($"AppDataDirectory={FileSystem.AppDataDirectory} HOME={Environment.GetEnvironmentVariable("HOME")} APP_SANDBOX_CONTAINER_ID={Environment.GetEnvironmentVariable("APP_SANDBOX_CONTAINER_ID")}");
            foreach (var dp in new[] { false, true })
            {
                var store = new MacKeychainTokenCacheStore("fwlite_probe_service", "fwlite_probe_account_dp" + dp, dp);
                var real = new MacKeychainTokenCacheStore(FwLiteShared.Auth.OAuthClient.KeyChainServiceName, FwLiteShared.Auth.OAuthClient.KeyChainAccountName, dp);
                Try($"dp={dp} read-before", () => Describe(store.Read()));
                Try($"dp={dp} real-item read", () => Describe(real.Read()));
                if (mode == "write")
                {
                    Try($"dp={dp} write", () => { store.Write(Encoding.UTF8.GetBytes("probe-value-1")); return "ok"; });
                    Try($"dp={dp} overwrite", () => { store.Write(Encoding.UTF8.GetBytes("probe-value-2")); return "ok"; });
                    Try($"dp={dp} read-after-write", () => Describe(store.Read()));
                }
            }
            Try("msal-helper default (keychain)", () =>
            {
                var props = new StorageCreationPropertiesBuilder(Path.Combine(FileSystem.AppDataDirectory, "probe-msal.cache"), FileSystem.AppDataDirectory)
                    .WithMacKeyChain("fwlite_probe_msal", "fwlite_probe_msal").Build();
                var helper = MsalCacheHelper.CreateAsync(props).GetAwaiter().GetResult();
                helper.VerifyPersistence();
                return "VerifyPersistence ok";
            });
            Try("msal-helper unprotected file", () =>
            {
                var props = new StorageCreationPropertiesBuilder(Path.Combine(FileSystem.AppDataDirectory, "probe-msal-file.cache"), FileSystem.AppDataDirectory)
                    .WithUnprotectedFile().Build();
                var helper = MsalCacheHelper.CreateAsync(props).GetAwaiter().GetResult();
                helper.VerifyPersistence();
                return "VerifyPersistence ok";
            });
        }
        catch (Exception e)
        {
            Log("crashed: " + e);
        }
        Log("DONE");
    }

    static string Describe(byte[]? data) => data is null ? "<none>" : $"'{Encoding.UTF8.GetString(data)}' ({data.Length} bytes)";

    static void Try(string name, Func<string> action)
    {
        try { Log($"{name}: {action()}"); }
        catch (Exception e) { Log($"{name}: FAILED {e.GetType().Name}: {e.Message}"); }
    }
}
