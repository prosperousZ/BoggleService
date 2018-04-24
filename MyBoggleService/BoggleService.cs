using Newtonsoft.Json;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Dynamic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using static System.Net.HttpStatusCode;

namespace Boggle
{
    /// <summary>
    /// Class boggleService
    /// </summary>
    public class BoggleService
    {
        // use the dictionary to hold the information
        private static readonly ConcurrentDictionary<string, string> users = new ConcurrentDictionary<string, string>();
        private static readonly ConcurrentQueue<Game> pendingGames = new ConcurrentQueue<Game>();
        private static readonly ConcurrentDictionary<string, Game> activeGames = new ConcurrentDictionary<string, Game>();
        private static readonly ConcurrentDictionary<string, Game> completedGames = new ConcurrentDictionary<string, Game>();
        private static readonly HashSet<string> dictionary = new HashSet<string>();
        private TcpListener server;

        /// <summary>
        /// without port
        /// </summary>
        public BoggleService()
        {
            BuildDictionary();
        }

        /// <summary>
        /// can be used in the specific port
        /// </summary>
        /// <param name="port"></param>
        public BoggleService(int port)
        {
            BuildDictionary();
            server = new TcpListener(IPAddress.Any, port);
            server.Start();
            server.BeginAcceptSocket(ConnectionRequested, null);
        }

        /// <summary>
        /// create a dictionary which will hold the dictionary.txt information
        /// </summary>
        private static void BuildDictionary()
        {
            string line;
            using (StreamReader file = new System.IO.StreamReader("dictionary.txt"))
            {
                // read all the words
                while ((line = file.ReadLine()) != null)
                {
                    dictionary.Add(line.ToLower().Trim());
                }
            }
        }

        /// <summary>
        /// try to connect the client to the socket
        /// </summary>
        /// <param name="result"></param>
        private void ConnectionRequested(IAsyncResult result)
        {
            Socket s = server.EndAcceptSocket(result);
            server.BeginAcceptSocket(ConnectionRequested, null);
            new ClientConnection(s);
        }


        /// <summary>
        /// This is the class for connection, by solve the bytes and strings for the socket,
        /// the connection can be achived.
        /// In this calss, the information can be sent and received, the form of the information is sting 
        /// </summary>
        class ClientConnection
        {
            private static System.Text.UTF8Encoding encoding = new System.Text.UTF8Encoding();
            private const int BUFFER_SIZE = 1024;
            private Socket socket;
            // need the information to be string
            private StringBuilder incoming;
            private StringBuilder outgoing;
            private Decoder decoder = encoding.GetDecoder();
            // get the incoming information
            private byte[] incomingBytes = new byte[BUFFER_SIZE];
            private char[] incomingChars = new char[BUFFER_SIZE];
            private bool sendIsOngoing = false;
            private readonly object sendSync = new object();
            private byte[] pendingBytes = new byte[0];
            // tracking the size
            private int pendingIndex = 0;
            private int contentLengthIn = 0;
            // use http here
            private dynamic httpRequestData = new ExpandoObject();
            private bool contentRead = false;
            private bool endOfHeaders = false;

            /// <summary>
            /// connect to the socket, and begin to receive
            /// </summary>
            /// <param name="s"></param>
            public ClientConnection(Socket s)
            {
                socket = s;
                incoming = new StringBuilder();
                outgoing = new StringBuilder();
                socket.BeginReceive(incomingBytes, 0, incomingBytes.Length, SocketFlags.None, MessageReceived, null);
            }

            /// <summary>
            /// recive the messsage from the other client with the string
            /// </summary>
            /// <param name="result"></param>
            private void MessageReceived(IAsyncResult result)
            {
                // if something need to be recived
                int bytesRead = socket.EndReceive(result);
                if (bytesRead == 0)
                {
                    Console.WriteLine("Socket closed");
                    socket.Close();
                }

                // recive the information
                else
                {
                    int charsRead = decoder.GetChars(incomingBytes, 0, bytesRead, incomingChars, 0, false);
                    incoming.Append(incomingChars, 0, charsRead);
                    // change the recived message to string format
                    if (incoming.Length == contentLengthIn)
                    {
                        httpRequestData.contentIn = incoming.ToString(0, contentLengthIn);
                        incoming.Remove(0, contentLengthIn);
                        //if (Regex.IsMatch(incoming.ToString(0, contentLengthIn), @"^({)((("")[a-zA-Z]+("":)(""|[a-zA-Z0-9\-])*[a-zA-Z0-9\-]*(""|[a-zA-Z0-9\-])(,))|(("")[a-zA-Z]+("":)(""|[a-zA-Z0-9\-])*[a-zA-Z0-9\-]*(""|[a-zA-Z0-9\-])))+(})$"))
                        //{
                        //    httpRequestData.contentIn = incoming.ToString(0, contentLengthIn);
                        //    incoming.Remove(0, contentLengthIn);
                        //}
                        contentRead = true;
                    }
                    // go through all the words
                    int lastNewline = -1;
                    int start = 0;
                    int lineNumber = 1;
                    for (int i = 0; i < incoming.Length; i++)
                    {
                        if (incoming[i] == '\n')
                        {
                            String line = incoming.ToString(start, i + 1 - start);
                            if (lineNumber == 1)
                            {
                                httpRequestData.parameters = "";
                                if (ValidServiceMethod(line.Split(' ')[0]))
                                {
                                    httpRequestData.serviceMethod = line.Split(' ')[0];
                                }
                                string[] url = line.Split(' ')[1].Split('/');
                                if (ValidDirectory(url[2]))
                                {
                                    httpRequestData.directory = url[2];
                                }
                                if (url.Length == 4)
                                {
                                    httpRequestData.parameters = url[3];
                                }
                            }
                            // go the the next byte group
                            else if (line.Split(' ')[0] == "Content-Length:" || line.Split(' ')[0] == "content-length:")
                            {
                                contentLengthIn = int.Parse(line.Split(' ')[1]);
                            }
                            // go the the next lines
                            else if (line == "\r\n")
                            {
                                endOfHeaders = true;
                            }
                            lastNewline = i;
                            start = i + 1;
                            lineNumber++;
                        }
                    }
                    // find the header and read them
                    incoming.Remove(0, lastNewline + 1);
                    if (contentRead && endOfHeaders)
                    {
                        SendResponse();
                        contentRead = false;
                        endOfHeaders = false;
                    }
                    // find the end of the header
                    else if (!contentRead && endOfHeaders && (incoming.Length > 0))
                    {
                        httpRequestData.contentIn = incoming.ToString(0, contentLengthIn);
                        incoming.Remove(0, contentLengthIn);
                        SendResponse();
                        endOfHeaders = false;
                    }
                    // recive the essage from header
                    else if (endOfHeaders && (contentLengthIn == 0))
                    {
                        SendResponse();
                        endOfHeaders = false;
                    }
                    socket.BeginReceive(incomingBytes, 0, incomingBytes.Length, SocketFlags.None, MessageReceived, null);
                }
            }

            /// <summary>
            /// find the correct http address and then send the information
            /// </summary>
            private void SendResponse()
            {
                HttpStatusCode status;
                dynamic infoOut = RunServiceMethod(httpRequestData, out status);
                StringContent content = Serialize(infoOut);
                int contentLengthOut = (JsonConvert.SerializeObject(infoOut)).Length;
                // if the address is null, sent to default
                if (JsonConvert.SerializeObject(infoOut) == "null")
                {
                    SendMessage("HTTP/1.1 " + (int)status + " " + status.ToString() + "\r\n" +
                                content.Headers.ToString() +
                                "Content-Length:" + 0 + "\r\n" +
                                "\r\n");
                }
                // else send to set up address
                else
                {
                    SendMessage("HTTP/1.1 " + (int)status + " " + status.ToString() + "\r\n" +
                                content.Headers.ToString() +
                                "Content-Length:" + contentLengthOut + "\r\n" +
                                "\r\n" +
                                JsonConvert.SerializeObject(infoOut));
                }
            }

            /// <summary>
            /// find the matched method need to access
            /// </summary>
            /// <param name="toTest"></param>
            /// <returns></returns>
            private bool ValidServiceMethod(string toTest)
            {
                return toTest == "POST" || toTest == "PUT" || toTest == "GET";
            }

            /// <summary>
            /// find the object that need to argue
            /// </summary>
            /// <param name="toTest"></param>
            /// <returns></returns>
            private bool ValidDirectory(string toTest)
            {
                return toTest == "users" || toTest == "games";
            }

            /// <summary>
            /// send the information in the string format
            /// </summary>
            /// <param name="lines"></param>
            private void SendMessage(string lines)
            {
                lock (sendSync)
                {
                    outgoing.Append(lines);
                    if (!sendIsOngoing)
                    {
                        Console.WriteLine("Appending a " + lines.Length + " char line, starting send mechanism");
                        sendIsOngoing = true;
                        SendBytes();
                    }
                    else
                    {
                        Console.WriteLine("\tAppending a " + lines.Length + " char line, send mechanism already running");
                    }
                }
            }

            /// <summary>
            /// this method will send the bytes into the socket
            /// </summary>
            private void SendBytes()
            {
                if (pendingIndex < pendingBytes.Length)
                {
                    Console.WriteLine("\tSending " + (pendingBytes.Length - pendingIndex) + " bytes");
                    socket.BeginSend(pendingBytes, pendingIndex, pendingBytes.Length - pendingIndex, SocketFlags.None, MessageSent, null);
                }
                else if (outgoing.Length > 0)
                {
                    pendingBytes = encoding.GetBytes(outgoing.ToString());
                    pendingIndex = 0;
                    Console.WriteLine("\tConverting " + outgoing.Length + " chars into " + pendingBytes.Length + " bytes, sending them");
                    outgoing.Clear();
                    socket.BeginSend(pendingBytes, 0, pendingBytes.Length, SocketFlags.None, MessageSent, null);
                }
                else
                {
                    Console.WriteLine("Shutting down send mechanism\n");
                    sendIsOngoing = false;
                }
            }

            /// <summary>
            /// Called when a message has been successfully sent
            /// </summary>
            private void MessageSent(IAsyncResult result)
            {
                // Find out how many bytes were actually sent
                int bytesSent = socket.EndSend(result);
                Console.WriteLine("\t" + bytesSent + " bytes were successfully sent");

                // Get exclusive access to send mechanism
                lock (sendSync)
                {
                    // The socket has been closed
                    if (bytesSent == 0)
                    {
                        socket.Close();
                        Console.WriteLine("Socket closed");
                    }

                    // Update the pendingIndex and keep trying
                    else
                    {
                        pendingIndex += bytesSent;
                        SendBytes();
                    }
                }
            }

            /// <summary>
            /// receive the data from the http which seted by the users
            /// choose the right arguemnet and work on it
            /// </summary>
            /// <param name="httpRequestData"></param>
            /// <param name="status"></param>
            /// <returns></returns>
            private dynamic RunServiceMethod(dynamic httpRequestData, out HttpStatusCode status)
            {
                dynamic infoOut = null;
                status = HttpStatusCode.NotFound;
                // find the right arguement and do it
                switch ((string)httpRequestData.serviceMethod)
                {
                    case "POST":
                        switch ((string)httpRequestData.directory)
                        {
                            case "users":
                                RegisterInfo registerInfoIn = JsonConvert.DeserializeObject<RegisterInfo>(httpRequestData.contentIn);
                                infoOut = Register(registerInfoIn, out status);
                                break;
                            case "games":
                                JoinInfo joinInfoIn = JsonConvert.DeserializeObject<JoinInfo>(httpRequestData.contentIn);
                                infoOut = Join(joinInfoIn, out status);
                                break;
                            default:
                                break;
                        }
                        break;
                    case "PUT":
                        switch ((string)httpRequestData.directory)
                        {
                            case "games":
                                if (Regex.IsMatch((string)httpRequestData.parameters, @"^.+$"))
                                {
                                    PlayInfo playInfoIn = JsonConvert.DeserializeObject<PlayInfo>(httpRequestData.contentIn);
                                    infoOut = PlayWord((string)httpRequestData.parameters, playInfoIn, out status);
                                }
                                else if (Regex.IsMatch((string)httpRequestData.parameters, @"^$"))
                                {
                                    UserInfo userInfoIn = JsonConvert.DeserializeObject<UserInfo>(httpRequestData.contentIn);
                                    CancelJoin(userInfoIn, out status);
                                }
                                break;
                            default:
                                break;
                        }
                        break;
                    case "GET":
                        switch ((string)httpRequestData.directory)
                        {
                            case "games":
                                if(Regex.IsMatch((string)httpRequestData.parameters, @"^.+(\?)(brief|Brief)(=)(yes|no)$"))
                                {
                                    string[] theParameters = httpRequestData.parameters.Split('?');
                                    infoOut = GameStatus(theParameters[0], theParameters[1].Split('=')[1], out status);
                                }
                                else if (Regex.IsMatch((string)httpRequestData.parameters, @"^.+$"))
                                {
                                    infoOut = GameStatus(httpRequestData.parameters, null, out status);
                                }
                                break;
                            default:
                                break;
                        }
                        break;
                    default:
                        break;
                }
                return infoOut;
            }

            /// <summary>
            /// Helper for serializaing JSON.
            /// </summary>
            private static StringContent Serialize(dynamic json)
            {
                return new StringContent(JsonConvert.SerializeObject(json), Encoding.UTF8, "application/json");
            }

            /// <summary>
            /// The most recent call to SetStatus determines the response code used when
            /// an http response is sent.
            /// </summary>
            /// <param name="status"></param>
            private static void SetStatus(HttpStatusCode newStatus, out HttpStatusCode status)
            {
                status = newStatus;
            }

            /// <summary>
            /// Handles cancelling a user's registration for a pending game.
            /// </summary>
            /// <param name="user"></param>
            public void CancelJoin(UserInfo user, out HttpStatusCode status)
            {
                if (users.ContainsKey(user.UserToken))
                {
                    if (InGame(user.UserToken, pendingGames))
                    {
                        Game g;
                        if (pendingGames.TryDequeue(out g))
                        {
                            SetStatus(OK, out status);
                            return;
                        }
                    }
                }
                SetStatus(Forbidden, out status);
                return;
            }

            /// <summary>
            /// Handles processing and returning of game status data.
            /// </summary>
            /// <param name="id">Id of the game to retrieve data for.</param>
            /// <param name="brief">Whether or not the data should be brief.</param>
            /// <returns>Desired status info as available for the game.</returns>
            public StatusInfo GameStatus(string id, string brief, out HttpStatusCode status)
            {
                brief = string.IsNullOrEmpty(brief) ? "no" : brief.ToLower();
                Game g = GetGame(id);
                if (g != null)
                {
                    if (g.GameState == Game.Status.pending)
                    {
                        SetStatus(OK, out status);
                        return new StatusInfo() { GameState = "pending" };
                    }
                    else
                    {
                        SetStatus(OK, out status);
                        CheckGameComplete(g);
                        int timePassed = (int)(DateTime.Now - g.TimeStarted).TotalSeconds;
                        // reset the game info
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
                    SetStatus(Forbidden, out status);
                    return null;
                }
            }

            /// <summary>
            /// if the game is complete, the true will be teturned
            /// </summary>
            /// <param name="g"></param>
            /// <returns></returns>
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

            /// <summary>
            /// get the words that the player has palyed
            /// </summary>
            /// <param name="dic"></param>
            /// <returns></returns>
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

            /// <summary>
            /// get the score that the player should get
            /// </summary>
            /// <param name="dic"></param>
            /// <returns></returns>
            private static int GetScore(IDictionary<string, int> dic)
            {
                int total = 0;
                foreach (int i in dic.Values)
                {
                    total += i;
                }
                return total;
            }

            /// <summary>
            /// get the coresopnding game
            /// </summary>
            /// <param name="id"></param>
            /// <returns></returns>
            private Game GetGame(string id)
            {
                Game g;
                return SearchForGame(id, pendingGames, out g) ? g : activeGames.TryGetValue(id, out g) ? g : completedGames.TryGetValue(id, out g) ? g : null;
            }

            /// <summary>
            /// find the pending game, and join it
            /// </summary>
            /// <param name="id"></param>
            /// <param name="games"></param>
            /// <param name="game"></param>
            /// <returns></returns>
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
            public GameInfo Join(JoinInfo user, out HttpStatusCode status)
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
                                GameInfo info = JoinAsPlayerTwo(user, g, out status);
                                activeGames.TryAdd(g.ID, g);
                                g.Start();
                                return info;
                            }
                            else
                            {
                                return JoinAsPlayerOne(user, g, out status);
                            }
                        }
                        else
                        {
                            g = new Game();
                            g.PlayerOne = user;
                            g.ID = activeGames.Count + completedGames.Count + pendingGames.Count + 1 + "";
                            g.GameState = Game.Status.pending;
                            pendingGames.Enqueue(g);
                            SetStatus(Accepted, out status);
                            return new GameInfo() { GameID = g.ID };
                        }
                    }
                    SetStatus(Conflict, out status);
                    return null;
                }
                SetStatus(Forbidden, out status);
                return null;
            }

            /// <summary>
            /// when the two players are palying the game
            /// </summary>
            /// <param name="user"></param>
            /// <param name="g"></param>
            /// <param name="status"></param>
            /// <returns></returns>
            private GameInfo JoinAsPlayerTwo(JoinInfo user, Game g, out HttpStatusCode status)
            {
                g.PlayerTwo = user;
                g.TimeLimit = (g.PlayerOne.TimeLimit + user.TimeLimit) / 2;
                g.Board = new BoggleBoard();
                g.GameState = Game.Status.active;
                SetStatus(Created, out status);
                return new GameInfo() { GameID = g.ID };
            }

            /// <summary>
            /// whne just one player here, wait for another palyer
            /// </summary>
            /// <param name="user"></param>
            /// <param name="g"></param>
            /// <param name="status"></param>
            /// <returns></returns>
            private GameInfo JoinAsPlayerOne(JoinInfo user, Game g, out HttpStatusCode status)
            {
                g.PlayerOne = user;
                g.ID = activeGames.Count + completedGames.Count + pendingGames.Count + 1 + "";
                g.GameState = Game.Status.pending;
                SetStatus(Accepted, out status);
                return new GameInfo() { GameID = g.ID };
            }

            /// <summary>
            /// This class declear the game satatus which is in the game
            /// </summary>
            /// <param name="userToken"></param>
            /// <param name="pending"></param>
            /// <returns></returns>
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

            /// <summary>
            /// return ture when the game is going on
            /// </summary>
            /// <param name="userToken"></param>
            /// <param name="gameBag"></param>
            /// <returns></returns>
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
            public ScoreInfo PlayWord(string id, PlayInfo play, out HttpStatusCode status)
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
                                    SetStatus(Forbidden, out status);
                                    return null;
                                }
                                SetStatus(OK, out status);
                                return new ScoreInfo() { Score = score };
                            }
                        }
                    }

                    SetStatus(Conflict, out status);
                    return null;
                }
                SetStatus(Forbidden, out status);
                return null;
            }

            /// <summary>
            /// return the coresponding word scores
            /// </summary>
            /// <param name="word"></param>
            /// <returns></returns>
            private int WordScore(string word)
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
            public UserInfo Register(RegisterInfo user, out HttpStatusCode status)
            {
                if (user.Nickname == null || user.Nickname.Trim().Length == 0)
                {
                    SetStatus(Forbidden, out status);
                    return null;
                }
                UserInfo t = new UserInfo();
                t.UserToken = Guid.NewGuid().ToString();
                users.TryAdd(t.UserToken, user.Nickname);
                SetStatus(Created, out status);
                return t;
            }
        }
    }
}
