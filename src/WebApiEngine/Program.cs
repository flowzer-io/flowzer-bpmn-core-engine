using WebApiEngine;
using WebApiEngine.Auth;
using WebApiEngine.Background;
using WebApiEngine.BusinessLogic;
using WebApiEngine.Diagnostics;
using WebApiEngine.Middleware;
using WebApiEngine.Persistence;
using Microsoft.Extensions.Options;

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
builder.Services.AddHostedService<TimerSchedulerBackgroundService>();
builder.Services.AddFlowzerCors(builder.Configuration, builder.Environment);
builder.Services.AddFlowzerAuthentication(builder.Configuration);

var app = builder.Build();

app.UseFlowzerRequestDiagnostics();
app.UseFlowzerApiExceptionHandling();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseFlowzerCors();
app.UseFlowzerAuthentication();

// TLS terminiert am Reverse Proxy / Gateway; HTTPS-Redirect bewusst nicht im Host.

app.MapControllers();

await app.ApplyStartupMigrationsIfConfiguredAsync();

var timerSchedulerOptions = app.Services.GetRequiredService<IOptions<TimerSchedulerOptions>>().Value;
app.Services.GetRequiredService<BpmnBusinessLogic>().Load(timerSchedulerOptions.Enabled);

app.Run();
return 0;

public partial class Program
{
}
