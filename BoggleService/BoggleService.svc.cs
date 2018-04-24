using Newtonsoft.Json;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Dynamic;
using System.IO;
using System.Net;
using System.ServiceModel.Web;
using static System.Net.HttpStatusCode;

namespace Boggle
{
    public class BoggleService : IBoggleService
    {
        private static readonly ConcurrentDictionary<string, string> users = new ConcurrentDictionary<string, string>();
        private static readonly ConcurrentQueue<Game> pendingGames = new ConcurrentQueue<Game>();
        private static readonly ConcurrentDictionary<string, Game> activeGames = new ConcurrentDictionary<string, Game>();
        private static readonly ConcurrentDictionary<string, Game> completedGames = new ConcurrentDictionary<string, Game>();
        private static readonly HashSet<string> dictionary;

        static BoggleService()
        {
            dictionary = new HashSet<string>();
            string line;
            using (StreamReader file = new System.IO.StreamReader(AppDomain.CurrentDomain.BaseDirectory + "dictionary.txt"))
            {
                while ((line = file.ReadLine()) != null)
                {
                    dictionary.Add(line.ToLower().Trim());
                }
            }
        }

        /// <summary>
        /// The most recent call to SetStatus determines the response code used when
        /// an http response is sent.
        /// </summary>
        /// <param name="status"></param>
        private static void SetStatus(HttpStatusCode status)
        {
            WebOperationContext.Current.OutgoingResponse.StatusCode = status;
        }

        /// <summary>
        /// Returns a Stream version of index.html.
        /// </summary>
        /// <returns></returns>
        public Stream API()
        {
            SetStatus(OK);
            WebOperationContext.Current.OutgoingResponse.ContentType = "text/html";
            return File.OpenRead(AppDomain.CurrentDomain.BaseDirectory + "index.html");
        }

        /// <summary>
        /// Handles cancelling a user's registration for a pending game.
        /// </summary>
        /// <param name="user"></param>
        public void CancelJoin(UserInfo user)
        {
            if (users.ContainsKey(user.UserToken))
            {
                if (InGame(user.UserToken, pendingGames))
                {
                    Game g;
                    if (pendingGames.TryDequeue(out g))
                    {
                        SetStatus(OK);
                        return;
                    }
                }
            }
            SetStatus(Forbidden);
            return;
        }

        /// <summary>
        /// Handles processing and returning of game status data.
        /// </summary>
        /// <param name="id">Id of the game to retrieve data for.</param>
        /// <param name="brief">Whether or not the data should be brief.</param>
        /// <returns>Desired status info as available for the game.</returns>
        public StatusInfo GameStatus(string id, string brief)
        {
            brief = string.IsNullOrEmpty(brief) ? "no" : brief.ToLower();
            Game g = GetGame(id);
            if (g != null)
            {
                if (g.GameState == Game.Status.pending)
                {
                    SetStatus(OK);
                    return new StatusInfo() { GameState = "pending" };
                }
                else
                {
                    CheckGameComplete(g);
                    int timePassed = (int)(DateTime.Now - g.TimeStarted).TotalSeconds;
                    return new StatusInfo()
                    {
                        GameState = g.GameState.ToString(),
                        Board = g.Board.ToString(),
                        TimeLimit = g.TimeLimit,
                        TimeLeft = timePassed >= g.TimeLimit ? 0 : g.TimeLimit - timePassed,
                        Player1 = new PlayerInfo
                        {
                            Nickname = users[g.PlayerOne.UserToken],
                            Score = GetScore(g.PlayerOneWords),
                            WordsPlayed = brief.Equals("no") ? CollectWordData(g.PlayerOneWords) : null
                        },
                        Player2 = new PlayerInfo
                        {
                            Nickname = users[g.PlayerTwo.UserToken],
                            Score = GetScore(g.PlayerTwoWords),
                            WordsPlayed = brief.Equals("no") ? CollectWordData(g.PlayerTwoWords) : null
                        }
                    };
                }
            }
            else
            {
                SetStatus(Forbidden);
                return null;
            }
        }

        private bool CheckGameComplete(Game g)
        {
            if ((DateTime.Now - g.TimeStarted).TotalSeconds >= g.TimeLimit)
            {
                g.GameState = Game.Status.completed;
                completedGames.TryAdd(g.ID, g);
                return true;
            }
            return false;
        }

        private static WordInfo[] CollectWordData(IDictionary<string, int> dic)
        {
            WordInfo[] arr = new WordInfo[dic.Count];
            int i = 0;
            foreach (KeyValuePair<string, int> pair in dic)
            {
                arr[i] = new WordInfo() { Word = pair.Key, Score = pair.Value };
                i++;
            }
            return arr;
        }

        private static int GetScore(IDictionary<string, int> dic)
        {
            int total = 0;
            foreach (int i in dic.Values)
            {
                total += i;
            }
            return total;
        }

        private Game GetGame(string id)
        {
            Game g;
            return SearchForGame(id, pendingGames, out g) ? g : activeGames.TryGetValue(id, out g) ? g : completedGames.TryGetValue(id, out g) ? g : null;
        }

        private bool SearchForGame(string id, ConcurrentQueue<Game> games, out Game game)
        {
            foreach (Game g in games)
            {
                if (g.ID.Equals(id))
                {
                    game = g;
                    return true;
                }
            }
            game = null;
            return false;
        }

        /// <summary>
        /// Handles joining a pending Boggle game.
        /// </summary>
        /// <param name="user">Data detailing the user token and the time of the match</param>
        /// <returns>Data containing the game's ID.</returns>
        public GameInfo Join(JoinInfo user)
        {
            if (!string.IsNullOrEmpty(user.UserToken) && users.ContainsKey(user.UserToken) && user.TimeLimit >= 5 && user.TimeLimit <= 120)
            {
                if (!InGame(user.UserToken, pendingGames))
                {
                    Game g;
                    if (pendingGames.TryDequeue(out g))
                    {
                        if (g.PlayerOne != null)
                        {
                            GameInfo info = JoinAsPlayerTwo(user, g);
                            activeGames.TryAdd(g.ID, g);
                            g.Start();
                            return info;
                        }
                        else
                        {
                            return JoinAsPlayerOne(user, g);
                        }
                    }
                    else
                    {
                        g = new Game();
                        g.PlayerOne = user;
                        g.ID = activeGames.Count + completedGames.Count + pendingGames.Count + 1 + "";
                        g.GameState = Game.Status.pending;
                        pendingGames.Enqueue(g);
                        SetStatus(Accepted);
                        return new GameInfo() { GameID = g.ID };
                    }
                }
                SetStatus(Conflict);
                return null;
            }
            SetStatus(Forbidden);
            return null;
        }

        private GameInfo JoinAsPlayerTwo(JoinInfo user, Game g)
        {
            g.PlayerTwo = user;
            g.TimeLimit = (g.PlayerOne.TimeLimit + user.TimeLimit) / 2;
            g.Board = new BoggleBoard();
            g.GameState = Game.Status.active;
            SetStatus(Created);
            return new GameInfo() { GameID = g.ID };
        }

        private GameInfo JoinAsPlayerOne(JoinInfo user, Game g)
        {
            g.PlayerOne = user;
            g.ID = activeGames.Count + completedGames.Count + pendingGames.Count + 1 + "";
            g.GameState = Game.Status.pending;
            SetStatus(Accepted);
            return new GameInfo() { GameID = g.ID };
        }

        private bool InGame(string userToken, ConcurrentQueue<Game> pending)
        {
            foreach (Game g in pending)
            {
                if ((g.PlayerOne != null && g.PlayerOne.UserToken.Equals(userToken)) || (g.PlayerTwo != null && g.PlayerTwo.UserToken.Equals(userToken)))
                {
                    return true;
                }
            }
            return false;
        }

        private bool InGame(string userToken, ICollection<Game> gameBag)
        {
            foreach (Game g in gameBag)
            {
                if ((g.PlayerOne != null && g.PlayerOne.UserToken.Equals(userToken)) || (g.PlayerTwo != null && g.PlayerTwo.UserToken.Equals(userToken)))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Handles playing a word to the boggle game
        /// </summary>
        /// <param name="id">ID of the game to submit a word to</param>
        /// <param name="play">Info detailing what player played what word.</param>
        /// <returns>Data containing the score that word had.</returns>
        public ScoreInfo PlayWord(string id, PlayInfo play)
        {
            string word = play.Word.ToLower().Trim();
            if (!string.IsNullOrEmpty(word))
            {
                Game g;
                if (activeGames.TryGetValue(id, out g))
                {
                    if (!CheckGameComplete(g))
                    {
                        if (g.ID.Equals(id))
                        {
                            int score = (g.PlayerOneWords.ContainsKey(word) || g.PlayerTwoWords.ContainsKey(word) || (word.Length < 3)) ? 0 : g.Board.CanBeFormed(word) && dictionary.Contains(word) ? WordScore(word) : -1;
                            if (play.UserToken.Equals(g.PlayerOne.UserToken))
                            {
                                g.PlayerOneWords.Add(word, score);
                            }
                            else if (play.UserToken.Equals(g.PlayerTwo.UserToken))
                            {
                                g.PlayerTwoWords.Add(word, score);
                            }
                            else
                            {
                                SetStatus(Forbidden);
                                return null;
                            }
                            SetStatus(OK);
                            return new ScoreInfo() { Score = score };
                        }
                    }
                }

                SetStatus(Conflict);
                return null;
            }
            SetStatus(Forbidden);
            return null;
        }

        public int WordScore(string word)
        {
            switch (word.Length)
            {
                case 3:
                    return 1;
                case 4:
                    return 1;
                case 5:
                    return 2;
                case 6:
                    return 3;
                case 7:
                    return 5;
                default:
                    return 11;
            }
        }

        /// <summary>
        /// Registers the user into the database
        /// </summary>
        /// <param name="user">Required registration info.</param>
        /// <returns>The user's user token.</returns>
        public UserInfo Register(RegisterInfo user)
        {
            if (user.Nickname == null || user.Nickname.Trim().Length == 0)
            {
                SetStatus(Forbidden);
                return null;
            }
            UserInfo t = new UserInfo();
            t.UserToken = Guid.NewGuid().ToString();
            Console.WriteLine(t.UserToken);
            users.TryAdd(t.UserToken, user.Nickname);
            SetStatus(Created);
            return t;
        }
    }
}
