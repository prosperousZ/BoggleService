using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.Serialization;
using System.Web;

namespace Boggle
{
    public class RegisterInfo
    {
        public string Nickname { get; set; }
    }

    public class UserInfo
    {
        public string UserToken { get; set; }
    }

    public class JoinInfo
    {
        public string UserToken { get; set; }
        public int TimeLimit { get; set; }
    }

    public class Game
    {
        public string ID { get; set; }
        public Status GameState { get; set; }
        public JoinInfo PlayerOne { get; set; }
        public JoinInfo PlayerTwo { get; set; }
        public int TimeLimit { get; set; }
        public DateTime TimeStarted { get; private set; }
        public BoggleBoard Board { get; set; }
        public IDictionary<string, int> PlayerOneWords { get; private set; }
        public IDictionary<string, int> PlayerTwoWords { get; private set; }

        public Game()
        {
            this.PlayerOneWords = new Dictionary<string, int>();
            this.PlayerTwoWords = new Dictionary<string, int>();
        }

        public void Start()
        {
            this.TimeStarted = DateTime.Now;
        }

        public enum Status
        {
            pending,
            active,
            completed
        }
    }

    [DataContract]
    public class StatusInfo
    {

        [DataMember]
        public string GameState { get; set; }

        [DataMember(EmitDefaultValue = false)]
        public string Board { get; set; }

        [DataMember(EmitDefaultValue = false)]
        public int TimeLimit { get; set; }

        [DataMember(EmitDefaultValue = false)]
        public object TimeLeft { get; set; }

        [DataMember(EmitDefaultValue = false)]
        public PlayerInfo Player1 { get; set; }

        [DataMember(EmitDefaultValue = false)]
        public PlayerInfo Player2 { get; set; }
    }

    [DataContract]
    public class PlayerInfo
    {
        [DataMember(EmitDefaultValue = false)]
        public string Nickname { get; set; }

        [DataMember(EmitDefaultValue = true)]
        public int Score { get; set; }

        [DataMember(EmitDefaultValue = false)]
        public WordInfo[] WordsPlayed { get; set; }
    }

    public class WordInfo
    {
        public string Word { get; set; }
        public int Score { get; set; }
    }

    public class GameInfo
    {
        public string GameID { get; set; }
    }

    public class ScoreInfo
    {
        public int Score { get; set; }
    }

    public class PlayInfo
    {
        public string UserToken { get; set; }
        public string Word { get; set; }
    }
}