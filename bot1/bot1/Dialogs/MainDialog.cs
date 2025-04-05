using AgentDo;
using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.Dialogs;
using Microsoft.Bot.Schema;
using Microsoft.IdentityModel.JsonWebTokens;
using System.ComponentModel;
using System.Text.Json;

namespace bot1.Dialogs
{
	public class MainDialog : LogoutDialog
	{
		protected readonly ILogger Logger;

		record ConversationData(string Context);

		public MainDialog(IConfiguration configuration, IAgent agent, ILogger<MainDialog> logger, ConversationState conversationState)
			: base(nameof(MainDialog), configuration["ConnectionName"]!)
		{
			Logger = logger;

			var conversationStateAccessors = conversationState.CreateProperty<ConversationData>(nameof(ConversationData));

			AddDialog(new OAuthPrompt(
				nameof(OAuthPrompt),
				new OAuthPromptSettings
				{
					ConnectionName = ConnectionName,
					Text = "Please Sign In",
					Title = "Sign In",
					Timeout = 300000, // User has 5 minutes to login (1000 * 60 * 5),
					EndOnInvalidMessage = true,
				}));

			AddDialog(new ConfirmPrompt(nameof(ConfirmPrompt)));

			AddDialog(new WaterfallDialog(nameof(WaterfallDialog),
			[
				async (WaterfallStepContext stepContext, CancellationToken cancellationToken) =>
				{
					var message = stepContext.Context.Activity.AsMessageActivity();
					if (message != null)
					{
						stepContext.Values["ask"] = message.Text;

						// Call the oauth prompt everytime we need the token. The reason for this are:
						// 1. If the user is already logged in we do not need to store the token locally in the bot and worry
						// about refreshing it. We can always just call the prompt again to get the token.
						// 2. We never know how long it will take a user to respond. By the time the
						// user responds the token may have expired. The user would then be prompted to login again.
						//
						// There is no reason to store the token locally in the bot because we can always just call
						// the OAuth prompt to get the token or get a new token if needed.
						return await stepContext.BeginDialogAsync(nameof(OAuthPrompt), null, cancellationToken);
					}
					else
					{
						await stepContext.Context.SendActivityAsync(MessageFactory.Text($"Sorry, I only accept message activities."), cancellationToken);
						return await stepContext.EndDialogAsync(cancellationToken: cancellationToken);
					}
				},
				async (WaterfallStepContext stepContext, CancellationToken cancellationToken) =>
				{
                    // Get the token from the previous step. Note that we could also have gotten the
                    // token directly from the prompt itself. There is an example of this in the next method.
                    var tokenResponse = (TokenResponse)stepContext.Result;
					if (tokenResponse != null)
					{
						var accessToken = tokenResponse.Token;
						var jwtReader = new JsonWebTokenHandler();
						var jwt = jwtReader.ReadJsonWebToken(accessToken);
						var username = jwt.GetClaim("name").Value;
						var userId = jwt.GetClaim("sub").Value;

						var ask = stepContext.Values["ask"] as string;
						if (!string.IsNullOrEmpty(ask))
						{
							var agentMessages = await agent.Do(
								task: new AgentDo.Content.Prompt(ask),
								tools:
								[
									Tool.From([Description("Get access token claims.")] async (Tool.Context context) =>
									{
										context.Cancelled = true;
										var claims = jwt.Claims.Select(c => new { c.Type, c.Value });
										var claimsJson = JsonSerializer.Serialize(claims, new JsonSerializerOptions { WriteIndented = true });
										var responseText = $"Here are your claims:\n\n<pre>{claimsJson}</pre>";
										await stepContext.Context.SendActivityAsync(MessageFactory.Text(responseText), cancellationToken);
									}),
									Tool.From([Description("Remember context.")] async (string ctx, Tool.Context context) =>
									{
										context.Cancelled = true;
										await conversationStateAccessors.SetAsync(stepContext.Context, new ConversationData(ctx));
										await stepContext.Context.SendActivityAsync(MessageFactory.Text($"Context saved: {ctx}"), cancellationToken);
									}),
									Tool.From([Description("Get remembered context.")] async (Tool.Context context) =>
									{
										context.Cancelled = true;
										var remembered = await conversationStateAccessors.GetAsync(stepContext.Context);
										if (remembered != null)
										{
											await stepContext.Context.SendActivityAsync(MessageFactory.Text($"Context remembered: {remembered.Context}"), cancellationToken);
										}
										else
										{
											await stepContext.Context.SendActivityAsync(MessageFactory.Text($"No context remembered yet."), cancellationToken);
										}
									}),
								]);

							if (!agentMessages.Any(m => m.ToolCalls?.Any() ?? false))
							{
								var agentResponse = agentMessages.SkipWhile(m => m.Role != "Assistant").Select(m => m.Text).FirstOrDefault();
								if (!string.IsNullOrWhiteSpace(agentResponse))
								{
									await stepContext.Context.SendActivityAsync(MessageFactory.Text(agentResponse), cancellationToken);
								}
								else
								{
									await stepContext.Context.SendActivityAsync(MessageFactory.Text($"Hello {username}. You asked for '{ask}'. Unfortunately I dont understand that yet."), cancellationToken);
								}
							}
						}
						else
						{
							await stepContext.Context.SendActivityAsync(MessageFactory.Text($"Hello {username}. How can I help you?"), cancellationToken);
						}

						return await stepContext.EndDialogAsync(cancellationToken: cancellationToken);
					}
					else
					{
						await stepContext.Context.SendActivityAsync(MessageFactory.Text("Login was not successful please try again."), cancellationToken);
						return await stepContext.EndDialogAsync(cancellationToken: cancellationToken);
					}
				},
			]));

			// The initial child Dialog to run.
			InitialDialogId = nameof(WaterfallDialog);
		}
	}
}
