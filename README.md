# bot1

Experimental chat bot for use cases within Microsoft Teams.

## Resources

1. Create an Azure Bot resource (Free tier) as a Single Tenant bot
2. Create an Azure App Service resource (free tier)
3. Setup deployment via GitHub action
4. Push an ASP.NET Core project inspired by [official sample code](https://github.com/microsoft/BotBuilder-Samples/tree/main/samples/csharp_dotnetcore)
5. Add `MicrosoftApp...` settings via Environment Variables or Azure KeyVault (add new `MicrosoftAppPassword` secret to the identity of the Azure Bot resource)
6. Configure the Azure Bot resource to point to the public endpoint of the app service

## Debugging

Instead of merely referencing the nuget package

```xml
<PackageReference Include="Microsoft.Bot.Builder.Integration.AspNet.Core" Version="4.22.9" />
```

we can reference the [code](https://github.com/microsoft/botbuilder-dotnet) of it instead, to debug the bot framework.

```xml
<ItemGroup>
  <ProjectReference Include="..\..\..\botbuilder-dotnet\libraries\integration\Microsoft.Bot.Builder.Integration.AspNet.Core\Microsoft.Bot.Builder.Integration.AspNet.Core.csproj" />
</ItemGroup>
```

Once the debugging session has started, we can open a [devtunnel](https://learn.microsoft.com/en-us/azure/developer/dev-tunnels/get-started?tabs=windows).

```powershell 
devtunnel host -p 7106 --protocol https -a
```

Now we can interact with the bot via teams or webchat and debug it within Visual Studio locally.
