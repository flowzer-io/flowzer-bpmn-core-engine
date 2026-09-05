using WebApiEngine;
using WebApiEngine.Auth;
using WebApiEngine.Background;
using WebApiEngine.BusinessLogic;
using WebApiEngine.Diagnostics;
using WebApiEngine.Limits;
using WebApiEngine.Middleware;
using WebApiEngine.Persistence;

var builder = WebApplication.CreateBuilder(args);

// Getrennter Migrationsschritt (Compose-Dienst `migrate` bzw. manuell): keine Host-Pipeline,
// nur die Datenbank auf den Stand der eingebetteten Migrationen bringen.
if (FlowzerStorageExtensions.IsMigrationRun(args))
{
    using var migrationLoggerFactory = LoggerFactory.Create(logging => logging.AddConsole());
    return await FlowzerStorageExtensions.RunMigrationsAsync(builder.Configuration, migrationLoggerFactory.CreateLogger("Migrations"));
}

builder.Services.AddControllers().AddJsonOptions(options =>
{
    options.JsonSerializerOptions.ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles;
    options.JsonSerializerOptions.WriteIndented = true;
    options.JsonSerializerOptions.DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull;
});

// Add services to the container.
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddHttpContextAccessor();
builder.Services.AddFlowzerStorage(builder.Configuration);
builder.Services.AddSingleton<ICurrentUserContextAccessor, HttpContextCurrentUserContextAccessor>();
builder.Services.AddSingleton<TimerSchedulerDiagnosticsState>();
builder.Services.AddFlowzerObservability(builder.Configuration);
builder.Services.AddSingleton<FormBusinessLogic>();
builder.Services.AddSingleton<DefinitionBusinessLogic>();
builder.Services.AddSingleton<BpmnBusinessLogic>();
builder.Services.AddSingleton<UserTaskFormResolver>();
builder.Services.Configure<TimerSchedulerOptions>(builder.Configuration.GetSection(TimerSchedulerOptions.SectionName));
// Reihenfolge zaehlt: erst den gespeicherten Zustand zurueckholen, dann zyklisch weiterarbeiten.
builder.Services.AddHostedService<EngineStartupService>();
builder.Services.AddHostedService<TimerSchedulerBackgroundService>();
builder.Services.AddFlowzerCors(builder.Configuration, builder.Environment);
builder.Services.AddFlowzerAuthentication(builder.Configuration);
builder.Services.AddFlowzerLimits(builder.Configuration);

// Die Uploadgrenze auch am Server selbst setzen. Die Middleware setzt sie je Anfrage; ohne
// diesen Wert bliebe fuer alles, was die Middleware nicht erreicht, das Server-Default stehen.
var configuredUploadLimit = builder.Configuration.GetSection(FlowzerUploadLimitOptions.SectionName)
    .Get<FlowzerUploadLimitOptions>() ?? new FlowzerUploadLimitOptions();
builder.WebHost.ConfigureKestrel(kestrel => kestrel.Limits.MaxRequestBodySize = configuredUploadLimit.MaxUploadBytes);

// Hinter dem Gateway steht die echte Adresse des Aufrufers nur im Weiterleitungsheader.
// Ohne diese Auswertung liefe das Kontingent fuer alle anonymen Aufrufer gegen die Adresse
// des Proxys, also gegen ein gemeinsames Fenster.
builder.Services.AddFlowzerForwardedHeaders(builder.Configuration);

var app = builder.Build();

app.UseFlowzerForwardedHeaders();
app.UseFlowzerRequestDiagnostics();
app.UseFlowzerApiExceptionHandling();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseFlowzerCors();
// Ein zu grosser Koerper soll gar nicht erst gepuffert werden.
app.UseFlowzerUploadLimit();
app.UseFlowzerAuthentication();
// Erst nach der Authentifizierung steht fest, wer anfragt; davor liefe jedes Kontingent
// gegen dieselbe Adress-Partition.
app.UseFlowzerRateLimiting();

// TLS terminiert am Reverse Proxy / Gateway; HTTPS-Redirect bewusst nicht im Host.

app.MapControllers();

await app.ApplyStartupMigrationsIfConfiguredAsync();

app.Run();
return 0;

public partial class Program
{
}
