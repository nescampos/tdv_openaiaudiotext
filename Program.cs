using OpenAI.GPT3.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHttpClient();
builder.Services.AddOpenAIService();
builder.Services.AddControllers();

var app = builder.Build();

app.MapControllers();

app.Run();
