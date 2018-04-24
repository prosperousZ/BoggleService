using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static System.Net.HttpStatusCode;
using System.Diagnostics;
using Newtonsoft.Json;
using System.Dynamic;

namespace Boggle
{
    /// <summary>
    /// Provides a way to start and stop the IIS web server from within the test
    /// cases.  If something prevents the test cases from stopping the web server,
    /// subsequent tests may not work properly until the stray process is killed
    /// manually.
    /// </summary>
    public static class IISAgent
    {
        // Reference to the running process
        private static Process process = null;

        /// <summary>
        /// Starts IIS
        /// </summary>
        public static void Start(string arguments)
        {
            if (process == null)
            {
                ProcessStartInfo info = new ProcessStartInfo(Properties.Resources.IIS_EXECUTABLE, arguments);
                info.WindowStyle = ProcessWindowStyle.Minimized;
                info.UseShellExecute = false;
                process = Process.Start(info);
            }
        }

        /// <summary>
        ///  Stops IIS
        /// </summary>
        public static void Stop()
        {
            if (process != null)
            {
                process.Kill();
            }
        }
    }
    [TestClass]
    public class BoggleTests
    {
        /// <summary>
        /// This is automatically run prior to all the tests to start the server
        /// </summary>
        [ClassInitialize()]
        public static void StartIIS(TestContext context)
        {
            IISAgent.Start(@"/site:""BoggleService"" /apppool:""Clr4IntegratedAppPool"" /config:""..\..\..\.vs\config\applicationhost.config""");
        }

        /// <summary>
        /// This is automatically run when all tests have completed to stop the server
        /// </summary>
        [ClassCleanup()]
        public static void StopIIS()
        {
            IISAgent.Stop();
        }

        private RestTestClient client = new RestTestClient("http://localhost:60000/BoggleService.svc/");

        [TestMethod]
        public void CreateUserNicknameNull()
        {
            dynamic data = new ExpandoObject();
            data.Nickname = null;
            Response r = client.DoPostAsync("users", data).Result;
            Assert.AreEqual(Forbidden, r.Status);
        }

        [TestMethod]
        public void CreateUserNicknameEmpty()
        {
            dynamic data = new ExpandoObject();
            data.Nickname = "      ";
            Response r = client.DoPostAsync("users", data).Result;
            Assert.AreEqual(Forbidden, r.Status);
        }

        [TestMethod]
        public void CreateUserNicknameValid()
        {
            dynamic data = new ExpandoObject();
            data.Nickname = "Valid_Name";
            Response r = client.DoPostAsync("users", data).Result;
            Assert.AreEqual(Created, r.Status);
        }

        [TestMethod]
        public void JoinGameUserTokenInvalid()
        {
            dynamic data = new ExpandoObject();
            data.UserToken = "";
            data.TimeLimit = 60;
            Response r = client.DoPostAsync("games", data).Result;
            Assert.AreEqual(Forbidden, r.Status);
        }

        [TestMethod]
        public void JoinGameTimeLimitTooSmall()
        {
            dynamic data = new ExpandoObject();
            data.Nickname = "Valid_Name";
            Response r = client.DoPostAsync("users", data).Result;
            string userToken = r.Data.UserToken.ToString();
            Assert.AreEqual(Created, r.Status);
            
            data.UserToken = userToken;
            data.TimeLimit = 4;
            r = client.DoPostAsync("games", data).Result;
            Assert.AreEqual(Forbidden, r.Status);
        }

        [TestMethod]
        public void JoinGameTimeLimitTooLarge()
        {
            dynamic data = new ExpandoObject();
            data.Nickname = "Valid_Name";
            Response r = client.DoPostAsync("users", data).Result;
            string userToken = r.Data.UserToken.ToString();
            Assert.AreEqual(Created, r.Status);
            
            data.UserToken = userToken;
            data.TimeLimit = 121;
            r = client.DoPostAsync("games", data).Result;
            Assert.AreEqual(Forbidden, r.Status);
        }

        [TestMethod]
        public void JoinGameUserTokenInPendingGame()
        {
            dynamic data = new ExpandoObject();
            data.Nickname = "Valid_Name";
            Response r = client.DoPostAsync("users", data).Result;
            string userToken = r.Data.UserToken.ToString();
            Assert.AreEqual(Created, r.Status);
            
            data.UserToken = userToken;
            data.TimeLimit = 60;
            r = client.DoPostAsync("games", data).Result;
            Assert.AreEqual(Accepted, r.Status);

            r = client.DoPostAsync("games", data).Result;
            Assert.AreEqual(Conflict, r.Status);

            r = client.DoPostAsync("users", data).Result;
            userToken = r.Data.UserToken.ToString();
            Assert.AreEqual(Created, r.Status);

            data.UserToken = userToken;
            data.TimeLimit = 60;
            r = client.DoPostAsync("games", data).Result;
            Assert.AreEqual(Created, r.Status);
        }

        [TestMethod]
        public void JoinGameSucessful()
        {
            dynamic data = new ExpandoObject();
            data.Nickname = "Valid_Name";
            Response r = client.DoPostAsync("users", data).Result;
            string userToken = r.Data.UserToken.ToString();
            Assert.AreEqual(Created, r.Status);
            
            data.UserToken = userToken;
            data.TimeLimit = 60;
            r = client.DoPostAsync("games", data).Result;
            Assert.AreEqual(Accepted, r.Status);

            r = client.DoPostAsync("users", data).Result;
            userToken = r.Data.UserToken.ToString();
            Assert.AreEqual(Created, r.Status);

            data.UserToken = userToken;
            data.TimeLimit = 60;
            r = client.DoPostAsync("games", data).Result;
            Assert.AreEqual(Created, r.Status);
        }

        [TestMethod]
        public void CancelJoinUserTokenInvalid()
        {
            dynamic data = new ExpandoObject();
            data.UserToken = "";
            Response r = client.DoPutAsync(data, "games").Result;
            Assert.AreEqual(Forbidden, r.Status);
        }

        [TestMethod]
        public void CancelJoinUserTokenNotInPendingGame()
        {
            dynamic data = new ExpandoObject();
            data.Nickname = "Valid_Name";
            Response r = client.DoPostAsync("users", data).Result;
            string userToken = r.Data.UserToken.ToString();
            Assert.AreEqual(Created, r.Status);

            data.UserToken = userToken;
            data.TimeLimit = 60;
            r = client.DoPostAsync("games", data).Result;
            Assert.AreEqual(Accepted, r.Status);

            r = client.DoPostAsync("users", data).Result;
            userToken = r.Data.UserToken.ToString();
            Assert.AreEqual(Created, r.Status);

            data.UserToken = userToken;
            r = client.DoPutAsync(data, "games").Result;
            Assert.AreEqual(Forbidden, r.Status);

            r = client.DoPostAsync("users", data).Result;
            userToken = r.Data.UserToken.ToString();
            Assert.AreEqual(Created, r.Status);

            data.UserToken = userToken;
            data.TimeLimit = 60;
            r = client.DoPostAsync("games", data).Result;
            Assert.AreEqual(Created, r.Status);
        }

        [TestMethod]
        public void CancelJoinSuccessful()
        {
            dynamic data = new ExpandoObject();
            data.Nickname = "Valid_Name";
            Response r = client.DoPostAsync("users", data).Result;
            string userToken = r.Data.UserToken.ToString();
            Assert.AreEqual(Created, r.Status);

            data.UserToken = userToken;
            data.TimeLimit = 60;
            r = client.DoPostAsync("games", data).Result;
            Assert.AreEqual(Accepted, r.Status);

            r = client.DoPutAsync(data, "games").Result;
            Assert.AreEqual(OK, r.Status);
        }

        [TestMethod]
        public void PlayWordNull()
        {
            dynamic data = new ExpandoObject();
            data.Nickname = "Valid_Name";
            Response r = client.DoPostAsync("users", data).Result;
            string userToken = r.Data.UserToken.ToString();
            Assert.AreEqual(Created, r.Status);

            data.UserToken = userToken;
            data.TimeLimit = 60;
            r = client.DoPostAsync("games", data).Result;
            Assert.AreEqual(Accepted, r.Status);

            r = client.DoPostAsync("users", data).Result;
            userToken = r.Data.UserToken.ToString();
            Assert.AreEqual(Created, r.Status);

            data.UserToken = userToken;
            data.TimeLimit = 60;
            r = client.DoPostAsync("games", data).Result;
            Assert.AreEqual(Created, r.Status);
            string gameID = r.Data.GameID.ToString();

            data.Word = null;
            r = client.DoPutAsync(data, "games/" + gameID).Result;
            //Assert.AreEqual(Forbidden, r.Status);
        }

        [TestMethod]
        public void PlayWordEmpty()
        {
            dynamic data = new ExpandoObject();
            data.Nickname = "Valid_Name";
            Response r = client.DoPostAsync("users", data).Result;
            string userToken = r.Data.UserToken.ToString();
            Assert.AreEqual(Created, r.Status);

            data.UserToken = userToken;
            data.TimeLimit = 60;
            r = client.DoPostAsync("games", data).Result;
            Assert.AreEqual(Accepted, r.Status);

            r = client.DoPostAsync("users", data).Result;
            userToken = r.Data.UserToken.ToString();
            Assert.AreEqual(Created, r.Status);

            data.UserToken = userToken;
            data.TimeLimit = 60;
            r = client.DoPostAsync("games", data).Result;
            Assert.AreEqual(Created, r.Status);
            string gameID = r.Data.GameID.ToString();

            data.Word = "";
            r = client.DoPutAsync(data, "games/" + gameID).Result;
            Assert.AreEqual(Forbidden, r.Status);
        }

        [TestMethod]
        public void PlayWordGameIDMissing()
        {
            dynamic data = new ExpandoObject();
            data.Nickname = "Valid_Name";
            Response r = client.DoPostAsync("users", data).Result;
            string userToken = r.Data.UserToken.ToString();
            Assert.AreEqual(Created, r.Status);

            data.UserToken = userToken;
            data.TimeLimit = 60;
            r = client.DoPostAsync("games", data).Result;
            Assert.AreEqual(Accepted, r.Status);

            r = client.DoPostAsync("users", data).Result;
            userToken = r.Data.UserToken.ToString();
            Assert.AreEqual(Created, r.Status);

            data.UserToken = userToken;
            data.TimeLimit = 60;
            r = client.DoPostAsync("games", data).Result;
            Assert.AreEqual(Created, r.Status);

            data.Word = "word";
            r = client.DoPutAsync(data, "games" + "").Result;
            Assert.AreEqual(Forbidden, r.Status);
        }

        [TestMethod]
        public void PlayWordUserTokenMissing()
        {
            dynamic data = new ExpandoObject();
            data.Nickname = "Valid_Name";
            Response r = client.DoPostAsync("users", data).Result;
            string userToken = r.Data.UserToken.ToString();
            Assert.AreEqual(Created, r.Status);

            data.UserToken = userToken;
            data.TimeLimit = 60;
            r = client.DoPostAsync("games", data).Result;
            Assert.AreEqual(Accepted, r.Status);

            r = client.DoPostAsync("users", data).Result;
            userToken = r.Data.UserToken.ToString();
            Assert.AreEqual(Created, r.Status);

            data.UserToken = userToken;
            data.TimeLimit = 60;
            r = client.DoPostAsync("games", data).Result;
            Assert.AreEqual(Created, r.Status);
            string gameID = r.Data.GameID.ToString();

            data.UserToken = "";
            data.Word = "word";
            r = client.DoPutAsync(data, "games/" + gameID).Result;
            Assert.AreEqual(Forbidden, r.Status);
        }

        [TestMethod]
        public void PlayWordGameIDInvalid()
        {
            dynamic data = new ExpandoObject();
            data.Nickname = "Valid_Name";
            Response r = client.DoPostAsync("users", data).Result;
            string userToken = r.Data.UserToken.ToString();
            Assert.AreEqual(Created, r.Status);

            data.UserToken = userToken;
            data.TimeLimit = 60;
            r = client.DoPostAsync("games", data).Result;
            Assert.AreEqual(Accepted, r.Status);

            r = client.DoPostAsync("users", data).Result;
            userToken = r.Data.UserToken.ToString();
            Assert.AreEqual(Created, r.Status);

            data.UserToken = userToken;
            data.TimeLimit = 60;
            r = client.DoPostAsync("games", data).Result;
            Assert.AreEqual(Created, r.Status);

            data.Word = "word";
            r = client.DoPutAsync(data, "games/" + "14678").Result;
            Assert.AreEqual(Conflict, r.Status);
        }

        [TestMethod]
        public void PlayWordUserTokenInvalid()
        {
            dynamic data = new ExpandoObject();
            data.Nickname = "Valid_Name";
            Response r = client.DoPostAsync("users", data).Result;
            string userToken = r.Data.UserToken.ToString();
            Assert.AreEqual(Created, r.Status);

            data.UserToken = userToken;
            data.TimeLimit = 60;
            r = client.DoPostAsync("games", data).Result;
            Assert.AreEqual(Accepted, r.Status);

            r = client.DoPostAsync("users", data).Result;
            userToken = r.Data.UserToken.ToString();
            Assert.AreEqual(Created, r.Status);

            data.UserToken = userToken;
            data.TimeLimit = 60;
            r = client.DoPostAsync("games", data).Result;
            Assert.AreEqual(Created, r.Status);
            string gameID = r.Data.GameID.ToString();

            data.UserToken = "";
            data.Word = "word";
            r = client.DoPutAsync(data, "games/" + gameID).Result;
            Assert.AreEqual(Forbidden, r.Status);
        }

        [TestMethod]
        public void PlayWordUserTokenIncorrect()
        {
            dynamic data = new ExpandoObject();
            data.Nickname = "Valid_Name";
            Response r = client.DoPostAsync("users", data).Result;
            string userToken = r.Data.UserToken.ToString();
            Assert.AreEqual(Created, r.Status);

            data.UserToken = userToken;
            data.TimeLimit = 60;
            r = client.DoPostAsync("games", data).Result;
            Assert.AreEqual(Accepted, r.Status);

            r = client.DoPostAsync("users", data).Result;
            userToken = r.Data.UserToken.ToString();
            Assert.AreEqual(Created, r.Status);

            data.UserToken = userToken;
            data.TimeLimit = 60;
            r = client.DoPostAsync("games", data).Result;
            Assert.AreEqual(Created, r.Status);
            string gameID = r.Data.GameID.ToString();

            data.UserToken = "THIS_IS_A_FAKE";
            data.Word = "word";
            r = client.DoPutAsync(data, "games/" + gameID).Result;
            Assert.AreEqual(Forbidden, r.Status);
        }

        [TestMethod]
        public void PlayWordGameStateNotActive()
        {
            dynamic data = new ExpandoObject();
            data.Nickname = "Valid_Name";
            Response r = client.DoPostAsync("users", data).Result;
            string userToken = r.Data.UserToken.ToString();
            Assert.AreEqual(Created, r.Status);

            data.UserToken = userToken;
            data.TimeLimit = 60;
            r = client.DoPostAsync("games", data).Result;
            Assert.AreEqual(Accepted, r.Status);
            string gameID = r.Data.GameID.ToString();

            data.Word = "word";
            r = client.DoPutAsync(data, "games/" + gameID).Result;
            Assert.AreEqual(Conflict, r.Status);

            r = client.DoPostAsync("users", data).Result;
            userToken = r.Data.UserToken.ToString();
            Assert.AreEqual(Created, r.Status);

            data.UserToken = userToken;
            data.TimeLimit = 60;
            r = client.DoPostAsync("games", data).Result;
            Assert.AreEqual(Created, r.Status);
        }

        [TestMethod]
        public void PlayWordSuccessful()
        {
            dynamic data = new ExpandoObject();
            data.Nickname = "Valid_Name";
            Response r = client.DoPostAsync("users", data).Result;
            string userToken = r.Data.UserToken.ToString();
            Assert.AreEqual(Created, r.Status);

            data.UserToken = userToken;
            data.TimeLimit = 60;
            r = client.DoPostAsync("games", data).Result;
            Assert.AreEqual(Accepted, r.Status);

            r = client.DoPostAsync("users", data).Result;
            userToken = r.Data.UserToken.ToString();
            Assert.AreEqual(Created, r.Status);

            data.UserToken = userToken;
            data.TimeLimit = 60;
            r = client.DoPostAsync("games", data).Result;
            Assert.AreEqual(Created, r.Status);
            string gameID = r.Data.GameID.ToString();

            data.Word = "word";
            r = client.DoPutAsync(data, "games/" + gameID).Result;
            Assert.AreEqual(OK, r.Status);
        }

        [TestMethod]
        public void GameStatusGameIDInvalid()
        {
            Response r = client.DoGetAsync("games/" + "13781").Result;
            Assert.AreEqual(Forbidden, r.Status);
        }

        [TestMethod]
        public void GameStatusSuccessfulWithBriefYes()
        {
            dynamic data = new ExpandoObject();
            data.Nickname = "Valid_Name";
            Response r = client.DoPostAsync("users", data).Result;
            string userToken = r.Data.UserToken.ToString();
            Assert.AreEqual(Created, r.Status);

            data.UserToken = userToken;
            data.TimeLimit = 60;
            r = client.DoPostAsync("games", data).Result;
            Assert.AreEqual(Accepted, r.Status);

            r = client.DoPostAsync("users", data).Result;
            userToken = r.Data.UserToken.ToString();
            Assert.AreEqual(Created, r.Status);

            data.UserToken = userToken;
            data.TimeLimit = 60;
            r = client.DoPostAsync("games", data).Result;
            Assert.AreEqual(Created, r.Status);
            string gameID = r.Data.GameID.ToString();

            r = client.DoGetAsync("games/" + gameID + "?Brief={0}", "Yes").Result;
            Assert.AreEqual(OK, r.Status);
        }

        [TestMethod]
        public void GameStatusSuccessfulWithBriefNo()
        {
            dynamic data = new ExpandoObject();
            data.Nickname = "Valid_Name";
            Response r = client.DoPostAsync("users", data).Result;
            string userToken = r.Data.UserToken.ToString();
            Assert.AreEqual(Created, r.Status);

            data.UserToken = userToken;
            data.TimeLimit = 60;
            r = client.DoPostAsync("games", data).Result;
            Assert.AreEqual(Accepted, r.Status);

            r = client.DoPostAsync("users", data).Result;
            userToken = r.Data.UserToken.ToString();
            Assert.AreEqual(Created, r.Status);

            data.UserToken = userToken;
            data.TimeLimit = 60;
            r = client.DoPostAsync("games", data).Result;
            Assert.AreEqual(Created, r.Status);
            string gameID = r.Data.GameID.ToString();

            r = client.DoGetAsync("games/" + gameID + "?Brief={0}", "No").Result;
            Assert.AreEqual(OK, r.Status);
        }

        [TestMethod]
        public void GameStatusSuccessfulWithOutBrief()
        {
            dynamic data = new ExpandoObject();
            data.Nickname = "Valid_Name";
            Response r = client.DoPostAsync("users", data).Result;
            string userToken = r.Data.UserToken.ToString();
            Assert.AreEqual(Created, r.Status);

            data.UserToken = userToken;
            data.TimeLimit = 60;
            r = client.DoPostAsync("games", data).Result;
            Assert.AreEqual(Accepted, r.Status);

            r = client.DoPostAsync("users", data).Result;
            userToken = r.Data.UserToken.ToString();
            Assert.AreEqual(Created, r.Status);

            data.UserToken = userToken;
            data.TimeLimit = 60;
            r = client.DoPostAsync("games", data).Result;
            Assert.AreEqual(Created, r.Status);
            string gameID = r.Data.GameID.ToString();

            r = client.DoGetAsync("games/" + gameID).Result;
            Assert.AreEqual(OK, r.Status);
        }
    }
}
