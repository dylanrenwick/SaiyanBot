using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SaiyanBot2
{
    public class Player
    {
        public string User { get; private set; }
        public int BonusPL;
        public double OnlineTime;
        public double OfflineTime;
        public double FightCD;
        public int PL
        {
            get
            {
                return (int)Math.Truncate(BonusPL + (OnlineTime / 60) + ((OfflineTime / 60) / 15));
            }
        }

        public Player(string user)
        {
            User = user;
            OnlineTime = 0;
            OfflineTime = 0;
            BonusPL = 0;
            FightCD = 0;
        }
    }
}
