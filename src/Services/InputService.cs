using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Discord;
using Discord.Net;
using Discord.Commands;
using Discord.WebSocket;
using PacManBot.Games;
using PacManBot.Utils;
using PacManBot.Constants;
using PacManBot.Extensions;

namespace PacManBot.Services
{
    public class InputService
    {
        private readonly DiscordShardedClient client;
        private readonly CommandService commands;
        private readonly StorageService storage;
        private readonly LoggingService logger;
        private readonly IServiceProvider provider;

        public readonly Regex waka = new Regex(@"^(w+a+k+a+\W*)+$", RegexOptions.IgnoreCase);


        public InputService(IServiceProvider provider)
        {
            this.provider = provider;
            client = provider.Get<DiscordShardedClient>();
            commands = provider.Get<CommandService>();
            storage = provider.Get<StorageService>();
            logger = provider.Get<LoggingService>();

            // Events
            client.MessageReceived += OnMessageReceived;
            client.ReactionAdded += OnReactionAdded;
            client.ReactionRemoved += OnReactionRemoved;
        }



        private Task OnMessageReceived(SocketMessage m)
        {
            _ = OnMessageReceivedAsync(m); // Discarding allows the async code to run without blocking the gateway task
            return Task.CompletedTask;
        }


        private Task OnReactionAdded(Cacheable<IUserMessage, ulong> m, ISocketMessageChannel c, SocketReaction r)
        {
            _ = OnReactionChangedAsync(m, c, r);
            return Task.CompletedTask;
        }


        private Task OnReactionRemoved(Cacheable<IUserMessage, ulong> m, ISocketMessageChannel c, SocketReaction r)
        {
            _ = OnReactionChangedAsync(m, c, r);
            return Task.CompletedTask;
        }




        private async Task OnMessageReceivedAsync(SocketMessage genericMessage)
        {
            try // I have to wrap discarded async methods in a try block so that exceptions don't go silent
            {
                if (storage.BannedChannels.Contains(genericMessage.Channel.Id)) // After a little bot-breaking incident
                {
                    if (genericMessage.Channel is IGuildChannel guildChannel) await guildChannel.Guild.LeaveAsync();
                    return;
                }

                if (client.CurrentUser != null && genericMessage is SocketUserMessage message
                    && !message.Author.IsBot && message.Channel.BotCan(ChannelPermission.SendMessages))
                {
                    // Only runs one
                    _ = await MessageGameInputAsync(message) || await CommandAsync(message) || await AutoresponseAsync(message);
                }
            }
            catch (Exception e)
            {
                await logger.Log(LogSeverity.Error, $"{e}");
            }
        }


        private async Task OnReactionChangedAsync(Cacheable<IUserMessage, ulong> messageData, ISocketMessageChannel channel, SocketReaction reaction)
        {
            try
            {   // Maybe someone will one day make a bot that plays this bot
                if (client.CurrentUser != null && reaction.UserId != client.CurrentUser.Id)
                {
                    await ReactionGameInputAsync(messageData, channel, reaction);
                }
            }
            catch (Exception e)
            {
                await logger.Log(LogSeverity.Error, $"{e}");
            }
        }




        private async Task<bool> CommandAsync(SocketUserMessage message)
        {
            var context = new ShardedCommandContext(client, message);

            string prefix = storage.GetPrefix(context.Guild);
            int commandPosition = 0;
            
            if (message.HasMentionPrefix(client.CurrentUser, ref commandPosition)
                || message.HasStringPrefix($"{prefix} ", ref commandPosition) || message.HasStringPrefix(prefix, ref commandPosition)
                || context.Channel is IDMChannel || storage.NoPrefixChannel(context.Channel.Id))
            {
                var result = await commands.ExecuteAsync(context, commandPosition, provider);

                if (result.IsSuccess) return true;
                else if (!result.ErrorReason.Contains("Unknown command"))
                {
                    await logger.Log(LogSeverity.Verbose, LogSource.Command,
                                     $"\"{message}\" by {message.Author.FullName()} in {context.Channel.FullName()} " +
                                     $"couldn't be executed. {result.ErrorReason}");

                    string reply = CommandErrorReply(result.ErrorReason, context.Guild);
                    if (reply != null && context.BotCan(ChannelPermission.SendMessages))
                    {
                        await context.Channel.SendMessageAsync(reply, options: Bot.DefaultOptions);
                    }

                    return true;
                }
            }

            return false;
        }


        private async Task<bool> AutoresponseAsync(SocketUserMessage message)
        {
            if (!(message.Channel is SocketGuildChannel gChannel) || storage.AllowsAutoresponse(gChannel.Guild.Id)
                || storage.AppInfo?.Owner.Id == message.Author.Id)
            {
                if (waka.IsMatch(message.Content))
                {
                    await message.Channel.SendMessageAsync("waka", options: Bot.DefaultOptions);
                    await logger.Log(LogSeverity.Verbose, $"Waka at {message.Channel.FullName()}");
                    return true;
                }
                else if (message.Content == "sudo neat")
                {
                    await message.Channel.SendMessageAsync("neat", options: Bot.DefaultOptions);
                    return true;
                }
            }

            return false;
        }


        private async Task<bool> MessageGameInputAsync(SocketUserMessage message)
        {
            var game = storage.GetChannelGame<IMessagesGame>(message.Channel.Id);
            if (game == null || !game.IsInput(message.Content, message.Author.Id)) return false;

            try
            {
                await ExecuteGameInputAsync(game, message);
            }
            catch (Exception e) when (e is OperationCanceledException || e is TimeoutException) { }
            catch (HttpException e)
            {
                await logger.Log(LogSeverity.Warning, LogSource.Game,
                                 $"During {game.GetType().Name} input in {game.ChannelId}: {e.Message}");
            }

            return true;
        }


        private async Task<bool> ReactionGameInputAsync(Cacheable<IUserMessage, ulong> messageData, ISocketMessageChannel channel, SocketReaction reaction)
        {
            var game = storage.GetChannelGame<IReactionsGame>(channel.Id);
            if (game == null || game.MessageId != reaction.MessageId || !game.IsInput(reaction.Emote, reaction.UserId)) return false;

            try
            {
                await ExecuteGameInputAsync(game, reaction, await messageData.GetOrDownloadAsync());
            }
            catch (Exception e) when (e is OperationCanceledException || e is TimeoutException) { }
            catch (HttpException e)
            {
                await logger.Log(LogSeverity.Warning, game.Name, $"During input in {game.ChannelId}: {e.Message}");
            }

            return true;
        }




        private async Task ExecuteGameInputAsync(IMessagesGame game, IUserMessage message)
        {
            var gameMessage = await game.GetMessage();

            await logger.Log(LogSeverity.Verbose, game.Name,
                             $"Input {message.Content} by {message.Author.FullName()} in {message.Channel.FullName()}");

            game.Input(message.Content, message.Author.Id);
            if (game is MultiplayerGame mGame)
            {
                while(mGame.BotTurn) mGame.BotInput();
            }
            if (game.State != State.Active) storage.DeleteGame(game);

            game.CancelRequests();
            var requestOptions = game.RequestOptions;

            if (gameMessage != null && message.Channel.BotCan(ChannelPermission.ManageMessages))
            {
                await gameMessage.ModifyAsync(game.UpdateMessage, requestOptions);
                await message.DeleteAsync(Bot.DefaultOptions);
            }
            else
            {
                var newMsg = await message.Channel.SendMessageAsync(game.GetContent(), false, game.GetEmbed()?.Build(), requestOptions);
                game.MessageId = newMsg.Id;
                if (gameMessage != null) await gameMessage.DeleteAsync(Bot.DefaultOptions);
            }
        }


        private async Task ExecuteGameInputAsync(IReactionsGame game, SocketReaction reaction, IUserMessage gameMessage)
        {
            var user = reaction.User.IsSpecified ? reaction.User.Value : client.GetUser(reaction.UserId);
            var channel = gameMessage.Channel;
            var guild = (channel as IGuildChannel)?.Guild;

            await logger.Log(LogSeverity.Verbose, game.Name, 
                             $"Input {PacManGame.GameInputs[reaction.Emote].ToString().Align(5)} " +
                             $"by {user.FullName()} in {channel.FullName()}");

            game.Input(reaction.Emote, user.Id);

            if (game.State != State.Active)
            {
                storage.DeleteGame(game);

                if (game is PacManGame pmGame && pmGame.State != State.Cancelled && !pmGame.custom)
                {
                    storage.AddScore(new ScoreEntry(pmGame.score, user.Id, pmGame.State, pmGame.Time,
                        user.NameandNum(), $"{guild?.Name}/{channel.Name}", DateTime.Now));
                }
                if (channel.BotCan(ChannelPermission.ManageMessages))
                {
                    await gameMessage.RemoveAllReactionsAsync(Bot.DefaultOptions);
                }
            }

            game.CancelRequests();
            await gameMessage.ModifyAsync(game.UpdateMessage, game.RequestOptions);
        }




        private string CommandErrorReply(string error, SocketGuild guild)
        {
            string help = $"Please use `{storage.GetPrefixOrEmpty(guild)}help [command name]` or try again.";

            if (error.Contains("requires") && guild == null)
                return "You need to be in a guild to use this command!";

            if (error.Contains("Bot requires"))
                return $"This bot is missing the permission**{Regex.Replace(error.Split(' ').Last(), @"([A-Z])", @" $1")}**!";

            if (error.Contains("User requires"))
                return $"You need the permission**{Regex.Replace(error.Split(' ').Last(), @"([A-Z])", @" $1")}** to use this command!";

            if (error.Contains("User not found"))
                return "Can't find the specified user!";

            if (error.Contains("Failed to parse"))
                return $"Invalid command parameters! {help}";

            if (error.Contains("too few parameters"))
                return $"Missing command parameters! {help}";

            if (error.Contains("too many parameters"))
                return $"Too many parameters! {help}";

            if (error.Contains("must be used in a guild"))
                return "You need to be in a guild to use this command!";

            if (error.ContainsAny("quoted parameter", "one character of whitespace"))
                return "Incorrect use of quotes in command parameters.";

            if (error.Contains("Timeout"))
                return "You're using that command too much. Please try again later.";

            return null;
        }
    }
}
