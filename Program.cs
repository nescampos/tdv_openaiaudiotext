using OpenAI.GPT3.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHttpClient();
var openAIKey = builder.Configuration["OpenAISecret"];
builder.Services.AddOpenAIService(settings => { settings.ApiKey = openAIKey; });
builder.Services.AddControllers();

var app = builder.Build();

app.MapControllers();

app.Run();
