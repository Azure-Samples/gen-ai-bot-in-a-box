
global using Microsoft.AspNetCore.Hosting;
global using Microsoft.Extensions.Hosting;
global using Microsoft.Extensions.Logging;

global using System.IdentityModel.Tokens.Jwt;
global using Microsoft.Bot.Builder.Teams;
global using Microsoft.Bot.Connector.Authentication;

global using System;
global using System.ClientModel;
global using System.Collections.Generic;
global using System.Linq;
global using System.Net.Http;
global using System.Text.Json;
global using System.Threading;
global using System.Threading.Tasks;
global using Azure.AI.OpenAI;
global using Microsoft.Bot.Builder;
global using Microsoft.Bot.Builder.Dialogs;
global using Microsoft.Bot.Schema;
global using Microsoft.BotBuilderSamples;
global using Microsoft.Extensions.Configuration;
global using OpenAI.Assistants;
global using OpenAI.Chat;
global using OpenAI.Files;