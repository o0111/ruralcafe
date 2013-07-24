using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;

namespace RuralCafe
{
    /// <summary>
    /// A logged in user. Contains his id and the time of login.
    /// </summary>
    public class LoggedInUser
    {
        public int userId;
        public DateTime timeOfLogin;

        public LoggedInUser(int userId, DateTime timeOfLogin)
        {
            this.userId = userId;
            this.timeOfLogin = timeOfLogin;
        }
    }

    public class SessionManager
    {
        // Constants
        private const long sessionTimeoutS = 60 * 60 * 24 * 14; // 2 weeks
        
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
        /// Gets the user id of a logged in user by its ip or -1.
        /// </summary>
        /// <param name="ip">The IP address of the client.</param>
        /// <returns>The user id or -1.</returns>
        public int GetUserId(IPAddress ip)
        {
            LoggedInUser user;
            if(usersLoggedIn.TryGetValue(ip, out user))
            {
                return user.userId;
            }
            return -1;
        }
    }
}
