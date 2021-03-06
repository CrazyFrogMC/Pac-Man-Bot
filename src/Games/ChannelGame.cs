﻿using System;
using System.Threading.Tasks;
using Discord;
using Discord.WebSocket;
using PacManBot.Extensions;

namespace PacManBot.Games
{
    public abstract class ChannelGame : BaseGame, IChannelGame
    {
        public virtual ulong ChannelId { get; set; }
        public virtual ulong MessageId { get; set; }

        public ISocketMessageChannel Channel => client.GetMessageChannel(ChannelId);

        public SocketGuild Guild => (client.GetChannel(ChannelId) as SocketGuildChannel)?.Guild;

        public async Task<IUserMessage> GetMessage() => MessageId != 0 ? await Channel.GetUserMessageAsync(MessageId) : null;


        protected ChannelGame() { }

        protected ChannelGame(ulong channelId, ulong[] userIds, IServiceProvider services)
            : base(userIds, services)
        {
            ChannelId = channelId;
        }
    }
}
