using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.ServiceModel;
using System.ServiceModel.Web;

namespace Boggle
{
    [ServiceContract]
    public interface IBoggleService
    {
        /// <summary>
        /// Sends back index.html as the response body.
        /// </summary>
        [WebGet(UriTemplate = "/api")]
        Stream API();

        [WebInvoke(Method = "POST", UriTemplate = "/users")]
        UserInfo Register(RegisterInfo user);

        [WebInvoke(Method = "POST", UriTemplate = "/games")]
        GameInfo Join(JoinInfo user);

        [WebInvoke(Method = "PUT", UriTemplate = "/games")]
        void CancelJoin(UserInfo user);

        [WebInvoke(Method = "PUT", UriTemplate = "/games/{id}")]
        ScoreInfo PlayWord(string id, PlayInfo play);

        [WebGet(UriTemplate = "/games/{id}?Brief={brief}")]
        StatusInfo GameStatus(string id, string brief);
    }
}
