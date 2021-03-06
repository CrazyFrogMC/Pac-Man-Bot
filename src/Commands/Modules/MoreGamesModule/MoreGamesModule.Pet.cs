﻿using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using Discord.WebSocket;
using PacManBot.Games;
using PacManBot.Constants;
using PacManBot.Extensions;

namespace PacManBot.Commands
{
    public partial class MoreGamesModule
    {
        [AttributeUsage(AttributeTargets.Method)]
        class PetCommandAttribute : Attribute
        {
            public string[] Names { get; }
            public PetCommandAttribute(params string[] names)
            {
                Names = names;
            }
        }



        [Command("pet"), Alias("clockagotchi"), Parameters("[command]"), Priority(-4)]
        [Remarks("Adopt your own pet!")]
        [Summary("**__Pet Commands__**\n\n" +
                 "**{prefix}pet** - Check on your pet or adopt if you don't have one\n" +
                 "**{prefix}pet stats** - Check your pet's statistics and achievements\n" +
                 "**{prefix}pet name <name>** - Name your pet\n" +
                 "**{prefix}pet image <image>** - Give your pet an image\n\n" +
                 "**{prefix}pet feed** - Fills your pet's Satiation and restores 2 Energy\n" +
                 "**{prefix}pet play** - Fills your pet's Happinness and consumes 5 Energy\n" +
                 "**{prefix}pet clean** - Fills your pet's Hygiene\n" +
                 "**{prefix}pet sleep/wakeup** - Sleep to restore Energy over time\n\n" +
                 "**{prefix}pet help** - This list of commands\n" +
                 "**{prefix}pet pet** - Pet your pet\n" +
                 "**{prefix}pet user <user>** - See another person's pet\n" +
                 "**{prefix}pet release** - Gives your pet to a loving family that will take care of it (Deletes pet forever)")]
        [BetterRequireBotPermission(ChannelPermission.EmbedLinks | ChannelPermission.AddReactions)]
        public async Task PetMaster(string commandName = "", [Remainder]string args = null)
        {
            var command = typeof(MoreGamesModule).GetMethods()
                .FirstOrDefault(x => x.GetCustomAttribute<PetCommandAttribute>()?.Names.Contains(commandName.ToLower()) ?? false);

            if (command == null)
            {
                await ReplyAsync($"Unknown pet command! Do `{Prefix}pet help` for help");
            }
            else
            {
                var pet = Storage.GetUserGame<PetGame>(Context.User.Id);
                if (pet == null)
                {
                    if (commandName == "")
                    {
                        pet = new PetGame("", Context.User.Id, Services);
                        Storage.AddGame(pet);
                    }
                    else
                    {
                        await ReplyAsync($"You don't have a pet yet! Simply do `{Prefix}pet` to adopt one.");
                        return;
                    }
                }

                await (Task)command.Invoke(this, new object[] { pet, args });
            }
        }



        [PetCommand("")]
        public async Task Pet(PetGame pet, string args)
        {
            await ReplyAsync(pet.GetContent(), pet.GetEmbed(Context.User as IGuildUser));
        }


        [PetCommand("exact", "precise", "decimals", "float", "double")]
        public async Task PetExact(PetGame pet, string args)
        {
            var user = Context.User as SocketGuildUser;

            if (!string.IsNullOrWhiteSpace(args))
            {
                var other = await Context.ParseUserAsync(args);
                if (other != null)
                {
                    user = other;
                    pet = Storage.GetUserGame<PetGame>(user.Id);
                    if (pet == null)
                    {
                        await ReplyAsync("This person doesn't have a pet :(");
                        return;
                    }
                }
            }

            await ReplyAsync(pet.GetContent(), pet.GetEmbed(user, decimals: true));
        }


        [PetCommand("stats", "achievements", "unlocks")]
        public async Task PetStats(PetGame pet, string args)
        {
            var user = Context.User as SocketGuildUser;

            if (!string.IsNullOrWhiteSpace(args))
            {
                var other = await Context.ParseUserAsync(args);
                if (other != null)
                {
                    user = other;
                    pet = Storage.GetUserGame<PetGame>(user.Id);
                    if (pet == null)
                    {
                        await ReplyAsync("This person doesn't have a pet :(");
                        return;
                    }
                }
            }

            await ReplyAsync(pet.GetEmbedAchievements(user));
        }


        [PetCommand("feed", "food", "eat", "hunger", "satiation")]
        public async Task PetFeed(PetGame pet, string args)
        {
            if (pet.Feed()) await Context.Message.AddReactionAsync(Bot.Random.Choose(PetGame.FoodEmotes).ToEmoji());
            else await ReplyAsync($"{CustomEmoji.Cross} Your pet is already full! (-1 happiness)");
        }


        [PetCommand("play", "fun", "happy", "happiness")]
        public async Task PetPlay(PetGame pet, string args)
        {
            if (pet.Play()) await Context.Message.AddReactionAsync(Bot.Random.Choose(PetGame.PlayEmotes).ToEmoji());
            else
            {
                string message = pet.energy.Ceiling() >= 5 ? "Your pet doesn't want to play anymore! (-1 happiness)" 
                                                           : "Your pet is too tired to play! It needs 5 energy or more.";
                await ReplyAsync($"{CustomEmoji.Cross} {message}");
            }
        }


        [PetCommand("clean", "hygiene", "wash")]
        public async Task PetClean(PetGame pet, string args)
        {
            if (pet.Clean()) await Context.Message.AddReactionAsync(Bot.Random.Choose(PetGame.CleanEmotes).ToEmoji());
            else await ReplyAsync($"{CustomEmoji.Cross} Your pet is already clean! (-1 happiness)");
        }


        [PetCommand("sleep", "rest", "energy")]
        public async Task PetSleep(PetGame pet, string args)
        {
            pet.UpdateStats(store: false);
            if (pet.energy.Ceiling() == PetGame.MaxStat && !pet.asleep)
            {
                pet.happiness = Math.Max(0, pet.happiness - 1);
                Storage.StoreGame(pet);
                await ReplyAsync($"{CustomEmoji.Cross} Your pet is not tired! (-1 happiness)");
            }
            else
            {
                string message = pet.asleep ? "Your pet is already sleeping."
                                            : "Your pet is now asleep.";
                await ReplyAsync($"{Bot.Random.Choose(PetGame.SleepEmotes)} {message}");
                if (!pet.asleep) pet.ToggleSleep();
            }
        }


        [PetCommand("wake", "wakeup", "awaken")]
        public async Task PetWake(PetGame pet, string args)
        {
            pet.UpdateStats(false);
            await ReplyAsync(pet.asleep ? "🌅 You wake up your pet." : "🌅 Your pet is already awake.");
            if (pet.asleep) pet.ToggleSleep();
        }


        [PetCommand("name")]
        public async Task PetName(PetGame pet, string args)
        {
            if (string.IsNullOrWhiteSpace(args)) await ReplyAsync($"{CustomEmoji.Cross} Please specify a name!");
            else if (args.Length > 32) await ReplyAsync($"{CustomEmoji.Cross} Pet name can't go above 32 characters!");
            else
            {
                pet.PetName = args;
                await AutoReactAsync();
            }
        }


        [PetCommand("image", "url")]
        public async Task PetImage(PetGame pet, string args)
        {
            string url = args ?? Context.Message.Attachments.FirstOrDefault()?.Url;

            if (url == null && pet.PetImageUrl == null)
            {
                await ReplyAsync($"{CustomEmoji.Cross} Please specify an image! You can use a link or upload your own.");
            }
            else
            {
                try
                {
                    pet.PetImageUrl = url;
                }
                catch (FormatException)
                {
                    await ReplyAsync($"{CustomEmoji.Cross} Invalid image link!\nYou can try uploading the image yourself.");
                    return;
                }

                if (url == null) await ReplyAsync($"{CustomEmoji.Check} Pet image reset!");
                else await AutoReactAsync();
            }
        }


        [PetCommand("help", "h")]
        public async Task PetHelp(PetGame pet, string args)
        {
            var summary = typeof(MoreGamesModule).GetMethod(nameof(PetMaster)).GetCustomAttribute<SummaryAttribute>();
            await ReplyAsync(summary?.Text.Replace("{prefix}", Prefix) ?? "Couldn't get help");
        }


        [PetCommand("pet", "pat", "pot", "p", "clockagotchi")]
        public async Task PetPet(PetGame pet, string args)
        {
            var now = DateTime.Now;
            if (now - pet.petTimerStart > TimeSpan.FromMinutes(1))
            {
                pet.petTimerStart = now;
                pet.timesPetSinceTimerStart = 0;
            }

            int limit = Context.Guild == null ? 15 : 5;
            if (pet.timesPetSinceTimerStart >= limit)
            {
                await ReplyAsync($"{CustomEmoji.Cross} That's enough petting! Try again in a minute"
                    + (Context.Guild == null ? "." : ", or pet in a DM with the bot."));
            }
            else
            {
                pet.timesPetSinceTimerStart += 1;
                await ReplyAsync(pet.DoPet());
            }
        }


        [PetCommand("user", "u")]
        public async Task PetUser(PetGame pet, string args)
        {
            if (string.IsNullOrWhiteSpace(args))
            {
                await ReplyAsync("You must specify a user!");
                return;
            }

            var user = await Context.ParseUserAsync(args);

            if (user == null)
            {
                await ReplyAsync("Can't find the specified user!");
                return;
            }

            pet = Storage.GetUserGame<PetGame>(user.Id);

            if (pet == null) await ReplyAsync("This person doesn't have a pet :(");
            else await ReplyAsync(pet.GetContent(), pet.GetEmbed(user));
        }


        [PetCommand("release")]
        public async Task PetRelease(PetGame pet, string args)
        {
            if (string.IsNullOrWhiteSpace(pet.PetName) || args?.SanitizeMarkdown().SanitizeMentions() == pet.PetName)
            {
                Storage.DeleteGame(pet);
                await ReplyAsync($"Goodbye {(string.IsNullOrWhiteSpace(pet.PetName) ? pet.Name : pet.PetName)}!");
            }
            else
            {
                await ReplyAsync($"❗ Are you sure you want to delete {pet.PetName}? It will be gone forever, along with your stats and achievements, " +
                                 $"and you can't get it back. Do **{Prefix}pet release {pet.PetName}** to release.");
            }
        }







        // You know, my intention when first creating this bot months ago was
        // to make a fun bot that produced the least amount of spam possible.
        // It was a huge mistake from my part to explicitly incentivize spamming.
        // I'm removing the petting leaderboard, and adding more restrictions to petting.
        // Pet on, my pet gods. Pet on. /s

        //[PetCommand("top", "rank", "lb", "ranking", "leaderboard", "best")]
        //public async Task PetRanking(PetGame pet, string args)
        //{
        //    var pets = storage.UserGames.Select(x => x as PetGame).Where(x => x != null).OrderByDescending(x => x.TimesPet);

        //    int pos = 1;
        //    var ranking = new StringBuilder();
        //    ranking.Append($"**Out of {pets.Count()} pets:**\n");
        //    foreach (var p in pets.Take(10))
        //    {
        //        ranking.Append($"\n**{pos}.** {p.TimesPet} pettings - ");
        //        if (args == "id") ranking.Append($"{p.OwnerId} ");
        //        else ranking.Append($"`{p.Owner?.Username.Replace("`", "´") ?? "Unknown"}'s {p.PetName.Replace("`", "´")}` ");
        //        ranking.Append(string.Join(' ', p.achievements.GetIcons(showHidden: true, highest: true)));
        //        pos++;
        //    }

        //    await ReplyAsync(ranking.ToString().Truncate(2000));

        //}
    }
}
