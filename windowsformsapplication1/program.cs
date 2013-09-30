using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Crawler
{
    /// <summary>
    /// Just an entry-point class.
    /// </summary>
    public static class Program
    {
        /// <summary>
        /// The main entry point for the application, if run standalone.
        /// </summary>
        [STAThread]
        public static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new ACrawlerWin(Directory.GetCurrentDirectory() + Path.DirectorySeparatorChar, null));
        }
    }
}
