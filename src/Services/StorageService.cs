using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Reflection;
using Microsoft.Data.Sqlite;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Collections.Concurrent;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using Discord;
using Discord.Rest;
using Discord.WebSocket;
using PacManBot.Games;
using PacManBot.Utils;
using PacManBot.Constants;
using PacManBot.Extensions;

namespace PacManBot.Services
{
    public class StorageService
    {
        private static readonly IReadOnlyDictionary<string, Type> StoreableGameTypes = GetStoreableGameTypes();

        private static readonly JsonSerializerSettings GameJsonSettings = new JsonSerializerSettings {
            ConstructorHandling = ConstructorHandling.AllowNonPublicDefaultConstructor,
        };


        private readonly DiscordShardedClient client;
        private readonly LoggingService logger;
        private readonly ConcurrentDictionary<ulong, IChannelGame> games;
        private readonly ConcurrentDictionary<(ulong, Type), IUserGame> userGames;
        private readonly ConcurrentDictionary<ulong, string> cachedPrefixes;
        private readonly ConcurrentDictionary<ulong, bool> cachedAllowsAutoresponse;
        private readonly ConcurrentDictionary<ulong, bool> cachedNoPrefixChannel;

        public IEnumerable<IChannelGame> GamesEnumerable => games.Select(x => x.Value);
        public IEnumerable<IUserGame> UserGamesEnumerable => userGames.Select(x => x.Value);

        public string DefaultPrefix { get; }
        public RestApplication AppInfo { get; private set; }
        public IConfigurationRoot BotContent { get; private set; }
        public string[] PettingMessages { get; private set; }
        public string[] SuperPettingMessages { get; private set; }
        public ulong[] BannedChannels { get; private set; }


        public SqliteConnection NewDatabaseConnection
            => new SqliteConnection($"Data Source={Files.Database};");



        public StorageService(DiscordShardedClient client, LoggingService logger, BotConfig config)
        {
            this.client = client;
            this.logger = logger;

            DefaultPrefix = config.defaultPrefix;
            BotContent = null;
            cachedPrefixes = new ConcurrentDictionary<ulong, string>();
            cachedAllowsAutoresponse = new ConcurrentDictionary<ulong, bool>();
            cachedNoPrefixChannel = new ConcurrentDictionary<ulong, bool>();
            games = new ConcurrentDictionary<ulong, IChannelGame>();
            userGames = new ConcurrentDictionary<(ulong, Type), IUserGame>();

            SetupDatabase();
            LoadContent();
            LoadGames();

            client.LoggedIn += LoadAppInfo;
        }




        public string GetPrefix(ulong guildId)
        {
            if (cachedPrefixes.TryGetValue(guildId, out string prefix)) return prefix;

            using (var connection = NewDatabaseConnection)
            {
                connection.Open();
                var command = new SqliteCommand($"SELECT prefix FROM prefixes WHERE id=@id LIMIT 1", connection)
                    .WithParameter("@id", guildId);

                prefix = (string)command.ExecuteScalar() ?? DefaultPrefix;
            }

            cachedPrefixes.TryAdd(guildId, prefix);
            return prefix;
        }

        public string GetPrefix(IGuild guild = null)
            => guild == null ? DefaultPrefix : GetPrefix(guild.Id);

        public string GetPrefixOrEmpty(IGuild guild)
            => guild == null ? "" : GetPrefix(guild.Id);


        public void SetPrefix(ulong guildId, string prefix)
        {
            cachedPrefixes[guildId] = prefix;

            string sql = "DELETE FROM prefixes WHERE id=@id;";
            if (prefix != DefaultPrefix) sql += "INSERT INTO prefixes VALUES (@id, @prefix);";

            using (var connection = NewDatabaseConnection)
            {
                connection.Open();

                new SqliteCommand(sql, connection)
                    .WithParameter("@id", guildId)
                    .WithParameter("@prefix", prefix)
                    .ExecuteNonQuery();
            }
        }




        public bool AllowsAutoresponse(ulong guildId)
        {
            if (cachedAllowsAutoresponse.TryGetValue(guildId, out bool allows)) return allows;

            using (var connection = NewDatabaseConnection)
            {
                connection.Open();

                var command = new SqliteCommand("SELECT * FROM noautoresponse WHERE id=@id LIMIT 1", connection)
                    .WithParameter("@id", guildId);
                allows = command.ExecuteScalar() == null;
                cachedAllowsAutoresponse.TryAdd(guildId, allows);
                return allows;
            }
        }


        public bool ToggleAutoresponse(ulong guildId)
        {
            cachedAllowsAutoresponse[guildId] = !cachedAllowsAutoresponse[guildId];

            using (var connection = NewDatabaseConnection)
            {
                connection.Open();
                new SqliteCommand("BEGIN", connection).ExecuteNonQuery();

                int rows = new SqliteCommand("DELETE FROM noautoresponse WHERE id=@id", connection)
                    .WithParameter("@id", guildId)
                    .ExecuteNonQuery();

                if (rows == 0)
                {
                    new SqliteCommand("INSERT INTO noautoresponse VALUES (@id)", connection)
                        .WithParameter("@id", guildId)
                        .ExecuteNonQuery();
                }

                new SqliteCommand("END", connection).ExecuteNonQuery();
                return rows != 0;
            }
        }




        public bool NoPrefixChannel(ulong channelId)
        {
            if (cachedNoPrefixChannel.TryGetValue(channelId, out bool noPrefixMode)) return noPrefixMode;

            using (var connection = NewDatabaseConnection)
            {
                connection.Open();

                var command = new SqliteCommand("SELECT * FROM noprefix WHERE id=@id LIMIT 1", connection)
                    .WithParameter("@id", channelId);
                noPrefixMode = command.ExecuteScalar() != null;
                cachedNoPrefixChannel.TryAdd(channelId, noPrefixMode);
                return noPrefixMode;
            }
        }


        public bool ToggleNoPrefix(ulong channelId)
        {
            cachedNoPrefixChannel[channelId] = !cachedNoPrefixChannel[channelId];

            using (var connection = NewDatabaseConnection)
            {
                connection.Open();
                new SqliteCommand("BEGIN", connection).ExecuteNonQuery();

                int rows = new SqliteCommand("DELETE FROM noprefix WHERE id=@id", connection)
                    .WithParameter("@id", channelId)
                    .ExecuteNonQuery();

                if (rows == 0)
                {
                    new SqliteCommand("INSERT INTO noprefix VALUES (@id)", connection)
                        .WithParameter("@id", channelId)
                        .ExecuteNonQuery();
                }

                new SqliteCommand("END", connection).ExecuteNonQuery();
                return rows != 0;
            }
        }




        public void AddScore(ScoreEntry entry)
        {
            logger.Log(LogSeverity.Info, LogSource.Storage, $"New scoreboard entry: {entry}");

            using (var connection = NewDatabaseConnection)
            {
                connection.Open();

                string sql = "INSERT INTO scoreboard VALUES (@score, @userid, @state, @turns, @username, @channel, @date)";
                new SqliteCommand(sql, connection)
                    .WithParameter("@score", entry.score)
                    .WithParameter("@userid", entry.userId)
                    .WithParameter("@state", entry.state)
                    .WithParameter("@turns", entry.turns)
                    .WithParameter("@username", entry.username)
                    .WithParameter("@channel", entry.channel)
                    .WithParameter("@date", entry.date)
                    .ExecuteNonQuery();
            }
        }


        public IReadOnlyList<ScoreEntry> GetScores(TimePeriod period, int amount = 1, int start = 0, ulong? userId = null)
        {
            var conditions = new List<string>();
            if (period != TimePeriod.All) conditions.Add($"date>=@date");
            if (userId != null) conditions.Add($"userid=@userid");

            string sql = "SELECT * FROM scoreboard " +
             (conditions.Count == 0 ? "" : $"WHERE {string.Join(" AND ", conditions)} ") +
             "ORDER BY score DESC LIMIT @amount OFFSET @start";

            var scores = new List<ScoreEntry>();

            using (var connection = NewDatabaseConnection)
            {
                connection.Open();

                var command = new SqliteCommand(sql, connection)
                    .WithParameter("@amount", amount)
                    .WithParameter("@start", start)
                    .WithParameter("@userid", userId)
                    .WithParameter("@date", DateTime.Now - TimeSpan.FromHours((int)period));

                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        int score = reader.GetInt32(0);
                        ulong id = (ulong)reader.GetInt64(1);
                        State state = (State)reader.GetInt32(2);
                        int turns = reader.GetInt32(3);
                        string name = reader.GetString(4);
                        string channel = reader.GetString(5);
                        DateTime date = reader.GetDateTime(6);

                        scores.Add(new ScoreEntry(score, id, state, turns, name, channel, date));
                    }
                }
            }

            logger.Log(LogSeverity.Info, LogSource.Storage, $"Grabbed {scores.Count} score entries");
            return scores.AsReadOnly();
        }




        public IChannelGame GetChannelGame(ulong channelId)
            => games.TryGetValue(channelId, out var game) ? game : null;


        public TGame GetChannelGame<TGame>(ulong channelId) where TGame : class, IChannelGame
            => GetChannelGame(channelId) as TGame;


        public TGame GetUserGame<TGame>(ulong userId) where TGame : class, IUserGame
            => userGames.TryGetValue((userId, typeof(TGame)), out var game) ? (TGame)game : null;


        public void AddGame(IBaseGame game)
        {
            if (game is IUserGame uGame) userGames.TryAdd((uGame.OwnerId, uGame.GetType()), uGame);
            else if (game is IChannelGame cGame) games.TryAdd(cGame.ChannelId, cGame);
        }


        public void DeleteGame(IBaseGame game)
        {
            try
            {
                game.CancelRequests();
                if (game is IStoreableGame sGame && File.Exists(sGame.GameFile()))
                {
                    File.Delete(sGame.GameFile());
                }

                bool success = false;
                if (game is IUserGame uGame)
                {
                    success = userGames.TryRemove((uGame.OwnerId, uGame.GetType()));
                }
                else if (game is IChannelGame cGame)
                {
                    success = games.TryRemove(cGame.ChannelId);
                }

                if (success) logger.Log(LogSeverity.Verbose, LogSource.Storage, $"Removed {game.GetType().Name} at {game.IdentifierId()}");
            }
            catch (Exception e)
            {
                logger.Log(LogSeverity.Error, LogSource.Storage, $"Trying to remove game at {game.IdentifierId()}: {e}");
            }
        }


        public void StoreGame(IStoreableGame game)
        {
            File.WriteAllText(game.GameFile(), JsonConvert.SerializeObject(game), Encoding.UTF8);
        }




        private void SetupDatabase()
        {
            if (!File.Exists(Files.Database))
            {
                File.Create(Files.Database);
                logger.Log(LogSeverity.Info, LogSource.Storage, "Creating database");
            }

            using (var connection = NewDatabaseConnection)
            {
                connection.Open();

                foreach (string table in new[] {
                    "prefixes (id BIGINT PRIMARY KEY, prefix TEXT)",
                    "scoreboard (score INT, userid BIGINT, state INT, turns INT, username TEXT, channel TEXT, date DATETIME)",
                    "noautoresponse (id BIGINT PRIMARY KEY)",
                    "noprefix (id BIGINT PRIMARY KEY)"
                })
                {
                    new SqliteCommand($"CREATE TABLE IF NOT EXISTS {table}", connection).ExecuteNonQuery();
                }
            }
        }


        public void LoadContent()
        {
            if (BotContent == null)
            {
                BotContent = new ConfigurationBuilder().SetBasePath(AppContext.BaseDirectory).AddJsonFile(Files.Contents).Build();
            }
            else
            {
                BotContent.Reload();
            }

            BannedChannels = BotContent["banned"].Split(',').Select(ulong.Parse).ToArray();
            PettingMessages = BotContent["petting"].Split('\n', StringSplitOptions.RemoveEmptyEntries);
            SuperPettingMessages = BotContent["superpetting"].Split('\n', StringSplitOptions.RemoveEmptyEntries);

            logger.LoadLogExclude(this);
        }


        private void LoadGames()
        {
            if (!Directory.Exists(Files.GameFolder))
            {
                Directory.CreateDirectory(Files.GameFolder);
                logger.Log(LogSeverity.Warning, LogSource.Storage, $"Created missing directory \"{Files.GameFolder}\"");
                return;
            }

            uint fail = 0;
            bool firstFail = true;

            IServiceProvider gameServices = new ServiceCollection()
                .AddSingleton(this)
                .AddSingleton(client)
                .AddSingleton(logger)
                .BuildServiceProvider();

            foreach (string file in Directory.GetFiles(Files.GameFolder))
            {
                if (file.EndsWith(Files.GameExtension))
                {
                    try
                    {
                        Type gameType = StoreableGameTypes.First(x => file.Contains(x.Key)).Value;
                        var game = (IStoreableGame)JsonConvert.DeserializeObject(File.ReadAllText(file), gameType, GameJsonSettings);
                        game.PostDeserialize(gameServices);

                        if (game is IUserGame uGame) userGames.TryAdd((uGame.OwnerId, uGame.GetType()), uGame);
                        else if (game is IChannelGame cGame) games.TryAdd(cGame.ChannelId, cGame);
                    }
                    catch (Exception e)
                    {
                        logger.Log(LogSeverity.Error, LogSource.Storage,
                                   $"Couldn't load game at {file}: {(firstFail ? e.ToString() : e.Message)}");
                        fail++;
                        firstFail = false;
                    }
                }
            }

            logger.Log(LogSeverity.Info, LogSource.Storage,
                       $"Loaded {games.Count + userGames.Count} games{$" with {fail} errors".If(fail > 0)}");
        }


        private async Task LoadAppInfo()
        {
            AppInfo = await client.GetApplicationInfoAsync(Bot.DefaultOptions);
            client.LoggedIn -= LoadAppInfo;
        }

        


        private static IReadOnlyDictionary<string, Type> GetStoreableGameTypes()
        {
            return Assembly.GetAssembly(typeof(IStoreableGame)).GetTypes()
                .Where(t => t.IsClass && !t.IsAbstract && t.GetInterfaces().Contains(typeof(IStoreableGame)))
                .Select(x => KeyValuePair.Create(((IStoreableGame)Activator.CreateInstance(x, true)).FilenameKey, x))
                .OrderByDescending(x => x.Key.Length)
                .AsDictionary()
                .AsReadOnly();
        }
    }
}
