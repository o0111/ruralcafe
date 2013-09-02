using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Xml;

namespace RuralCafe
{
    /// <summary>
    /// Manages sessions of the user. Also does everything connected to the users.xml.
    /// </summary>
    public class SessionManager
    {
        /// <summary>
        /// A logged in user. Contains his id and the time of login.
        /// </summary>
        private class LoggedInUser
        {
            public int userId;
            public DateTime timeOfLastActivity;

            public LoggedInUser(int userId, DateTime timeOfLastActivity)
            {
                this.userId = userId;
                this.timeOfLastActivity = timeOfLastActivity;
            }
        }

        // Constants
        private const long SESSION_TIMEOUT_S = 60 * 60 * 24; // 24 hours

        // The proxy
        private RCLocalProxy _proxy;

        private string _usersXML;
        
        // All users currently logged in.
        private Dictionary<IPAddress, LoggedInUser> usersLoggedIn = new Dictionary<IPAddress,LoggedInUser>();

        /// <summary>
        /// A new SessionManager.
        /// </summary>
        /// <param name="proxy">The proxy.</param>
        public SessionManager(RCLocalProxy proxy)
        {
            this._proxy = proxy;
            this._usersXML = proxy.UIPagesPath + "users.xml"; // FIXME
        }

        #region IP adress stuff

        /// <summary>
        /// Logs a user in. Saves his userId so that it can be retrieved later by the IP.
        /// </summary>
        /// <param name="ip">The IP address of the client.</param>
        /// <param name="userId">The user id.</param>
        public void LogUserIn(IPAddress ip, int userId)
        {
            lock (usersLoggedIn)
            {
                // Log out user (may be loggin in here or elsewhere
                LogUserOutNoLock(userId);

                if (usersLoggedIn.ContainsKey(ip))
                {
                    // Log out old user using that machine
                    LogUserOutNoLock(usersLoggedIn[ip].userId);
                }
                usersLoggedIn[ip] = new LoggedInUser(userId, DateTime.Now);
            }
        }

        private void LogUserOutNoLock(int userId)
        {
            // Remove all IPs that map to the userId (normally one)
            IPAddress ip;
            while ((ip = usersLoggedIn.FirstOrDefault(x => x.Value.userId == userId).Key) != null)
            {
                usersLoggedIn.Remove(ip);
            }
        }

        /// <summary>
        /// Logs a user out.
        /// </summary>
        /// <param name="userId">The user id.</param>
        public void LogUserOut(int userId)
        {
            lock (usersLoggedIn)
            {
                LogUserOutNoLock(userId);
            }
        }

        /// <summary>
        /// Gets the user id of a logged in user by its IP or -1.
        /// </summary>
        /// <param name="ip">The IP address of the client.</param>
        /// <returns>The user id or -1.</returns>
        public int GetUserId(IPAddress ip)
        {
            LoggedInUser user;
            if(usersLoggedIn.TryGetValue(ip, out user))
            {
                // Check if the user session is expired!
                if (user.timeOfLastActivity.AddSeconds(SESSION_TIMEOUT_S).CompareTo(DateTime.Now) < 0)
                {
                    // Expired. Log the user out and return -1
                    LogUserOut(user.userId);
                    return -1;
                }

                return user.userId;
            }
            return -1;
        }

        /// <summary>
        /// Gets the DateTime at which the user's session would expire at the moment.
        /// </summary>
        /// <param name="userId">The user id.</param>
        /// <returns>The session expiry DateTime.</returns>
        public DateTime GetSessionExpiryDate(int userId)
        {
            LoggedInUser user = usersLoggedIn.FirstOrDefault(x => x.Value.userId == userId).Value;
            if (user == null)
            {
                throw new Exception("User not logged in.");
            }
            return user.timeOfLastActivity.AddSeconds(SESSION_TIMEOUT_S);
        }

        #endregion
        #region users.xml stuff

        /// <summary>
        /// Get the user ID that belongs to a username.
        /// </summary>
        /// <param name="username">The username.</param>
        /// <returns>His user ID or -1.</returns>
        public int UserID(string username)
        {
            XmlDocument doc = new XmlDocument();
            doc.Load(_usersXML);

            XmlNode userNode = doc.SelectSingleNode(String.Format("//user[text() = '{0}']", username));
            if (userNode == null)
            {
                return -1;
            }

            return Int32.Parse(userNode.ParentNode.Attributes["custid"].Value);
        }

        /// <summary>
        /// Checks if this username/password combination is correct.
        /// </summary>
        /// <param name="username">The username.</param>
        /// <param name="password">The password.</param>
        /// <returns>If the combination is correct.</returns>
        public bool IsCorrectPW(string username, string password)
        {
            XmlDocument doc = new XmlDocument();
            doc.Load(_usersXML);

            XmlNode userNode = doc.SelectSingleNode(String.Format("//user[text() = '{0}']", username));
            if (userNode == null)
            {
                return false;
            }

            XmlNode pwdNode = userNode.NextSibling;
            return pwdNode.InnerText.Equals(password);
        }

        /// <summary>
        /// Adds a new user to the users.xml.
        /// </summary>
        /// <param name="username">The username.</param>
        /// <param name="password">The password.</param>
        /// <returns>The user ID of the new user.</returns>
        public int SignUpUser(string username, string password)
        {
            XmlDocument doc = new XmlDocument();
            doc.Load(_usersXML);

            XmlNode custsNode = doc.DocumentElement;
            // Get new id
            int custid = custsNode.ChildNodes.Count + 1; // IDs start with 1
            // Append zeros
            String custidStr = custid.ToString("D3");
            // Add new user
            custsNode.AppendChild(custsNode.LastChild.CloneNode(true));
            custsNode.LastChild.Attributes["custid"].Value = custidStr;
            custsNode.LastChild.SelectSingleNode("user").InnerText = username;
            custsNode.LastChild.SelectSingleNode("pwd").InnerText = password;
            //Save
            doc.Save(_usersXML);

            return custid;
        }

        /// <summary>
        /// Gets the nunber of registered users.
        /// </summary>
        /// <returns>The number of users.</returns>
        public int UsersNumber()
        {
            XmlDocument doc = new XmlDocument();
            doc.Load(_usersXML);
            return doc.DocumentElement.ChildNodes.Count;
        }

        #endregion
    }
}
