using System;
using System.Collections.Generic;
using TShockAPI;
using TShockAPI.DB;
using Terraria;
using System.Threading;
using Newtonsoft.Json;

namespace EyeSpy
{
    internal class PlayerData
    {
        private EyeSpy main;
        public TSPlayer TSPlayer;
        public byte LastState = 0;
        public byte IdleCount = 0;
        public int LastPVPid = -1;
        public int TotalOnline = 0;
        public int tempMin = 0;
        public Dictionary<int, int> KillData = new Dictionary<int, int>();
        private int money;
        public int Money
        {
            get { return money; }
            set
            {
                main.Database.Query("UPDATE vault_players SET money = @0 WHERE username = @1 AND worldID = @2", value, TSPlayer.Name, Main.worldID);
                money = value;
            }
        }
        public bool ChangeMoney(int amount, MoneyEventFlags flags, bool announce = false)
        {
            MoneyEventArgs args = new MoneyEventArgs() { Amount = amount, CurrentMoney = money, PlayerIndex = TSPlayer.Index, PlayerName = TSPlayer.Name, EventFlags = flags };
            if (money >= amount * -1)
            {
                if (!EyeSpy.InvokeEvent(args))
                {
                    Money += amount;
                    if (announce)
                        TSPlayer.SendMessage(string.Format("You've {1} {0}", EyeSpy.MoneyToString(amount), amount >= 0 ? "gained" : "lost"), Color.DarkOrange);
                    return true;
                }
            }
            return false;
        }
        public void AddKill(int mobID)
        {
            if (KillData.ContainsKey(mobID))
                KillData[mobID] += 1;
            else
                KillData.Add(mobID, 1);
        }
        public PlayerData(EyeSpy instance, TSPlayer player)
        {
            main = instance;
            TSPlayer = player;
            UpdatePlayerData();
            StartUpdating();
        }
        public void UpdatePlayerData()
        {
            QueryResult result = main.Database.QueryReader("SELECT * FROM vault_players WHERE username = @0 AND worldID = @1", TSPlayer.Name, Main.worldID);
            if (result.Read())
            {
                money = result.Get<int>("money");
                TotalOnline = result.Get<int>("totalOnline");
                tempMin = result.Get<int>("tempMin");
                KillData = JsonConvert.DeserializeObject<Dictionary<int, int>>(result.Get<string>("killData"));
            }
            else
            {
                money = EyeSpy.config.InitialMoney;
                main.Database.Query("INSERT INTO vault_players(username, money, worldID, killData, tempMin, totalOnline, lastSeen) VALUES(@0,@1,@2,@3,0,0,@4)", TSPlayer.Name, money, Main.worldID, JsonConvert.SerializeObject(new Dictionary<int, int>()), JsonConvert.SerializeObject(DateTime.UtcNow));
            }
            result.Dispose();
        }


        public Thread UpdateThread = null;
        public void StartUpdating()
        {
            try
            {
                if (UpdateThread == null || !UpdateThread.IsAlive)
                {
                    var updater = new Updater(main, this);
                    UpdateThread = new Thread(updater.PayTimer);
                    UpdateThread.Start();
                }
            }
            catch (Exception ex) { TShock.Log.ConsoleError(ex.ToString()); }
        }
        public void StopUpdating()
        {
            try
            {
                if (UpdateThread != null)
                    UpdateThread.Abort();
            }
            catch (Exception ex) { TShock.Log.ConsoleError(ex.ToString()); }
        }

        // -------------------------------- UPDATER ----------------------------------------------------------
        private class Updater
        {
            int who;
            EyeSpy main;
            int TimerCount;
            public Updater(EyeSpy instance, PlayerData pd)
            {
                main = instance;
                who = pd.TSPlayer.Index;
                TimerCount = (pd.tempMin > 0) ? (pd.tempMin - 1) : 0;
            }
            public void PayTimer()
            {
                while (Thread.CurrentThread.IsAlive && main != null)
                {
                    var player = main.PlayerList[who];
                    if (player != null)
                    {
                        try
                        {
                            if (EyeSpy.config.MaxIdleTime == 0 || player.IdleCount <= EyeSpy.config.MaxIdleTime)
                            {
                                player.IdleCount++;
                                player.TotalOnline++;
                                if (EyeSpy.config.GiveTimedPay && TimerCount == EyeSpy.config.PayEveryMinutes)
                                    player.ChangeMoney(EyeSpy.config.Payamount, MoneyEventFlags.TimedPay, EyeSpy.config.AnnounceTimedPay);
                                this.TimerCount++;
                                if (TimerCount > EyeSpy.config.PayEveryMinutes)
                                    TimerCount = 1;
                            }
                            main.Database.Query("UPDATE vault_players SET tempMin = @0, totalOnline = @1, lastSeen = @2, killData = @5 WHERE username = @3 AND worldID = @4", TimerCount, player.TotalOnline, JsonConvert.SerializeObject(DateTime.UtcNow), player.TSPlayer.Name, Main.worldID, JsonConvert.SerializeObject(player.KillData));
                        }
                        catch (Exception ex) { TShock.Log.ConsoleError(ex.ToString()); }
                        Thread.Sleep(60000);
                    }
                    else
                        Thread.CurrentThread.Abort();
                }
            }
        }

    }
}
