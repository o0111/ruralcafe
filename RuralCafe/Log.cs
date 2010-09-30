using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;

namespace RuralCafe
{
    class Log
    {
        public static string REQUEST_LOG_PATH = Directory.GetCurrentDirectory() + @"\Log\requests.log";
        // public static string EXCEPTION_LOG_PATH = Directory.GetCurrentDirectory() + @"\Log\exceptions.log";

        public static void WriteRequest(string entry)
        {
            Write(REQUEST_LOG_PATH, entry);
        }

        public static void WriteResponse(string entry)
        {
            Write(REQUEST_LOG_PATH, entry);
        }

        public static void WriteException(string entry)
        {
            Write(REQUEST_LOG_PATH, entry);
        }

        public static void Write(string path, string entry)
        {
            System.IO.StreamWriter s = null;

            try
            {
                s = System.IO.File.AppendText(path);
                s.WriteLine(entry);
                s.Close();
            }
            catch (Exception)
            {
                // XXX: do nothing
            }
            finally
            {
                s = null;
            }
        }
    }
}
