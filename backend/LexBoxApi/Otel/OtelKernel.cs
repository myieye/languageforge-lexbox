using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Security.Claims;
using LexCore.Auth;
using Npgsql;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace LexBoxApi.Otel;

public static class OtelKernel
{
    public const string ServiceName = "LexBox-Api";
    public static void AddOpenTelemetryInstrumentation(this IServiceCollection services)
    {
        var appResourceBuilder = ResourceBuilder.CreateDefault()
            .AddEnvironmentVariableDetector()
            .AddService(ServiceName);
        services.AddOpenTelemetry().WithTracing(tracerProviderBuilder =>
            tracerProviderBuilder
                // Debugging
                // .AddConsoleExporter()
                .AddOtlpExporter()
                .AddSource(ServiceName)
                .SetResourceBuilder(appResourceBuilder)
                // could potentially add baggage to the trace as done in
                // https://github.com/honeycombio/honeycomb-opentelemetry-dotnet/blob/main/src/Honeycomb.OpenTelemetry.Instrumentation.AspNetCore/TracerProviderBuilderExtensions.cs
                .AddAspNetCoreInstrumentation(options =>
                {
                    options.RecordException = true;
                    options.EnrichWithHttpRequest = (activity, request) =>
                    {
                        activity.EnrichWithUser(request.HttpContext);
                    };
                    options.EnrichWithHttpResponse = (activity, response) =>
                    {
                        activity.EnrichWithUser(response.HttpContext);
                    };
                })
                .AddHttpClientInstrumentation()
                .AddEntityFrameworkCoreInstrumentation()
                .AddNpgsql()
                .AddHotChocolateInstrumentation()
            );

        var meter = new Meter(ServiceName);
        var counter = meter.CreateCounter<long>("api.login-attempts");
        services.AddOpenTelemetry().WithMetrics(metricProviderBuilder =>
            metricProviderBuilder
                .AddOtlpExporter()
                .AddMeter(meter.Name)
                .SetResourceBuilder(appResourceBuilder)
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
        );
    }

    private static void EnrichWithUser(this Activity activity, HttpContext httpContext)
    {
        var claimsPrincipal = httpContext.User;
        var userId = claimsPrincipal?.FindFirstValue(LexAuthConstants.IdClaimType);
        if (userId != null)
        {
            activity.SetTag("app.user.id", userId);
        }
        var userRole = claimsPrincipal?.FindFirstValue(LexAuthConstants.RoleClaimType);
        if (userRole != null)
        {
            activity.SetTag("app.user.role", userRole);
        }
    }
}