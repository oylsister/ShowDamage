using System.Net.Http.Headers;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;
using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using static System.Net.Mime.MediaTypeNames;
using static System.Runtime.InteropServices.JavaScript.JSType;
using static CounterStrikeSharp.API.Core.Listeners;

namespace ShowDamage
{
    public class ShowDamage : BasePlugin
    {
        public override string ModuleName => "ShowDamage";
        public override string ModuleVersion => "1.2";
        public override string ModuleAuthor => "Oylsister";

        private SqliteConnection _database = null!;
        private Dictionary<CCSPlayerController, bool> _enableShowDamage = new Dictionary<CCSPlayerController, bool>();
        private Dictionary<CCSPlayerController, double> _hudTime = new Dictionary<CCSPlayerController, double>();
        private Dictionary<CCSPlayerController, DamageInfo> _damageInfo = new Dictionary<CCSPlayerController, DamageInfo>();

        public override void Load(bool hotReload)
        {
            RegisterEventHandler<EventPlayerHurt>(OnPlayerHurt);
            RegisterEventHandler<EventPlayerConnectFull>(OnPlayerConnectFull);
            RegisterListener<Listeners.OnClientDisconnect>(OnClientDisconnect);

            RegisterListener<Listeners.OnMapStart>(MapStart);
            RegisterListener<Listeners.OnMapEnd>(MapEnd);

            LoadClientSettings().Wait();

            AddCommand("css_showdamage", "", ShowDamageCommand);
        }

        public void MapStart(string map)
        {
            RegisterListener<Listeners.OnTick>(OnGameFrame);
        }

        public void MapEnd()
        {
            RemoveListener<Listeners.OnTick>(OnGameFrame);
        }

        public HookResult OnPlayerConnectFull(EventPlayerConnectFull @event, GameEventInfo info)
        {
            if (@event.Userid == null)
                return HookResult.Continue;

            if (@event.Userid.IsBot)
                return HookResult.Continue;

            OnClientPutInServer(@event.Userid!);
            _hudTime.Add(@event.Userid, 0.0f);
            _damageInfo.TryAdd(@event.Userid, new DamageInfo(@event.Userid, 0, 0, null));

            return HookResult.Continue;
        }

        public void OnClientPutInServer(CCSPlayerController client)
        {
            if (client == null)
                return;

            // get player data from database first 
            if (client.AuthorizedSteamID == null)
                return;

            _enableShowDamage.Add(client, true);

            var result = GetPlayerData(client.AuthorizedSteamID.SteamId3);

            Logger.LogInformation($"Result for {client.PlayerName} is {result}");

            if (result == -1) 
            {
                _enableShowDamage[client] = true;
                InsertPlayerData(client.AuthorizedSteamID.SteamId3, 1).Wait();

                return;
            }

            else
            {
                _enableShowDamage[client] = Convert.ToBoolean(result);
                return;
            }
        }

        public void OnClientDisconnect(int clientSlot)
        {
            var client = Utilities.GetPlayerFromSlot(clientSlot);
            _enableShowDamage.Remove(client!);
            _hudTime.Remove(client!);
            _damageInfo.Remove(client!);
        }

        [CommandHelper(0, "", CommandUsage.CLIENT_ONLY)]
        private void ShowDamageCommand(CCSPlayerController? client, CommandInfo info)
        {
            if (client == null)
                return;

            if(!_enableShowDamage.ContainsKey(client))
            {
                info.ReplyToCommand($" {ChatColors.Green}[ShowDamage]{ChatColors.White} You are not in database!");
                return;
            }

            _enableShowDamage[client] = !_enableShowDamage[client];
            InitialUpdate(client);

            if (_enableShowDamage[client])
            {
                info.ReplyToCommand($" {ChatColors.Green}[ShowDamage]{ChatColors.White} You have {ChatColors.Olive}turned on {ChatColors.White}showdamage info.");
                return;
            }

            else
            {
                info.ReplyToCommand($" {ChatColors.Green}[ShowDamage]{ChatColors.White} You have {ChatColors.LightRed}turned off {ChatColors.White}showdamage info.");
                return;
            }
        }

        private HookResult OnPlayerHurt(EventPlayerHurt @event, GameEventInfo gameEventInfo)
        {
            var attacker = @event.Attacker!;
            var victim = @event.Userid;
            var damage = @event.DmgHealth;
            var health = victim!.PlayerPawn.Value!.Health;

            if (attacker.IsBot)
                return HookResult.Continue;

            if (!_enableShowDamage.ContainsKey(attacker))
                return HookResult.Continue;

            if (_enableShowDamage[attacker])
            {
                _hudTime[attacker] = Server.EngineTime + 2.0;

                _damageInfo[attacker].Damage = damage;
                _damageInfo[attacker].HP = health;
                _damageInfo[attacker].Name = victim.PlayerName; 
            }

            return HookResult.Continue;
        }

        public void OnGameFrame()
        {
            foreach(var player in Utilities.GetPlayers())
            {
                if (!_enableShowDamage.ContainsKey(player))
                    continue;

                if (_enableShowDamage[player])
                {
                    if (_hudTime[player] > Server.EngineTime)
                        ShowDamageToClient(player, _damageInfo[player].Damage, _damageInfo[player].HP, _damageInfo[player].Name!);
                }
            }
        }

        void ShowDamageToClient(CCSPlayerController client, int damage, int hpleft, string clientname)
        {
            var message = $"Damage: {damage}<br>{clientname} HP: {hpleft}";
            client.PrintToCenterHtml(message);
        }

        public async void InitialUpdate(CCSPlayerController client)
        {
            if (client.AuthorizedSteamID == null)
                return;

            await UpdatePlayerData(client.AuthorizedSteamID.SteamId3, Convert.ToInt32(_enableShowDamage[client]));
        }

        private async Task LoadClientSettings()
        {
            _database = new SqliteConnection($"Data Source={Path.Join(ModuleDirectory, "showdamage.db")}");
            _database.Open();

            await _database.ExecuteAsync(@"CREATE TABLE IF NOT EXISTS `showdamage` (player_auth VARCHAR(64) PRIMARY KEY, showtext INT);");
        }

        private async Task InsertPlayerData(string auth, int setting)
        {
            await _database.ExecuteAsync($"INSERT INTO `showdamage` (player_auth, showtext) VALUES (\"{auth}\", {setting});");
        }

        private int GetPlayerData(string auth)
        {
            var datas = new SqliteCommand($"SELECT showtext FROM `showdamage` WHERE player_auth = \"{auth}\";", _database);
            var reader = datas.ExecuteReader();

            if(reader.HasRows)
            {
                while (reader.Read())
                {
                    return reader.GetInt32(0);
                }
            }

            return -1;
        }

        private async Task UpdatePlayerData(string auth, int value)
        {
            await _database.ExecuteAsync($"UPDATE showdamage SET showtext = {value} WHERE player_auth = \"{auth}\";");
        }
    }

    public class DamageInfo
    {
        public DamageInfo(CCSPlayerController attacker, int damage, int hP, string? name)
        {
            Attacker = attacker;
            Damage = damage;
            HP = hP;
            Name = name;
        }

        public CCSPlayerController Attacker { get; set; }
        public int Damage { get; set; }
        public int HP { get; set; }
        public string? Name { get; set; }
    }
}
