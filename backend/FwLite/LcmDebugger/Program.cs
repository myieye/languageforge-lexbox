// See https://aka.ms/new-console-template for more information

using FwDataMiniLcmBridge;
using FwLiteProjectSync;
using LcmCrdt;
using LcmDebugger;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MiniLcm.Project;
using Moq;

var builder = Host.CreateApplicationBuilder();
//slows down import to log all sql.
builder.Logging.AddFilter("Microsoft.EntityFrameworkCore", LogLevel.Warning);
builder.Services.AddFwDataBridge();

//*
// does not include FTS
builder.Services.AddLcmCrdtClientCore();
/*/
// does include FTS
builder.Services.AddLcmCrdtClient();
//*/

builder.Services.AddFwLiteProjectSync();
builder.Services.AddScoped((_services) => new Mock<IServerHttpClientProvider>().Object);

using var app = builder.Build();

await using var scope = app.Services.CreateAsyncScope();
var services = scope.ServiceProvider;

using var project = await services.OpenDownloadedProject("sbe-flex-20260728081315", openCopy: true);
var currentProjectService = services.GetRequiredService<CurrentProjectService>();
await currentProjectService.UpdateUserRole(UserProjectRole.Editor);
await services.SyncFwHeadlessProject(project, dryRun: true);

// await services.PrintAllEntries("sena-3");
