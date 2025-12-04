using ResumeScannerApp.Config;
using ResumeScannerApp.Interfaces;
using ResumeScannerApp.Services;
using System.Text.Json;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);
var config = builder.Configuration;
// Add services to the container.

// Bind options
var aiOptions = new AzureOpenAiOptions();
config.GetSection("AzureOpenAI").Bind(aiOptions);

//builder.Services.AddControllers();

builder.Services.AddControllers()
    .AddJsonOptions(opts =>
    {
        // allow "Contains" / "contains" etc. to bind to enum values
        opts.JsonSerializerOptions.PropertyNameCaseInsensitive = true;
        opts.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
    });

//builder.Services.AddControllers().AddNewtonsoftJson();

// Add services (interfaces -> implementations)
builder.Services.AddSingleton(aiOptions);
builder.Services.AddHttpClient(); // for AzureOpenAiService


// Register DI (small interfaces)
builder.Services.AddSingleton<ITextExtractor,PdfAndDocxTextExtractor>();

//builder.Services.AddSingleton<ITextExtractor, TikaTextExtractor>();
builder.Services.AddSingleton<IOpenAiClient>(sp =>
{
    var http = sp.GetRequiredService<IHttpClientFactory>().CreateClient();
    return new AzureOpenAiService(http, aiOptions);
});
builder.Services.AddSingleton<IResumeParser, ResumeParserService>();
builder.Services.AddSingleton<IStorageProvider, LocalStorageProvider>();


builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});


var app = builder.Build();

app.UseCors("AllowAll");


// Configure the HTTP request pipeline.

app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();

app.Run();
