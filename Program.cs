using System.Text;
using Google.Protobuf.WellKnownTypes;
using Grpc.Net.Client;
using Maichess.Database.V1;
using Maichess.Engine.V1;
using Maichess.MoveValidator.V1;
using Maichess.User.V1;
using MaichessAnalysisService.Data;
using MaichessAnalysisService.Domain;
using MaichessAnalysisService.Rest;
using MaichessAnalysisService.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using SocketGrpc = Socket.V1.Socket;

DotNetEnv.Env.Load();
WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

string dbUrl = builder.Configuration["Services:DatabaseService"]
    ?? throw new InvalidOperationException("Services:DatabaseService is not configured");
string engineUrl = builder.Configuration["Services:EngineService"]
    ?? throw new InvalidOperationException("Services:EngineService is not configured");
string moveValidatorUrl = builder.Configuration["Services:MoveValidatorService"]
    ?? throw new InvalidOperationException("Services:MoveValidatorService is not configured");
string socketUrl = builder.Configuration["Services:SocketService"]
    ?? throw new InvalidOperationException("Services:SocketService is not configured");
string userServiceUrl = builder.Configuration["Services:UserService"]
    ?? throw new InvalidOperationException("Services:UserService is not configured");
string jwtKey = builder.Configuration["Jwt:Key"]
    ?? throw new InvalidOperationException("Jwt:Key is not configured");

builder.Services.AddSingleton(new Database.DatabaseClient(GrpcChannel.ForAddress(dbUrl)));
builder.Services.AddSingleton(new Bots.BotsClient(GrpcChannel.ForAddress(engineUrl)));
builder.Services.AddSingleton(new Moves.MovesClient(GrpcChannel.ForAddress(moveValidatorUrl)));
builder.Services.AddSingleton(new SocketGrpc.SocketClient(GrpcChannel.ForAddress(socketUrl)));
builder.Services.AddSingleton(new Users.UsersClient(GrpcChannel.ForAddress(userServiceUrl)));

builder.Services.AddSingleton<IAnalysisGameRepository, AnalysisGameRepository>();
builder.Services.AddSingleton<IAnalysisResultRepository, AnalysisResultRepository>();
builder.Services.AddSingleton<AnalysisMetaRepository>();
builder.Services.AddSingleton<AnalysisGameService>();
builder.Services.AddSingleton<AnalysisSessionService>();

builder.Services.Configure<AnalysisConfig>(builder.Configuration.GetSection("Analysis"));

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
            ValidateIssuer = false,
            ValidateAudience = false,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero,
        };
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                if (context.Request.Cookies.TryGetValue("access_token", out string? token))
                {
                    context.Token = token;
                }

                return Task.CompletedTask;
            },
        };
    });

builder.Services.AddAuthorization();
builder.Services.AddOpenApi();

string otlpEndpoint = Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT")
    ?? "http://otel-collector:4317";

builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService("analysis-service"))
    .WithTracing(t => t
        .AddAspNetCoreInstrumentation()
        .AddGrpcClientInstrumentation()
        .AddOtlpExporter(o => o.Endpoint = new Uri(otlpEndpoint)));

WebApplication app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseAuthentication();
app.UseAuthorization();
app.MapAnalysisEndpoints();

// Startup bot-mismatch check: purge cached results when the default bot has changed.
AnalysisMetaRepository startupMetaRepo = app.Services.GetRequiredService<AnalysisMetaRepository>();
Database.DatabaseClient startupDb = app.Services.GetRequiredService<Database.DatabaseClient>();
string defaultBotId = app.Services.GetRequiredService<IOptions<AnalysisConfig>>().Value.DefaultBotId;
string? storedBotId = await startupMetaRepo.GetStoredBotIdAsync(CancellationToken.None);
if (storedBotId != defaultBotId)
{
    await startupDb.DeleteWhereAsync(
        new DeleteWhereRequest { Collection = "analysis_results", Filter = new Struct() });
    await startupMetaRepo.UpsertStoredBotIdAsync(defaultBotId, CancellationToken.None);
}

await app.RunAsync();
