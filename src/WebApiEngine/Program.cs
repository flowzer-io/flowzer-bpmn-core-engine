using WebApiEngine;
using WebApiEngine.Auth;
using WebApiEngine.BusinessLogic;
using WebApiEngine.Middleware;

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
builder.Services.AddSingleton<FormBusinessLogic>();
builder.Services.AddSingleton<DefinitionBusinessLogic>();
builder.Services.AddSingleton<BpmnBusinessLogic>();
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

app.Services.GetRequiredService<BpmnBusinessLogic>().Load();

app.Run();

public partial class Program
{
}
