using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SaiyanBot2
{
    public class GriftBet
    {
        public Dictionary<string, int> YesVotes;
        public Dictionary<string, int> NoVotes;
        public int GriftLevel;

        public bool Open { get; private set; }

        public GriftBet(int grift)
        {
            GriftLevel = grift;
            YesVotes = new Dictionary<string,int>();
            NoVotes = new Dictionary<string,int>();

            Open = true;
        }

        public void VoteYes(Player player, int amt)
        {
            if (Open)
            {
                if (VotedNo(player))
                {
                    NoVotes.Remove(player.User);
                }
                if (VotedYes(player))
                {
                    YesVotes.Remove(player.User);
                }

                YesVotes.Add(player.User, amt);
            }
        }

        public void VoteNo(Player player, int amt)
        {
            if (Open)
            {
                if (VotedNo(player))
                {
                    NoVotes.Remove(player.User);
                }
                if (VotedYes(player))
                {
                    YesVotes.Remove(player.User);
                }

                NoVotes.Add(player.User, amt);
            }
        }

        private bool VotedYes(Player p)
        {
            return YesVotes.ContainsKey(p.User);
        }

        private bool VotedNo(Player p)
        {
            return NoVotes.ContainsKey(p.User);
        }

        public void CloseBids()
        {
            Open = false;
        }
    }
}
