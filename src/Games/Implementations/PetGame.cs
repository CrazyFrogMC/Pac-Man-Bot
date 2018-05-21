using System;
using System.Runtime.Serialization;
using Discord;
using Discord.WebSocket;
using PacManBot.Services;

namespace PacManBot.Games
{
    [DataContract]
    class PetGame : BaseGame, IStoreableGame
    {
        private const int MaxStat = 20;

        public override string Name => "Clockagotchi";
        public override TimeSpan Expiry => TimeSpan.MaxValue;
        public string GameFile => $"games/pet{UserId[0]}.json";

        [DataMember] public string PetName { get; private set; }
        [DataMember] private string petImageUrl = null;
        [DataMember] private int satiation;
        [DataMember] private int fun;
        [DataMember] private int clean;
        [DataMember] private DateTime bornDate;
        [DataMember] private DateTime lastUpdated;

        [DataMember]
        public ulong OwnerId
        {
            get { return UserId[0]; }
            set { UserId = new ulong[] { value }; }
        }

        private int TotalStats => satiation + fun + clean;


        protected PetGame() { } // Serialization

        public PetGame(string name, ulong ownerId, DiscordShardedClient client, LoggingService logger, StorageService storage)
            : base(1, new ulong[] { ownerId }, client, logger, storage)
        {
            PetName = name;
            satiation = MaxStat;
            fun = MaxStat;
            clean = MaxStat;
            bornDate = DateTime.Now;
            lastUpdated = DateTime.Now;
        }


        public override EmbedBuilder GetEmbed(bool showHelp = true) => GetEmbed(null);
        public EmbedBuilder GetEmbed(IGuildUser owner)
        {
            UpdateStats();

            string desc = "";

            string prefix = storage.GetPrefixOrEmpty(owner?.Guild);
            if (PetName == "") desc += $"Congratulations on your new Clockagotchi!\nUse the **{prefix}pet name** and **{prefix}pet image** commands to customize it\n\n";

            desc += $"**Name:** {(PetName == "" ? "*Unnamed*" : PetName)}\n";

            var age = (DateTime.Now - bornDate);
            desc += $"**Age:** {$"{(int)age.TotalDays} day{"s".If((int)age.TotalDays > 1)}, ".If((int)age.TotalDays > 0)}" +
                $"{$"{age.Hours} hour{"s".If(age.Hours > 1)}, ".If(age.Hours > 0)}" +
                (age.Minutes > 0 ? $"{age.Minutes} minute{"s".If(age.Minutes > 1)}\n\n" : "Newborn\n\n");

            desc += $"🍎 **Satiation:** {satiation}/{MaxStat}{" Hungry!".If(satiation < 5)}\n";
            desc += $"🏈 **Happiness:** {fun}/{MaxStat}{" Lonely!".If(satiation < 5)}\n";
            desc += $"🛀 **Cleanness:** {clean}/{MaxStat}{" Dirty!".If(satiation < 5)}";

            return new EmbedBuilder
            {
                Title = $"{owner?.Nickname ?? client.GetUser(OwnerId).Username}'s Clockagotchi",
                Description = desc,
                Color = TotalStats >= 45 ? new Color(0, 200, 0) : TotalStats >= 20 ? new Color(255, 200, 0) : new Color(255, 0, 0),
                ThumbnailUrl = petImageUrl ?? "https://cdn.discordapp.com/attachments/353729197824278541/447979173554946051/clockagotchi.png",
            };
        }


        public void UpdateStats(bool feed = false, bool play = false, bool wash = false)
        {
            var now = DateTime.Now;
            double hours = (now - lastUpdated).TotalHours;

            int oldSatiation = satiation, oldFun = fun, oldClean = clean;

            satiation = feed ? MaxStat : Math.Max(0, satiation - (int)(hours * (Bot.Random.NextDouble() + 0.5)));
            fun = play ? MaxStat : Math.Max(0, fun - (int)(hours * 1.2 * (Bot.Random.NextDouble() + 0.5)));
            clean = wash ? MaxStat : Math.Max(0, clean - (int)(hours / 1.6 * (Bot.Random.NextDouble() + 0.5)));

            if (oldSatiation != satiation || oldFun != fun || oldClean != clean) lastUpdated = now;

            storage.StoreGame(this);
        }


        public void SetName(string name)
        {
            PetName = name.SanitizeMarkdown().SanitizeMentions();
            UpdateStats();
        }


        public void SetImage(string url)
        {
            petImageUrl = url;
            UpdateStats();
        }


        public virtual void SetServices(DiscordShardedClient client, LoggingService logger, StorageService storage)
        {
            this.client = client;
            this.logger = logger;
            this.storage = storage;
        }
    }
}
