using System;
using System.Collections.Generic;
using System.IO;
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
        /// A logged in user. Contains his id and the time of login and the time of last activity.
        /// </summary>
        private class LoggedInUser
        {
            public int userId;
            public DateTime timeOfLogin;
            public DateTime timeOfLastActivity;

            public LoggedInUser(int userId, DateTime timeOfLogin)
            {
                this.userId = userId;
                this.timeOfLogin = timeOfLogin;
                this.timeOfLastActivity = timeOfLogin;
            }
        }

        // Constants
        private readonly TimeSpan SESSION_TIMEOUT = new TimeSpan(0, 20, 0); // 20 minutes
        private readonly TimeSpan DEFAULT_SESSION_LENGTH = new TimeSpan(0, 20, 0); // 20 minutes
        private const double SESSION_LENGTH_EXP_DECAY_VALUE = 0.8;

        // The proxy
        private RCLocalProxy _proxy;

        // The path to users.xml
        private string _usersXML;
        
        // All users currently logged in.
        private Dictionary<IPAddress, LoggedInUser> _usersLoggedIn = new Dictionary<IPAddress,LoggedInUser>();

        // The avg. session length of each user. Not actually the avg., but using exponential decay
        private Dictionary<int, TimeSpan> _avgSessionLengths = new Dictionary<int, TimeSpan>();

        /// <summary>
        /// A new SessionManager.
        /// </summary>
        /// <param name="proxy">The proxy.</param>
        public SessionManager(RCLocalProxy proxy)
        {
            this._proxy = proxy;
            this._usersXML = proxy.ProxyPath + "users.xml";
        }

        #region session length stuff

        /// <summary>
        /// Gets the average session length of a user, or the default if this is the user's first session.
        /// </summary>
        /// <param name="userId">The user id.</param>
        /// <returns>The user's avg. session length.</returns>
        public TimeSpan GetAvgSessionLength(int userId)
        {
            return _avgSessionLengths.ContainsKey(userId) ? _avgSessionLengths[userId] : DEFAULT_SESSION_LENGTH;
        }

        /// <summary>
        /// Includes a session length in the user's average.
        /// </summary>
        /// <param name="userId">The user id.</param>
        /// <param name="sessionLength">The session length.</param>
        private void IncludeSessionLengthInAvg(int userId, TimeSpan sessionLength)
        {
            if (_avgSessionLengths.ContainsKey(userId))
            {
                long ticksAvg = (long)(_avgSessionLengths[userId].Ticks * SESSION_LENGTH_EXP_DECAY_VALUE
                    + sessionLength.Ticks * (1 - SESSION_LENGTH_EXP_DECAY_VALUE));
                _avgSessionLengths[userId] = new TimeSpan(ticksAvg);
            }
            else
            {
                _avgSessionLengths[userId] = sessionLength;
            }
        }

        #endregion
        #region IP adress stuff

        /// <summary>
        /// Logs a user in. Saves his userId so that it can be retrieved later by the IP.
        /// </summary>
        /// <param name="ip">The IP address of the client.</param>
        /// <param name="userId">The user id.</param>
        public void LogUserIn(IPAddress ip, int userId)
        {
            lock (_usersLoggedIn)
            {
                // Log out user (may be logged in here or elsewhere)
                LogUserOutNoLock(userId);

                if (_usersLoggedIn.ContainsKey(ip))
                {
                    // Log out old user using that machine
                    LogUserOutNoLock(_usersLoggedIn[ip].userId);
                }
                _usersLoggedIn[ip] = new LoggedInUser(userId, DateTime.Now);
            }
        }

        private void LogUserOutNoLock(int userId)
        {
            // Remove all IPs that map to the userId (normally one)
            KeyValuePair<IPAddress,LoggedInUser> ipAndUser;
            while ((ipAndUser = _usersLoggedIn.FirstOrDefault(x => x.Value.userId == userId)).Key != null)
            {
                // Remove from active sessions
                _usersLoggedIn.Remove(ipAndUser.Key);
                // Save session length
                IncludeSessionLengthInAvg(ipAndUser.Value.userId, DateTime.Now.Subtract(ipAndUser.Value.timeOfLogin));
            }
        }

        /// <summary>
        /// Logs a user out.
        /// </summary>
        /// <param name="userId">The user id.</param>
        public void LogUserOut(int userId)
        {
            lock (_usersLoggedIn)
            {
                LogUserOutNoLock(userId);
            }
        }

        /// <summary>
        /// Updates the time of last activity to now.
        /// </summary>
        /// <param name="ip">The client IP.</param>
        public void UpdateLastActivityTime(IPAddress ip)
        {
            lock (_usersLoggedIn)
            {
                LoggedInUser user;
                if (_usersLoggedIn.TryGetValue(ip, out user))
                {
                    user.timeOfLastActivity = DateTime.Now;
                }
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
            if(_usersLoggedIn.TryGetValue(ip, out user))
            {
                // Check if the user session is expired!
                if (user.timeOfLastActivity.Add(SESSION_TIMEOUT).CompareTo(DateTime.Now) < 0)
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
            LoggedInUser user = _usersLoggedIn.FirstOrDefault(x => x.Value.userId == userId).Value;
            if (user == null)
            {
                throw new Exception("User not logged in.");
            }
            return user.timeOfLastActivity.Add(SESSION_TIMEOUT);
        }

        /// <summary>
        /// Gets the DateTime the user logged in.
        /// </summary>
        /// <param name="userId">The user id.</param>
        /// <returns>The time of login.</returns>
        public DateTime GetTimeOfLogin(int userId)
        {
            LoggedInUser user = _usersLoggedIn.FirstOrDefault(x => x.Value.userId == userId).Value;
            if (user == null)
            {
                throw new Exception("User not logged in.");
            }
            return user.timeOfLogin;
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
            if (!File.Exists(_usersXML))
            {
                return -1;
            }
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
            if (!File.Exists(_usersXML))
            {
                return false;
            }
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
            if (File.Exists(_usersXML))
            {
                doc.Load(_usersXML);
            }
            else
            {
                doc.AppendChild(doc.CreateXmlDeclaration("1.0", "UTF-8", String.Empty));
                doc.AppendChild(doc.CreateElement("customers"));
            }

            XmlNode custsNode = doc.DocumentElement;
            // Get new id
            int custid = custsNode.ChildNodes.Count + 1; // IDs start with 1
            // Append zeros
            String custidStr = custid.ToString("D3");

            // Add new user
            XmlElement customerNode = doc.CreateElement("customer");
            customerNode.SetAttribute("custid", custidStr);
            custsNode.AppendChild(customerNode);

            XmlElement userNode = doc.CreateElement("user");
            XmlElement pwdNode = doc.CreateElement("pwd");
            userNode.InnerText = username;
            pwdNode.InnerText = password;
            customerNode.AppendChild(userNode);
            customerNode.AppendChild(pwdNode);
            

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
            if (!File.Exists(_usersXML))
            {
                return 0;
            }
            XmlDocument doc = new XmlDocument();
            doc.Load(_usersXML);
            return doc.DocumentElement.ChildNodes.Count;
        }

        #endregion
    }
}
