using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using MiniLcm.Project;
using SIL.Harmony.Config;

namespace LcmCrdt.Tests;

public static class LcmCrdtTestsKernel
{
    public static IServiceCollection AddTestLcmCrdtClient(this IServiceCollection services, CrdtProject? project = null)
    {
        services.TryAddSingleton<IConfiguration>(new ConfigurationRoot([]));
        services.AddLogging(builder => builder.AddDebug());
        services.AddSingleton<IServerHttpClientProvider, FakeHttpClientProvider>();
        services.AddLcmCrdtClient();
        services.Configure<LcmCrdtConfig>(config => config.EnableProjectDataFileCache = false);
        // Must be set here, not per-fixture: the IChange converter (which bakes in UnknownChangeHandling)
        // lives in the EF model, and EF caches the model process-wide, so the first fixture's setting
        // would win for the whole test run. Production keeps the default Throw.
        services.Configure<HarmonyConfig>(config => config.UnknownChangeHandling = UnknownChangeHandling.Fallback);
        if (project is not null)
        {
            var initializedNewDb = false;
            services.AddScoped(provider =>
            {
                var currentProjectService = ActivatorUtilities.CreateInstance<CurrentProjectService>(provider);
                if (!initializedNewDb)
                {
                    // this init code is practical in most cases, but if it happens a second time,
                    // we assume the code intentionally created a seperate scope that it will explicitly initialize
                    currentProjectService.SetupProjectContextForNewDb(project);
                    initializedNewDb = true;
                }
                return currentProjectService;
            });
        }
        return services;
    }

    private class FakeHttpClientProvider : IServerHttpClientProvider
    {
        public ValueTask<HttpClient> GetHttpClient()
        {
            throw new NotImplementedException();
        }

        public ValueTask<ConnectionStatus> ConnectionStatus(bool forceRefresh = false)
        {
            return ValueTask.FromResult(MiniLcm.Project.ConnectionStatus.Offline);
        }
    }
}
