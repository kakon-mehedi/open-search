using Microsoft.EntityFrameworkCore;
using OpenSearch.Client;
using OpenSearch.Net;
using OpenSearchLearningLab.Data;
using OpenSearchLearningLab.OpenSearch;
using OpenSearchLearningLab.Services;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();

// Swagger UI purely as a way to browse and try every endpoint from the
// browser instead of hand-typing curl commands — no bearing on OpenSearch
// itself, just a convenience for exploring this project.
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Postgres")));

// --- OpenSearch client setup ---------------------------------------------
//
// DisableDirectStreaming() + OnRequestCompleted() below exist purely for
// requirement #20 in the brief: "show the actual OpenSearch requests".
// By default the client streams JSON straight to the socket and never
// materializes it as a string. Disabling that costs a bit of performance
// (irrelevant for a learning lab) in exchange for being able to log the
// EXACT request body and response body for every call — the same JSON
// you could paste into curl or OpenSearch Dashboards' Dev Tools console.
var openSearchUrl = builder.Configuration["OpenSearch:Url"] ?? "http://localhost:9200";
var pool = new SingleNodeConnectionPool(new Uri(openSearchUrl));
var connectionSettings = new ConnectionSettings(pool)
    .DefaultIndex(OpenSearchIndexSetup.IndexName)
    .DisableDirectStreaming()
    .OnRequestCompleted(callDetails =>
    {
        var logger = LoggerFactory.Create(b => b.AddConsole()).CreateLogger("OpenSearchRequest");
        var request = callDetails.RequestBodyInBytes is null
            ? "(no body)"
            : Encoding.UTF8.GetString(callDetails.RequestBodyInBytes);
        logger.LogInformation(
            "OpenSearch {Method} {Uri}\nRequest body:\n{Request}",
            callDetails.HttpMethod, callDetails.Uri, request);
    });

builder.Services.AddSingleton<IOpenSearchClient>(new OpenSearchClient(connectionSettings));
builder.Services.AddScoped<ProductService>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapControllers();

// --- Startup setup --------------------------------------------------------
//
// Two explicit setup steps happen before the app accepts traffic:
//   1. Ensure the PostgreSQL schema exists (products table).
//   2. Ensure the OpenSearch index exists WITH the explicit mapping from
//      OpenSearchIndexSetup — we do not let OpenSearch invent field types
//      from the first document it happens to receive.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.EnsureCreatedAsync();

    var openSearchClient = scope.ServiceProvider.GetRequiredService<IOpenSearchClient>();
    await OpenSearchIndexSetup.EnsureIndexAsync(openSearchClient);

    await SeedData.SeedIfEmptyAsync(db, openSearchClient);
}

app.Run();
