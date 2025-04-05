using AgentDo;
using AgentDo.OpenAI;
using bot1;
using bot1.Dialogs;
using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.Integration.AspNet.Core;
using Microsoft.Bot.Connector.Authentication;
using OpenAI.Chat;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddSingleton<BotFrameworkAuthentication, ConfigurationBotFrameworkAuthentication>();
builder.Services.AddSingleton<IBotFrameworkHttpAdapter, AdapterWithErrorHandler>();
builder.Services.AddSingleton<IStorage, MemoryStorage>();
builder.Services.AddSingleton<UserState>();
builder.Services.AddSingleton<ConversationState>();
builder.Services.AddSingleton<MainDialog>();
builder.Services.AddTransient<IBot, AuthBot<MainDialog>>();

var config = builder.Configuration;
builder.Services.AddSingleton(sp => new ChatClient(
	model: "gpt-4o",
	apiKey: config["OPENAI_API_KEY"]!));

builder.Services.AddSingleton<IAgent, OpenAIAgent>();
builder.Services.Configure<OpenAIAgentOptions>(o =>
{
	o.Temperature = 0.0f;
	o.SystemPrompt = "You are a helpful assistant. When asked what you can do, please respond with the list of tools available to you.";
});


var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();

if (app.Environment.IsDevelopment())
{
	app.UseDeveloperExceptionPage();
}
else
{
	app.UseHttpsRedirection();
}

app.UseDefaultFiles();

app.UseStaticFiles();

app.UseWebSockets();

app.UseRouting();

app.UseAuthorization();

app.MapControllers();

app.Run();
