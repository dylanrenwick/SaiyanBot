using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using System.IO;
using System.Net;
using System.Data.SQLite;
using System.Data;
using System.Threading;

using SkidsDev.EasyIRC;
using Newtonsoft.Json.Linq;
using GunnerWolfLib;

namespace SaiyanBot2
{
    /*
     * SaiyanBot2.0 by GunnerWolf/SkidsDev
     * Latest update: 25/02/2015
     * 
     * Twitch channel:
     *    http://twitch.tv/natsuma_z
     *    
     * Yes, everything happens in Program.cs, because I'm lazy
    */
    class Program
    {
        //IRC Object to connect to twitch IRC and parse commands
        private static IRC irc;

        //All players that have savedata
        private static HashSet<Player> playerList;
        //All players currently online
        private static HashSet<Player> onlinePlayers;
        //Main time thread
        private static Thread timer;
        //DateTime to help time per-second events
        private static DateTime prevSecond;
        //When the last event happened
        private static DateTime prevEvent;
        //When the current event started
        private static DateTime eventStart;
        //Used purely to calculate Ticklength
        private static DateTime lastTick;
        //When the last save happened
        private static DateTime lastSave;

        //Players that can't play
        private static HashSet<string> banned = new HashSet<string> { "natsuma_z", "saiyanbot", "nightbot", "dusk_shade" };
        //All commands
        private static HashSet<string> commands = new HashSet<string> { "!power", "!check", "!scouter", "!enter", "!ping", "!ladder", "!ts", "!fight", "!startbet", "!betyes", "!betno", "!endbet", "!bet" };
        //Players with access to mod-only commands
        private static HashSet<string> mods = new HashSet<string> { "teemomies", "natsuma_z", "dusk_shade", "taohuayuanji", "gunnerwolfgaming", "lteden", "mightiecake", "haxd3", "thedeadset" };

        //Currently running Bet
        private static GriftBet runningBet;
        //Currently running Event
        private static ChatEvent eventRunning;
        //The "stage" the current event is at
        private static int eventStage;

        //Whether the stream is live
        private static bool IsLive;
        //Property that actually checks if the stream is live using Twitch API
        private static bool Live
        {
            get
            {
                using (var w = new WebClient())
                {
                    JObject stream;
                    try
                    {
                        var json_data = w.DownloadString("https://api.twitch.tv/kraken/streams/" + channel);
                        stream = JObject.Parse(json_data);
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine("##########");
                        Console.WriteLine("Error getting stream status:");
                        Console.WriteLine(e.Message + " @ " + DateTime.Now.ToString());
                        Console.WriteLine("##########");
                        return false;
                    }
                    if (stream != null) return stream["stream"].HasValues;
                    else return false;
                }
            }
        }

        //Save path
        private static string dirPath = Application.StartupPath + "\\Saves";

        //IRC Username
        private readonly static string user = Properties.Settings.Default.Username;
        //IRC Channel
        private readonly static string channel = Properties.Settings.Default.Channel;
        //IRC Twitch oauth token
        private readonly static string oauth = Properties.Settings.Default.Password;

        //Basically the constructor/console command parser
        static void Main(string[] args)
        {
            //Check if the stream is live before doing anything
            Write("##Connecting to twitch...");
            IsLive = Live;
            Write("##Connected. Stream is currently " + (IsLive ? "live." : "offline."));

            //Load playerdata
            playerList = Load();
            onlinePlayers = new HashSet<Player>();

            //Instantiate IRC object
            Write("##Connecting to irc...");
            irc = new IRC(user, oauth, "irc.twitch.tv", channel);
            Write("##Connected.");

            //Add commands to IRC object's command list
            Write("##Adding commands...");
            foreach (var c in commands)
            {
                irc.AddCommand(c);
                Write("##Added " + c);
            }

            //Subscribe to IRC events
            irc.onCommand += new CommandEventHandler(irc_onCommand);
            irc.onOutput += new OutputEventHandler(handleOutput);

            //Can be used to send messages to IRC when connected
            //Send("Test");

            //Create and start main time thread
            Write("##Starting Main Loop...");
            timer = new Thread(new ThreadStart(TimerLoop));
            timer.IsBackground = true;
            timer.Start();
            Write("##Main Loop started");

            //Parse console commands
            while (true)
            {
                var input = Console.ReadLine();

                //Force a save
                if (input.ToLower() == "save")
                {
                    Write("##Saving...");
                    try
                    {
                        Save();
                    }
                    catch (Exception e)
                    {
                        Write(e.Message);
                        Console.WriteLine(e.StackTrace);
                        Console.ReadLine();
                    }
                    continue;
                }
                //Check time since last event
                else if (input.ToLower() == "etime")
                {
                    var time = TimeSpan.FromMinutes(20).Subtract(TimeSince(prevEvent));

                    Write("##Time until next event: " + time.TotalMinutes.ToString());
                    continue;
                }
                //Force Event
                else if (input.ToLower() == "event")
                {
                    Write("##Forcing Event...");
                    prevEvent = DateTime.Now.Subtract(TimeSpan.FromMinutes(20));
                    continue;
                }
                //Initiate full data wipe
                else if (input.ToLower() == "wipe")
                {
                    Write("##WARNING: This will wipe ALL player data, type yes to confirm: ");
                    var conf = Console.ReadLine();
                    if (conf.ToLower() == "yes")
                    {
                        Write("##Wiping data...");
                        Send("Gunner has initiated a data wipe cos he sux, enjoy being n00bs again natsK");
                        if (File.Exists(dirPath + "\\Save.sqlite"))
                            File.Delete(dirPath + "\\Save.sqlite");
                        playerList = new HashSet<Player>();
                        onlinePlayers = new HashSet<Player>();

                        Write("##Saving...");
                        try
                        {
                            Save();
                        }
                        catch (Exception e)
                        {
                            Write(e.Message);
                            Console.WriteLine(e.StackTrace);
                            Console.ReadLine();
                        }
                        continue;
                    }
                }
                //Add PL to a player or all players
                else if (input.ToLower().StartsWith("addpl"))
                {
                    var ex = input.ToLower().Split(' ');

                    if (ex.Length == 2)
                    {
                        int pl;
                        if (int.TryParse(ex[1], out pl))
                        {
                            var temp = new HashSet<Player>(playerList);
                            foreach (var p in temp)
                            {
                                p.BonusPL += pl;
                            }
                            Write(string.Format("##Added {0} PL to all players", pl));
                            continue;
                        }
                    }
                    else if (ex.Length == 3)
                    {
                        int pl;
                        if (int.TryParse(ex[2], out pl))
                        {
                            var p = GetExistingPlayer(ex[1]);

                            if (p != null)
                            {
                                p.BonusPL += pl;
                                Write(string.Format("##Added {0} PL to {1}", pl, p.User));
                                continue;
                            }
                        }
                    }
                }
                //Save and quit
                else if (input.ToLower() == "quit")
                {
                    Write("##Saving...");
                    try
                    {
                        timer.Abort();
                        irc.Close();
                        Save();
                    }
                    catch (Exception e)
                    {
                        Write(e.Message);
                        Console.WriteLine(e.StackTrace);
                        Console.ReadLine();
                    }
                    break;
                }

                Write("##Invalid Command");
            }

            Write("##Quitting application...");
            timer.Abort();
        }

        //Simple helper method to combine onlinetime and offlinetime of a player
        private static string GetTime(Player p)
        {
            var ts = TimeSpan.FromSeconds(p.OnlineTime).Add(TimeSpan.FromSeconds(p.OfflineTime));
            return FormatTime(ts);
        }
        //Simple helper method to ensure second, minute and hour values for a Time string are at least 2 characters
        private static string FormatTime(TimeSpan ts)
        {
            var val = "";

            if (ts.Hours.ToString().Length < 2)
                val += "0";

            val += ts.Hours.ToString();
            val += ":";

            if (ts.Minutes.ToString().Length < 2)
                val += "0";

            val += ts.Minutes.ToString();
            val += ":";

            if (ts.Seconds.ToString().Length < 2)
                val += "0";

            val += ts.Seconds.ToString();

            return val;
        }

        //Whenever a registered command is executed
        static void irc_onCommand(Command cmd)
        {
            //else-ifs instead of switch-case because I like curly braces around my cases :3
            //!power command, displays current PL and total view time of the player
            if (cmd.Name == "!power")
            {
                Player player = GetPlayer(cmd.User);

                Send(string.Format("{0}, your Power Level is {1} and you have {2} recorded view time.", cmd.User, player.PL, GetTime(player)));
            }
            //!startbet [int] command, begins a new bet, mod only
            else if (cmd.Name == "!startbet")
            {
                if (mods.Contains(cmd.User))
                {
                    if (runningBet != null)
                    {
                        Send(string.Format("{0}, a bet is already running, dumbass! natsK", cmd.User));
                        return;
                    }

                    if (cmd.Args.Length != 1)
                    {
                        Send(string.Format("{0}, do you even grift level brah? Command Usage: !startbet griftlevel"));
                        return;
                    }

                    int grift;
                    if (!int.TryParse(cmd.Args[0], out grift))
                    {
                        Send(string.Format("{0}, do you even grift level brah? Command Usage: !startbet griftlevel"));
                        return;
                    }

                    runningBet = new GriftBet(grift);
                    Send(string.Format("Nat is trying to do a {0} grift! Type !bet for more info!", grift));

                    new Thread(new ThreadStart(() =>
                    {
                        Thread.Sleep(30000);
                        runningBet.CloseBids();
                        Send(string.Format("Bets for the grift are now closed!"));
                    })).Start();
                }
            }
            //!betyes [int] command, allows a player to bet "yes" in a bet
            else if (cmd.Name == "!betyes")
            {
                if (runningBet != null)
                {
                    if (cmd.Args.Length != 1)
                    {
                        Send(string.Format("{0}, how much you tryin' to bet bruh? Command Usage: !betyes betAmount", cmd.User));
                        return;
                    }

                    int betAmt;
                    if (!int.TryParse(cmd.Args[0], out betAmt))
                    {
                        Send(string.Format("{0}, how much you tryin' to bet bruh? Command Usage: !betyes betAmount", cmd.User));
                        return;
                    }

                    var player = GetPlayer(cmd.User);
                    if (betAmt > player.PL)
                    {
                        Send(string.Format("{0}, do you wanna be in debt? Don't bet more than you have!", cmd.User));
                        return;
                    }
                    if (betAmt > 150)
                    {
                        Send(string.Format("{0}, do you wanna be in debt? Don't bet more than 150 PL!", cmd.User));
                        return;
                    }

                    runningBet.VoteYes(player, betAmt);
                    Send(string.Format("{0} has bet {1} PL that Nat will succeed in the grift, how foolish. natsK", cmd.User, betAmt));
                }
            }
            //!betno [int] command, allows a player to bet "no" in a bet
            else if (cmd.Name == "!betno")
            {
                if (runningBet != null)
                {
                    if (cmd.Args.Length != 1)
                    {
                        Send(string.Format("{0}, how much you tryin' to bet bruh? Command Usage: !betno betAmount", cmd.User));
                        return;
                    }

                    int betAmt;
                    if (!int.TryParse(cmd.Args[0], out betAmt))
                    {
                        Send(string.Format("{0}, how much you tryin' to bet bruh? Command Usage: !betno betAmount", cmd.User));
                        return;
                    }

                    var player = GetPlayer(cmd.User);
                    if (betAmt > player.PL)
                    {
                        Send(string.Format("{0}, do you wanna be in debt? Don't bet more than you have!", cmd.User));
                        return;
                    }
                    if (betAmt > 150)
                    {
                        Send(string.Format("{0}, do you wanna be in debt? Don't bet more than 150 PL!", cmd.User));
                        return;
                    }

                    runningBet.VoteNo(player, betAmt);
                    Send(string.Format("{0} has bet {1} PL that Nat will fail in the grift, smart move. natsK", cmd.User, betAmt));
                }
            }
            //!endbet win/lose command, ends a running bet, mod only.
            else if (cmd.Name == "!endbet")
            {
                if (mods.Contains(cmd.User))
                {
                    if (runningBet == null)
                    {
                        Send(string.Format("{0}, there isn't a bet running, dumbass! natsK", cmd.User));
                        return;
                    }

                    if (cmd.Args.Length != 1)
                    {
                        Send(string.Format("{0}, so who won? natsK Command Usage: !endbet win/lose", cmd.User));
                        return;
                    }

                    string result = cmd.Args[0];
                    Dictionary<string, int> winners;
                    switch (result)
                    {
                        case "win":
                            winners = new Dictionary<string, int>(runningBet.YesVotes);
                            Send("Natsuma actually won!? I call hax natsK");
                            break;
                        case "lose":
                            winners = new Dictionary<string, int>(runningBet.NoVotes);
                            Send("As always, Nats lost natsK");
                            break;
                        default:
                            Send(string.Format("{0}, so who won? natsK Command Usage: !endbet win/lose", cmd.User));
                            return;
                    }

                    if (runningBet.Open)
                    {
                        runningBet.CloseBids();
                        Send(string.Format("Bets for the grift are now closed!"));
                    }

                    var winList = new Dictionary<Player, int>();

                    foreach (var k in winners)
                    {
                        var player = GetExistingPlayer(k.Key);
                        if (player != null)
                            winList.Add(player, k.Value * 2);
                    }

                    string msg = "Winners: ";

                    foreach (var k in winList)
                    {
                        k.Key.BonusPL += k.Value;
                        msg += string.Format("{0} - {1} ({2}), ", k.Key.User, k.Value, k.Value / 2);
                    }

                    msg = msg.Substring(0, msg.Length - 2);

                    Send(msg);
                }
            }
            //!bet command, displays info about bets
            else if (cmd.Name == "!bet")
            {
                if (runningBet == null)
                {
                    Send("No bet is currently running. Bets allow viewers to bet on whether nat will win or lose a grift. \"Winning\" means killing the Rift Guardian before the timer expires.");
                    return;
                }

                if (runningBet.Open)
                {
                    Send(string.Format("A bet is open! Nat is going to run a {0} grift, type !betyes bidAmt or !betno bidAmt to bet, bidAmt is the amount of PL you want to bet, to a max of 150 PL", runningBet.GriftLevel));
                    return;
                }

                Send(string.Format("A bet is running but voting has closed! Nat is running a {0} grift, find out who wins when the grift is over!", runningBet.GriftLevel));
            }
            //!fight [player] command, allows a player to fight another player
            else if (cmd.Name == "!fight" && cmd.Args.Length == 1)
            {
                Player atk = GetPlayer(cmd.User);
                Player def = GetExistingPlayer(cmd.Args[0]);

                if (def == null)
                {
                    Send("Player not found natsK");
                    return;
                }
                else if (def == atk)
                {
                    Send("You can't attack yourself noob natsK");
                    return;
                }
                else if (banned.Contains(def.User))
                {
                    Send("You can't fight that player!");
                    return;
                }

                if (atk.PL < 100)
                {
                    Send(string.Format("{0}, you need at least 100 PL to fight!", atk.User));
                    return;
                }
                else if (def.PL < 100)
                {
                    Send(string.Format("{0} has less than 100 PL! Where is your honor!?", def.User));
                    return;
                }
                else if (atk.FightCD > 0)
                {
                    Send(string.Format("{0}, you can only fight once every 30 mins, you have to wait {1} longer.", atk.User, FormatTime(TimeSpan.FromSeconds(atk.FightCD))));
                    return;
                }

                int spoils = (int)Math.Round((def.PL * 0.05), 0);
                if (spoils < 1) spoils = 0;

                bool result = Fight(atk, def);
                if (result)
                {
                    Send(string.Format("{0} has bested {1} in honorable combat! They have received {2} power as a reward.", atk.User, def.User, spoils));
                    atk.BonusPL += spoils;
                }
                else
                {
                    spoils /= 2;
                    Send(string.Format("{0} has failed to best {1} in combat! They have lost {2} power as a result.", atk.User, def.User, spoils));
                    atk.BonusPL += spoils;
                }
            }
            //!ts command, displays teamspeak info
            else if (cmd.Name == "!ts")
            {
                Send("Teamspeak3 IP: 37.187.115.6 Password: Nats");
            }
            //!ping command, troll
            else if (cmd.Name == "!ping")
            {
                var i = new RNG().Next(0, 7);

                if (i == 0 || i == 1)
                {
                    Send("Ping! natsK");
                }
                else if (i == 2)
                {
                    Send("Too high to dash! natsK");
                }
                else if (i == 3)
                {
                    Send("Higher than PTR! natsK");
                }
                else
                {
                    Send("Pong!");
                }
            }
            //!ladder command, displays top 5 players
            else if (cmd.Name == "!ladder")
            {
                var top = GetTop();

                if (top.Count != 0)
                {
                    var message = "Top 5: ";
                    foreach (var p in top)
                    {
                        message += " [" + p.User + " - " + p.PL + "]";
                    }

                    Send(message);
                }

            }
            //!enter command, allows players to enter an event
            else if (cmd.Name == "!enter" && eventRunning != null)
            {
                var player = GetPlayer(cmd.User);
                if (eventRunning.eType == EventType.Sub250 && player.PL >= 250)
                {
                    Send(string.Format("{0}, this event is only available to those with less than 250, let the noobs have a chance BibleThump", cmd.User));
                    return;
                }

                eventRunning.Enter(player);
            }
            //!check/!scouter [player] command, allows a player to see the PL of another player
            else if ((cmd.Name == "!check" || cmd.Name == "!scouter") && cmd.Args.Length == 1)
            {
                Player player;
                if ((player = GetExistingPlayer(cmd.Args[0])) != null)
                {
                    Send(string.Format("{0}'s Power Level is {1} and they have {2} recorded view time.", player.User, player.PL, GetTime(player)));
                }
                else
                {
                    Send("Player not found natsK");
                    return;
                }
            }
        }

        //Simple loop to check the stream's status every minute
        private static void LiveCheck()
        {
            while (true)
            {
                Thread.Sleep(60000);
                IsLive = Live;
            }
        }
        //Main timer thread
        private static void TimerLoop()
        {
            lastSave = prevEvent = eventStart = prevSecond = DateTime.Now;

            //Each loop lasts 100.0057-100.0058 milliseconds
            while (true)
            {
                //Only do shit if the bot is actually connected
                if (irc.Connected)
                {
                    //For calculating ticklength
                    lastTick = DateTime.Now;
                    //Wait 100ms
                    Thread.Sleep(100);

                    //Remove duplicate Players
                    var list = new HashSet<Player>(playerList);
                    foreach (var p in list)
                    {
                        if (banned.Contains(p.User))
                            playerList.Remove(p);
                    }

                    var nameList = new HashSet<string>();
                    foreach (var p in list)
                    {
                        nameList.Add(p.User);
                    }

                    if (nameList.Count > playerList.Count)
                    {
                        RemoveDupes();
                    }

                    //Once per second
                    if (TimeSince(prevSecond).TotalMilliseconds >= 1000)
                    {
                        var temp = new HashSet<Player>(onlinePlayers);
                        foreach (var p in temp)
                        {
                            if (IsLive)
                                p.OnlineTime += 1;
                            else
                                p.OfflineTime += 1;

                            if (p.FightCD > 0)
                                p.FightCD--;

                            if (!PlayerExists(p.User))
                                playerList.Add(p);
                        }

                        //Write("##Added 1 second");
                        prevSecond = DateTime.Now;
                    }
                    //Save every 30 mins
                    if (TimeSince(lastSave).TotalMinutes >= 30)
                    {
                        Write("##Last save time: " + lastSave.ToString("hh:mm:ss"));
                        Write("##Saving...");
                        try
                        {
                            new Thread(new ThreadStart(Save)).Start();
                        }
                        catch (Exception e)
                        {
                            Write(e.Message);
                            Write(e.StackTrace);
                            Console.ReadLine();
                        }

                        Write("##Next save at: " + lastSave.Add(TimeSpan.FromMinutes(30)).ToString("hh:mm:ss"));
                        lastSave = DateTime.Now;
                    }
                    //Run an event every 20 minutes
                    if (TimeSince(prevEvent).TotalMinutes >= 20)
                    {
                        if (eventRunning == null && IsLive)
                        {
                            var rng = new RNG();
                            Write("##Event starting...");

                            eventRunning = ChatEvent.Events[ChatEvent.Events.Count - 1];//rng.Next(0, ChatEvent.Events.Count - 1)];
                            Send(eventRunning.Message + " Type !enter to enter the draw.");

                            eventStart = DateTime.Now;
                            eventStage = 0;
                        }

                        prevEvent = DateTime.Now;
                    }
                    //Escalate the stage of an event every 30 secs
                    if (eventRunning != null)
                    {
                        var time = TimeSince(eventStart);

                        if (time.TotalSeconds >= 30)
                        {
                            switch (eventStage)
                            {
                                case 0:
                                    if (eventRunning.Entrants.Count > 0)
                                    {
                                        var names = "";
                                        foreach (var p in eventRunning.Entrants)
                                        {
                                            names += p.User + ", ";
                                        }
                                        names = names.Substring(0, names.Length - 2);

                                        Send("Currently in the draw: " + names + " There are only 60 seconds left to enter!");
                                    }
                                    else
                                    {
                                        Send("Nobody has entered the draw yet! There are only 60 seconds left to enter!");
                                    }
                                    eventStart = DateTime.Now;
                                    eventStage++;
                                    break;
                                case 1:
                                    if (eventRunning.Entrants.Count > 0)
                                    {
                                        var names = "";
                                        foreach (var p in eventRunning.Entrants)
                                        {
                                            names += p.User + ", ";
                                        }
                                        names = names.Substring(0, names.Length - 2);

                                        Send("Currently in the draw: " + names + " There are only 30 seconds left to enter!");
                                    }
                                    else
                                    {
                                        Send("Nobody has entered the draw yet! There are only 30 seconds left to enter!");
                                    }
                                    eventStart = DateTime.Now;
                                    eventStage++;
                                    break;
                                case 2:
                                    var winner = eventRunning.Complete();
                                    if (winner != null)
                                    {
                                        int spoils = eventRunning.Payout;
                                        Send("The winner is " + winner.User + "! You feel your power has increased. (" + spoils + " power level)");
                                        winner.BonusPL += spoils;
                                        eventRunning = null;
                                    }
                                    else
                                    {
                                        Send("As nobody entered the draw, the opportunity to gain power was wasted");
                                    }
                                    eventStart = DateTime.Now;

                                    eventRunning = null;

                                    break;
                                default:
                                    break;
                            }
                        }
                    }

                    //Write("##Tick Time: " + TimeSince(lastTick).TotalMilliseconds);
                }
            }
        }

        //Twitch is really bad at telling you who's in a channel, this helps
        private static void handleOutput(SkidsDev.EasyIRC.Message msg)
        {
            string message = "";

            if (msg.Type == "PRIVMSG" || msg.Type == "JOIN")
            {
                var p = GetExistingPlayer(msg.User);
                if (p == null)
                    p = new Player(msg.User);

                playerList.Add(p);

                if (!PlayerOnline(msg.User))
                    onlinePlayers.Add(p);
            }
            else if (msg.Type == "PART")
            {
                Player player;
                if ((player = GetPlayer(msg.User)) != null && PlayerOnline(msg.User))
                {
                    onlinePlayers.Remove(player);
                }
            }

            if (msg.Type == "PRIVMSG")
                message = string.Format("[{0}]<{1}>: {2}", msg.Channel, msg.User, msg.Msg);
            else if (msg.Type == "JOIN")
                message = string.Format("[{0}]<{1} has joined>", msg.Channel, msg.User);

            if (!String.IsNullOrWhiteSpace(message))
                Write(message);
        }

        //Check if a player is online
        private static bool PlayerOnline(string name)
        {
            var temp = new List<Player>(onlinePlayers);
            foreach (var p in temp)
            {
                if (p.User == name)
                    return true;
            }

            return false;
        }
        //Check if a player has savedata
        private static bool PlayerExists(string name)
        {
            return GetExistingPlayer(name) != null;
        }
        //Get savedata, return null if that player has no savedata
        private static Player GetExistingPlayer(string name)
        {
            var temp = new List<Player>(playerList);
            foreach (var p in temp)
            {
                if (p.User == name)
                    return p;
            }

            return null;
        }
        //Get savedata, create new savedata record if that player has no savedata
        private static Player GetPlayer(string name)
        {
            var temp = new List<Player>(playerList);
            foreach (var p in temp)
            {
                if (p.User == name)
                    return p;
            }

            temp = new List<Player>(onlinePlayers);
            foreach (var p in temp)
            {
                if (p.User == name)
                    return p;
            }

            var player = new Player(name);
            onlinePlayers.Add(player);
            return player;
        }
        //Calculate the top 5 players
        private static List<Player> GetTop()
        {
            RemoveDupes();

            List<Player> list = new List<Player>(playerList);
            if (list.Count == 0)
            {
                throw new InvalidOperationException("Empty list");
            }
            List<Player> maxPL = new List<Player>() { new Player(""), new Player(""), new Player(""), new Player(""), new Player("") };
            foreach (Player type in list)
            {
                if (type.PL > maxPL[0].PL)
                {
                    maxPL[0] = type;
                }
            }
            foreach (Player type in list)
            {
                if (type.PL > maxPL[1].PL && type.PL <= maxPL[0].PL && type.User != maxPL[0].User)
                {
                    maxPL[1] = type;
                }
            }
            foreach (Player type in list)
            {
                if (type.PL > maxPL[2].PL && type.PL <= maxPL[1].PL && type.User != maxPL[1].User && type.User != maxPL[0].User)
                {
                    maxPL[2] = type;
                }
            }
            foreach (Player type in list)
            {
                if (type.PL > maxPL[3].PL && type.PL <= maxPL[2].PL && type.User != maxPL[2].User && type.User != maxPL[1].User && type.User != maxPL[0].User)
                {
                    maxPL[3] = type;
                }
            }
            foreach (Player type in list)
            {
                if (type.PL > maxPL[4].PL && type.PL <= maxPL[3].PL && type.User != maxPL[3].User && type.User != maxPL[2].User && type.User != maxPL[1].User && type.User != maxPL[0].User)
                {
                    maxPL[4] = type;
                }
            }

            return maxPL;
        }
        //Return a TimeSpan indicating the amount of time that has passed since the given argument
        private static TimeSpan TimeSince(DateTime time)
        {
            return DateTime.Now.Subtract(time);
        }

        //Combine 2 Player records
        private static Player Combine(Player a, Player b)
        {
            var p = new Player(a.User);
            p.BonusPL = a.BonusPL + b.BonusPL;
            p.OnlineTime = a.OnlineTime + b.OnlineTime;
            p.OfflineTime = a.OfflineTime + b.OfflineTime;

            return p;
        }
        //Remove duplicate player records
        private static void RemoveDupes()
        {
            var noDupes = new HashSet<Player>();
            var dupes = new HashSet<Player>();

            var list = new HashSet<Player>(playerList);

            foreach (var p in list)
            {
                if (!HashSetContains(noDupes, p.User))
                    noDupes.Add(p);
                else
                    dupes.Add(p);
            }
            var plist = new List<Player>(noDupes);
            for (int i = 0; i < plist.Count; i++)
            {
                var p = plist[i];

                foreach (var d in dupes)
                {
                    if (p.User == d.User)
                    {
                        p = Combine(p, d);
                    }
                }
            }

            noDupes = new HashSet<Player>(plist);

            playerList = noDupes;
        }
        //Generic Helper Method
        private static bool HashSetContains(HashSet<Player> list, string user)
        {
            foreach (var p in list)
            {
                if (p.User == user)
                    return true;
            }

            return false;
        }
        //Calculate the winner of a fight
        private static bool Fight(Player atk, Player def)
        {
            double chance = 0;
            chance = (def.PL + atk.PL);
            chance = def.PL / chance;
            chance *= 100;
            chance = Math.Round(chance, 2);

            Write("##Win Chance: " + (100 - chance) + "%");
            double roll = Math.Round((new RNG().Next(0, 100) + new RNG().NextDouble()), 2);
            if (roll > 100) roll = 100;
            Write("##Win Roll: " + roll);

            atk.FightCD += 1800;

            return (roll > chance);
        }
        //Send a message to the IRC
        public static void Send(string msg)
        {
            irc.SendMessage(channel, msg);
        }
        //Write a message to the console, with timestamp
        public static void Write(string msg)
        {
            Console.WriteLine("[" + DateTime.Now.ToString("hh:mm:ss") + "]" + msg);
        }

        //Helper method
        private static bool SaveExists(Player player, List<Player> list)
        {
            foreach (var p in list)
            {
                if (p.User == player.User)
                    return true;
            }

            return false;
        }
        //Save data
        public static void Save()
        {
            var list = new List<Player>(playerList);
            foreach (var p in list)
            {
                if (banned.Contains(p.User))
                    playerList.Remove(p);
            }

            HashSet<Player> exPL = new HashSet<Player>(playerList);
            List<Player> exOP = new List<Player>(onlinePlayers);

            if (!Directory.Exists(dirPath))
                Directory.CreateDirectory(dirPath);

            if (File.Exists(dirPath + "\\Save.sqlite"))
                File.Delete(dirPath + "\\Save.sqlite");
                
            CreateDB();

            var existing = Load();

            using (var db = new SQLiteConnection(string.Format("Data Source={0}; Version=3", dirPath + "\\Save.sqlite")))
            {
                db.Open();

                foreach (var p in exOP)
                {
                    if (playerList.Contains(p)) playerList.Remove(p);

                    playerList.Add(p);
                }

                var temp = new List<Player>(playerList);
                temp.Sort((x, y) => string.Compare(x.User, y.User));
                exPL = new HashSet<Player>(temp);

                foreach (Player p in exPL)
                {
                    if (!banned.Contains(p.User))
                    {
                        SQLiteCommand cmd;
                        //if (!SaveExists(p, existing))
                        //{
                            cmd = new SQLiteCommand(string.Format("INSERT INTO Users (Name, BonusPL, OnlineTime, OfflineTime) VALUES ('{0}', {1}, {2}, {3});",
                                p.User, p.BonusPL, p.OnlineTime, p.OfflineTime), db);
                        /*}
                        else
                        {
                            cmd = new SQLiteCommand(string.Format("UPDATE Users SET BonusPL={0}, OnlineTime={1}, OfflineTime{2} WHERE Name='{3}';", p.BonusPL, p.OnlineTime, p.OfflineTime, p.User), db);
                        }*/

                        cmd.ExecuteNonQuery();
                    }
                }

                db.Close();
            }
            /*using (var db = new SQLiteConnection(string.Format("Data Source=Saves{0}; Version=3", "\\Save.sqlite")))
            {
                db.Open();

                for (int i = 0; i < dragonballs.Length; i++)
                {
                    if (dragonballs[i] == null)
                    {
                        var cmd = new SQLiteCommand("INSERT INTO Dragonballs (ID) VALUES (" + i + ");", db);
                        cmd.ExecuteNonQuery();
                    }
                    else
                    {
                        var cmd = new SQLiteCommand("INSERT INTO Dragonballs (ID, Name) VALUES (" + i + ", '" + dragonballs[i].user + "');", db);
                        cmd.ExecuteNonQuery();
                    }
                }

                db.Close();
            }*/

            Write("##Save Complete");
        }
        //Create SQLite database
        private static void CreateDB()
        {
            SQLiteConnection.CreateFile(dirPath + "\\Save.sqlite");

            using (var db = new SQLiteConnection(string.Format("Data Source={0}; Version=3", dirPath + "\\Save.sqlite")))
            {
                db.Open();
                var cmd = new SQLiteCommand("CREATE TABLE Users(ID INTEGER PRIMARY KEY AUTOINCREMENT, Name CHAR(64), BonusPL INT, OnlineTime INT, OfflineTime INT);", db);
                cmd.ExecuteNonQuery();
                cmd = new SQLiteCommand("CREATE TABLE Dragonballs(ID INT PRIMARY KEY UNIQUE, Name CHAR(64));", db);
                cmd.ExecuteNonQuery();
                db.Close();
            }
        }
        //Load data
        public static HashSet<Player> Load()
        {
            HashSet<Player> pList = new HashSet<Player>();
            bool no = false;

            if (!Directory.Exists(dirPath))
                no = true;
            if (!File.Exists(dirPath + "\\Save.sqlite"))
                no = true;

            if (no)
            {
                CreateDB();
                return pList;
            }

            using (var db = new SQLiteConnection(string.Format("Data Source={0}; Version=3", dirPath + "\\Save.sqlite")))
            {
                db.Open();

                var cmd = new SQLiteCommand("SELECT * FROM Users;", db);
                var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    var p = new Player((string)reader["Name"]);
                    p.BonusPL = (int)reader["BonusPL"];
                    p.OnlineTime = (int)reader["OnlineTime"];
                    p.OfflineTime = (int)reader["OfflineTime"];
                    pList.Add(p);
                }

                db.Close();
            }
            /*using (var db = new SQLiteConnection(string.Format("Data Source=Saves{0}; Version=3", "\\Save.sqlite")))
            {
                db.Open();

                var cmd = new SQLiteCommand("SELECT * FROM Users;", db);
                var reader = cmd.ExecuteReader();
                for (int i = 0; i < dragonballs.Length; i++)
                {
                    if (reader.Read())
                    {
                        if (reader["Name"] == null)
                        {
                            dragonballs[i] = null;
                        }
                        else
                        {
                            dragonballs[i] = Player.GetPlayer((string)reader["Name"], playerList);
                        }
                    }
                }

                db.Close();
            }*/

            return pList;
        }
    }
}
