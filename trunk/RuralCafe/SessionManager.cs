using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;

namespace RuralCafe
{
    /// <summary>
    /// Manages sessions of the user.
    /// 
    /// Currently just manages a dictionary of IPs mapped to the logged in users.
    /// Should later also do the actual Login/Logout stuff, which is currently done at the
    /// client side in JavaScript.
    /// </summary>
    public class SessionManager
    {
        /// <summary>
        /// A logged in user. Contains his id and the time of login.
        /// </summary>
        private class LoggedInUser
        {
            public int userId;
            public DateTime timeOfLogin;

            public LoggedInUser(int userId, DateTime timeOfLogin)
            {
                this.userId = userId;
                this.timeOfLogin = timeOfLogin;
            }
        }

        // Constants
        private const long SESSION_TIMEOUT_S = 60 * 60 * 24; // 24 hours
        
        // TODO auto remove aufter timeout
        // All users currently logged in.
        private Dictionary<IPAddress, LoggedInUser> usersLoggedIn = new Dictionary<IPAddress,LoggedInUser>();

        /// <summary>
        /// Logs a user in. Saves his userId so that it can be retrieved later by the IP.
        /// </summary>
        /// <param name="ip">The IP address of the client.</param>
        /// <param name="userId">The user id.</param>
        public void LogUserIn(IPAddress ip, int userId)
        {
            lock (usersLoggedIn)
            {
                if (usersLoggedIn.ContainsKey(ip))
                {
                    // Just update userId and time.
                    usersLoggedIn[ip].userId = userId;
                    usersLoggedIn[ip].timeOfLogin = DateTime.Now;
                }
                else
                {
                    usersLoggedIn[ip] = new LoggedInUser(userId, DateTime.Now);
                }
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
                // Remove all IPs that map to the userId (normally one)
                IPAddress ip;
                while ((ip = usersLoggedIn.FirstOrDefault(x => x.Value.userId == userId).Key) != null)
                {
                    usersLoggedIn.Remove(ip);
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
            if(usersLoggedIn.TryGetValue(ip, out user))
            {
                // Check if the user session is expired!
                if (user.timeOfLogin.AddSeconds(SESSION_TIMEOUT_S).CompareTo(DateTime.Now) < 0)
                {
                    // Expired. Log the user out and return -1
                    LogUserOut(user.userId);
                    return -1;
                }

                return user.userId;
            }
            return -1;
        }
    }
}
