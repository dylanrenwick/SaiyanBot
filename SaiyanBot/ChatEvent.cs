using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using GunnerWolfLib;

namespace SaiyanBot2
{
    public class ChatEvent
    {
        private static ChatEvent _timeChamber;
        private static ChatEvent _senzuBean;
        private static ChatEvent _kaiTraining;
        private static ChatEvent _martialArts;
        private static ChatEvent _roshiTraining;
        private static ChatEvent _gravChamber;
        private static ChatEvent _rejuvTank;
        private static ChatEvent _korinTraining;

        private static List<ChatEvent> _events;

        public static List<ChatEvent> Events
        {
            get
            {
                if (_events == null)
                {
                    var list = new List<ChatEvent>();
                    if (_timeChamber == null)
                        _timeChamber = new ChatEvent(EventType.Regular, 
                            "An opportunity to enter the Hyperbolic Time Chamber has opened, he who enters shall receive a significant Power boost.",
                            50);

                    if (_senzuBean == null)
                        _senzuBean = new ChatEvent(EventType.Regular, 
                            "A Senzu Bean has been found! Eating this bean will grand immense power to whomever consumes it.",
                            100);

                    if (_kaiTraining == null)
                        _kaiTraining = new ChatEvent(EventType.Regular, 
                            "The Kais are looking for an apprentice to train, their intence regiment gives great power to whomever receives such an honor.",
                            50);

                    if (_martialArts == null)
                        _martialArts = new ChatEvent(EventType.Regular, 
                            "The World Martial Arts Tournament is beginning! The winner will be granted great power.",
                            30);

                    if (_roshiTraining == null)
                        _roshiTraining = new ChatEvent(EventType.Sub500, 
                            "Master Roshi is looking for an apprentice to train.",
                            20);

                    if (_gravChamber == null)
                        _gravChamber = new ChatEvent(EventType.Regular, 
                            "An opportunity to enter the Gravity Chamber has opened, he who enters shall receive a significant Power boost.",
                            20);

                    if (_rejuvTank == null)
                        _rejuvTank = new ChatEvent(EventType.Regular, 
                            "An opportunity to enter the Rejuvination Tank has opened, he who enters shall receive a significant Power boost.",
                            20);

                    if (_korinTraining == null)
                        _korinTraining = new ChatEvent(EventType.Sub250, 
                            "Korin is looking for an apprentice to train.",
                            20);

                    list.Add(_timeChamber);
                    list.Add(_senzuBean);
                    list.Add(_kaiTraining);
                    list.Add(_martialArts);
                    list.Add(_roshiTraining);
                    list.Add(_gravChamber);
                    list.Add(_rejuvTank);
                    list.Add(_korinTraining);

                    _events = list;
                }

                return _events;
            }
        }

        private RNG rng;

        public List<Player> Entrants;
        public int Payout;
        public string Message;
        public EventType eType { get; private set; }

        private ChatEvent(EventType type, string message, int pay)
        {
            rng = new RNG();

            eType = type;

            Message = message;

            if (eType == EventType.Sub250)
                Message += " This event is only available to those with less than 250 PL.";
            else if (eType == EventType.Sub500)
                Message += " This event is only available to those with less than 500 PL.";

            Payout = pay;

            Entrants = new List<Player>();
        }

        public void Reset()
        {
            Entrants = new List<Player>();
        }

        public void Enter(Player player)
        {
            if (!Entrants.Contains(player))
            {
                Entrants.Add(player);
            }
        }

        public Player Complete()
        {
            if (Entrants.Count <= 0)
                return null;
            else if (Entrants.Count == 1)
                return Entrants[0];
            else
            {
                var winner = Entrants[rng.Next(0, Entrants.Count - 1)];
                if (eType == EventType.Sub250)
                    Payout = (int)Math.Ceiling((double)(250 - winner.PL) / 2);
                else if (eType == EventType.Sub500)
                    Payout = (int)Math.Ceiling((double)(500 - winner.PL) / 2);
                return winner;
            }
        }
    }

    public enum EventType
    {
        Regular,
        Sub250,
        Sub500
    }
}
