using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;
using Dapper;
using Microsoft.Data.Sqlite;

namespace ShowDamage
{
    public class ShowDamage : BasePlugin
    {
        public override string ModuleName => "ShowDamage";
        public override string ModuleVersion => "1.0";
        public override string ModuleAuthor => "Oylsister";

        private SqliteConnection _database = null!;
        private Dictionary<CCSPlayerController, bool> _enableShowDamage = new Dictionary<CCSPlayerController, bool>();

        public override void Load(bool hotReload)
        {
            RegisterEventHandler<EventPlayerHurt>(OnPlayerHurt);
            RegisterListener<Listeners.OnClientPutInServer>(OnClientPutInServer);
            RegisterListener<Listeners.OnClientDisconnect>(OnClientDisconnect);

            LoadClientSettings().Wait();

            AddCommand("css_showdamage", "", ShowDamageCommand);
        }

        public async void OnClientPutInServer(int clientSlot)
        {
            var client = Utilities.GetPlayerFromSlot(clientSlot);

            if (client == null)
                return;

            // get player data from database first 
            if (client.AuthorizedSteamID == null)
                return;

            _enableShowDamage.Add(client, true);

            var result = GetPlayerData(client.AuthorizedSteamID.SteamId3);

            if (result == null) 
            {
                _enableShowDamage[client] = true;
                await InsertPlayerData(client.AuthorizedSteamID.SteamId3, 1);

                return;
            }

            else
            {
                if (result == null)
                {
                    _enableShowDamage[client] = true;
                    await InsertPlayerData(client.AuthorizedSteamID.SteamId3, 1);

                    return;
                }
                else
                {
                    _enableShowDamage[client] = Convert.ToBoolean(result.Result);
                }
            }
        }

        public void OnClientDisconnect(int clientSlot)
        {
            var client = Utilities.GetPlayerFromSlot(clientSlot);
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
            var health = victim!.Health;

            if (_enableShowDamage[attacker])
                ShowDamageToClient(attacker, damage, health, victim.PlayerName);

            return HookResult.Continue;
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

        private async Task<int> GetPlayerData(string auth)
        {
            var data = await _database.QueryFirstOrDefaultAsync<int>($"SELECT showtext FROM `showdamage` WHERE player_auth = \"{auth}\";");

            return data;
        }

        private async Task UpdatePlayerData(string auth, int value)
        {
            await _database.ExecuteAsync($"UPDATE showdamage SET showtext = {value} WHERE player_auth = \"{auth}\";");
        }
    }
}
