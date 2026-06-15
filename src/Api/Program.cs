using Api.Models;
using Api.Services;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);

// Load .env (DATABASE_URL etc.) — secrets live there, never in appsettings.
LoadDotEnv(builder.Environment.ContentRootPath);

builder.Services.AddOpenApi();

// Register a single shared NpgsqlDataSource built from DATABASE_URL.
var databaseUrl = Environment.GetEnvironmentVariable("DATABASE_URL")
    ?? throw new InvalidOperationException("DATABASE_URL is not set. Copy .env.example to .env.");

builder.Services.AddSingleton(NpgsqlDataSource.Create(databaseUrl));

builder.Services.AddScoped<IngestService>();
builder.Services.AddScoped<GenerateService>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

// POST /ingest — chunk → embed → store. (stub)
app.MapPost("/ingest", (IngestRequest request, IngestService service) =>
{
    return Results.Problem("Not implemented", statusCode: StatusCodes.Status501NotImplemented);
});

// POST /generate — retrieve → Claude → draft. (stub)
app.MapPost("/generate", (GenerateRequest request, GenerateService service) =>
{
    return Results.Problem("Not implemented", statusCode: StatusCodes.Status501NotImplemented);
});

// GET /health — liveness check. (stub)
app.MapGet("/health", () =>
{
    return Results.Problem("Not implemented", statusCode: StatusCodes.Status501NotImplemented);
});

app.Run();

// Minimal .env loader: walk up from the content root and set any KEY=VALUE pairs
// not already present in the environment. Avoids an extra package dependency.
static void LoadDotEnv(string startPath)
{
    var dir = new DirectoryInfo(startPath);
    while (dir is not null)
    {
        var envPath = Path.Combine(dir.FullName, ".env");
        if (File.Exists(envPath))
        {
            foreach (var rawLine in File.ReadAllLines(envPath))
            {
                var line = rawLine.Trim();
                if (line.Length == 0 || line.StartsWith('#'))
                {
                    continue;
                }

                var separator = line.IndexOf('=');
                if (separator <= 0)
                {
                    continue;
                }

                var key = line[..separator].Trim();
                var value = line[(separator + 1)..].Trim();

                if (Environment.GetEnvironmentVariable(key) is null)
                {
                    Environment.SetEnvironmentVariable(key, value);
                }
            }

            return;
        }

        dir = dir.Parent;
    }
}
