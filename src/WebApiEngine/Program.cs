using WebApiEngine;
using WebApiEngine.Auth;
using WebApiEngine.Background;
using WebApiEngine.BusinessLogic;
using WebApiEngine.Diagnostics;
using WebApiEngine.Middleware;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

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
//builder.Services.AddSingleton<IStorageSystem, MemoryStorageSystem.MemoryStorageSystem>();
builder.Services.AddSingleton<ITransactionalStorageProvider, FilesystemStorageSystem.FileSystemTransactionalStorageProvider>();
builder.Services.AddSingleton<IStorageSystem, FilesystemStorageSystem.Storage>();
builder.Services.AddSingleton<ICurrentUserContextAccessor, HttpContextCurrentUserContextAccessor>();
builder.Services.AddSingleton<TimerSchedulerDiagnosticsState>();
builder.Services.AddFlowzerObservability(builder.Configuration);
builder.Services.AddSingleton<FormBusinessLogic>();
builder.Services.AddSingleton<DefinitionBusinessLogic>();
builder.Services.AddSingleton<BpmnBusinessLogic>();
builder.Services.Configure<TimerSchedulerOptions>(builder.Configuration.GetSection(TimerSchedulerOptions.SectionName));
builder.Services.AddHostedService<TimerSchedulerBackgroundService>();
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAllOrigins",
        builder =>
        {
            builder.AllowAnyOrigin()
                .AllowAnyHeader()
                .AllowAnyMethod();
        });
});

var app = builder.Build();

app.UseFlowzerRequestDiagnostics();
app.UseFlowzerApiExceptionHandling();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors("AllowAllOrigins"); // Diese Zeile stellt sicher, dass die CORS-Richtlinie angewendet wird

// app.UseHttpsRedirection();

app.MapControllers();

var timerSchedulerOptions = app.Services.GetRequiredService<IOptions<TimerSchedulerOptions>>().Value;
app.Services.GetRequiredService<BpmnBusinessLogic>().Load(timerSchedulerOptions.Enabled);

app.Run();

public partial class Program
{
}
