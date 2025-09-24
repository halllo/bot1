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
		record ConversationHistory(DateTimeOffset LastResponse, AgentResult AgentResult);

		public MainDialog(IConfiguration configuration, IAgent agent, ILogger<MainDialog> logger, ConversationState conversationState)
			: base(nameof(MainDialog), configuration["ConnectionName"]!)
		{
			Logger = logger;

			var conversationStateAccessors = conversationState.CreateProperty<ConversationData>(nameof(ConversationData));
			var conversationHistoryAccessors = conversationState.CreateProperty<ConversationHistory>(nameof(ConversationHistory));

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
					var tokenResponse = (TokenResponse)stepContext.Result;
					if (tokenResponse != null)
					{
						// The OAuthPrompt returns a TokenResponse which contains the access token.
						var accessToken = tokenResponse.Token;
						var jwtReader = new JsonWebTokenHandler();
						var jwt = jwtReader.ReadJsonWebToken(accessToken);
						var username = jwt.GetClaim("name").Value;
						var userId = jwt.GetClaim("sub").Value;

						var ask = stepContext.Values["ask"] as string;
						if (!string.IsNullOrEmpty(ask))
						{
							var systemPrompt = new Message { Role = "System", Text = "You are a helpful assistant. When asked what you can do, please respond with the list of tools available to you." };
							var toolUsed = false;
							var forgetPreviousMessages = false;

							// Retrieve the previous messages and pending tool uses from the conversation state.
							var history = await conversationHistoryAccessors.GetAsync(stepContext.Context);
							var previousResult = history?.LastResponse > DateTimeOffset.UtcNow.AddMinutes(-5) ? history?.AgentResult : new AgentResult { Messages = [systemPrompt] };

							// Invoke the agent.
							var result = await agent.Do(
								task: new AgentDo.Content.Prompt(ask, previousResult),
								tools:
								[
									Tool.From([Description("Get access token claims (users identity).")] async (Tool.Context context) =>
									{
										context.Cancelled = true;
										context.RememberToolResultWhenCancelled = true;
										toolUsed = true;
										var claims = jwt.Claims.Select(c => new { c.Type, c.Value });
										var claimsJson = JsonSerializer.Serialize(claims, new JsonSerializerOptions { WriteIndented = true });
										var responseText = $"{context.Text}\n\n<pre>{claimsJson}</pre>";
										await stepContext.Context.SendActivityAsync(MessageFactory.Text(responseText), cancellationToken);
									}),
									Tool.From([Description("Forget previous messages.")] async (Tool.Context context) =>
									{
										context.Cancelled = true;
										context.RememberToolResultWhenCancelled = true;
										toolUsed = true;
										forgetPreviousMessages = true;
										var responseText = $"{context.Text}\n\n<pre>forgotten</pre>";
										await stepContext.Context.SendActivityAsync(MessageFactory.Text(responseText), cancellationToken);
									}, requireApproval: true),
									Tool.From([Description("Brain inspection.")] async (Tool.Context context) =>
									{
										context.Cancelled = true;
										context.RememberToolResultWhenCancelled = true;
										toolUsed = true;
										var responseText = $"{context.Text}\n\n<pre>{JsonSerializer.Serialize(previousResult?.Messages)}</pre>";
										await stepContext.Context.SendActivityAsync(MessageFactory.Text(responseText), cancellationToken);
									}),
								]);

							// Update the conversation state with the new messages and pending tool uses.
							result.Agent = null!;// Some things cannot be serialized by newtonsoft.json
							result.Task = null!;
							result.Tools = null!;
							result.Messages = forgetPreviousMessages ? [systemPrompt] : result.Messages;
							await conversationHistoryAccessors.SetAsync(stepContext.Context, new ConversationHistory(DateTimeOffset.UtcNow, result), cancellationToken);

							// If the agent wants to use an approvable tool, we need to include the human in the loop.
							if (result.NeedsApprovalToContinue)
							{
								return await stepContext.BeginDialogAsync(nameof(ConfirmPrompt), new PromptOptions { Prompt = MessageFactory.Text($"Invoke '{result.Approvable?.ToolName}'?") }, cancellationToken);
							}
							// If the agent didn't use a tool, we can just return the response.
							else if (!toolUsed)
							{
								var agentResponse = result.Messages.Last().Text;
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
				async (WaterfallStepContext stepContext, CancellationToken cancellationToken) =>
				{
					var confirmed = (bool)stepContext.Result;
					var history = await conversationHistoryAccessors.GetAsync(stepContext.Context);
					var approvable = history?.AgentResult.Approvable;
					if (confirmed && approvable != null)
					{
						// The user confirmed the tool use, so we can proceed with the tool call be starting the dialog again.
						approvable.Approved = true;

						var updatedHistory = new ConversationHistory(DateTimeOffset.UtcNow, history!.AgentResult);
						await conversationHistoryAccessors.SetAsync(stepContext.Context, updatedHistory, cancellationToken);
						return await stepContext.BeginDialogAsync(nameof(WaterfallDialog), null, cancellationToken);
					}
					else
					{
						// The user rejected the tool use, so we need to close the open tool call.
						var updatedPreviousMessages = new ConversationHistory(
							LastResponse: DateTimeOffset.UtcNow,
							AgentResult: new AgentResult
							{
								Messages = [
									..history!.AgentResult.Messages,
									approvable != null
										? new Message { Role = "Tool", ToolResults = [new Message.ToolResult { Id = approvable.ToolUseId, Output = "tool invocation rejected" }] }
										: new Message { Role = "Assistant", Text = "Understood. Not invoking the tool." }
								],
								PendingToolUses = null,
							});
						await conversationHistoryAccessors.SetAsync(stepContext.Context, updatedPreviousMessages, cancellationToken);
						await stepContext.Context.SendActivityAsync(MessageFactory.Text("Invocation rejected. Dialog ends."), cancellationToken);
					}

					return await stepContext.EndDialogAsync(cancellationToken: cancellationToken);
				}
			]));

			// The initial child Dialog to run.
			InitialDialogId = nameof(WaterfallDialog);
		}
	}
}
