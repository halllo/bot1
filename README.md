# bot1

Experimental chat bot for use cases within Microsoft Teams.

## Resources

1. Create an Azure Bot resource (Free tier) as a Single Tenant bot
2. Create an Azure App Service resource (free tier)
3. Setup deployment via GitHub action
4. Push an ASP.NET Core project inspired by [official sample code](https://github.com/microsoft/BotBuilder-Samples/tree/main/samples/csharp_dotnetcore)
5. Add `MicrosoftApp...` settings via Environment Variables or Azure KeyVault (add new `MicrosoftAppPassword` secret to the identity of the Azure Bot resource)
6. Configure the Azure Bot resource to point to the [public endpoint](https://bot1-backend-ayhgfpd0a9d9a7hr.germanywestcentral-01.azurewebsites.net) `/api/messages` of the app service

## Debugging

Instead of merely referencing the nuget packages

```xml
<PackageReference Include="Microsoft.Bot.Builder.Integration.AspNet.Core" Version="4.22.9" />
<PackageReference Include="Microsoft.Bot.Builder.Dialogs" Version="4.22.9" />
```

we can reference the [code](https://github.com/microsoft/botbuilder-dotnet) of it instead, to debug the bot framework.

```xml
<ItemGroup>
  <ProjectReference Include="..\..\..\botbuilder-dotnet\libraries\integration\Microsoft.Bot.Builder.Integration.AspNet.Core\Microsoft.Bot.Builder.Integration.AspNet.Core.csproj" />
  <ProjectReference Include="..\..\..\botbuilder-dotnet\libraries\Microsoft.Bot.Builder.Dialogs\Microsoft.Bot.Builder.Dialogs.csproj" />
</ItemGroup>
```

Once the debugging session has started, we can open a [devtunnel](https://learn.microsoft.com/en-us/azure/developer/dev-tunnels/get-started?tabs=windows).

```powershell 
devtunnel host -p 7106 --protocol https -a
```

The public tunnel address we provide as the Messaging endpoint of the Azure Bot Configuration.

Now we can interact with the bot via teams or webchat and debug it within Visual Studio locally.

## Remote Authentication

Add OAuth 2.0 authentication and settings like <https://stackoverflow.com/a/77832960> (Generic Oauth 2).

We use the [experimental IdentityServer](https://identityserver-dghzawaudva6ewgb.germanywestcentral-01.azurewebsites.net) from [PermissionedNotes](https://github.com/halllo/PermissionedNotes).

To make authentication work with MS Teams, the bot needs to overwrite `OnTeamsSigninVerifyStateAsync` to forward the received auth code (received in the form of a Teams-specific invoke action) to the `OAuthPrompt`, like explained [here](https://learn.microsoft.com/en-us/microsoftteams/platform/bots/how-to/authentication/add-authentication?tabs=dotnet%2Cdotnet-sample).
