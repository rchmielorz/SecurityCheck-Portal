using securitycheck_portal.Core.Data;
using securitycheck_portal.Core.Git;
using securitycheck_portal.Core.Scanning;
using securitycheck_portal.Worker;

var builder = Host.CreateApplicationBuilder(args);

// Runs as a Windows service; from a console (dotnet run) it behaves as a normal host.
builder.Services.AddWindowsService(options => options.ServiceName = "SecurityCheck.Worker");

// A scan may be running when the service is stopped: its token is cancelled, which kills Trivy and git,
// and the scan is marked interrupted. Thirty seconds is enough for that.
builder.Services.Configure<HostOptions>(options => options.ShutdownTimeout = TimeSpan.FromSeconds(30));

// ConnectionStrings:Portal, Git:Token, Git:AllowedHosts and Scan:CacheDirectory / Scan:WorkRoot are validated on start;
// secrets come from user-secrets (worker/) or the environment, never from appsettings*.json.
builder.Services.AddPortalData();
builder.Services.AddGitResolution();
builder.Services.AddScanning();

builder.Services.AddHostedService<ScanWorker>();

await builder.Build().RunAsync();
