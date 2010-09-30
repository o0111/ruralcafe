using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;

namespace RuralCafe
{
    public class Logger
    {
        string _proxyName;
        string _logPath;
        string _messagesFile;
        string _debugFile;

        // called once by the each proxy to initialize directories for logs
        public Logger(string proxyName, string logPath)
        {
            _proxyName = proxyName;

            _logPath = logPath;
            _messagesFile = DateTime.Now.ToString("s") + "-messages.log";
            _messagesFile = _messagesFile.Replace(':', '.');

            _debugFile = DateTime.Now.ToString("s") + "-debug.log";
            _debugFile = _debugFile.Replace(':', '.');

            if (!Directory.Exists(_logPath))
            {
                System.IO.Directory.CreateDirectory(_logPath);
            }
            if (!System.IO.File.Exists(_logPath + _messagesFile))
            {
                FileStream dummyStream = System.IO.File.Create(_logPath + _messagesFile);
                dummyStream.Close();
            }
            if (!System.IO.File.Exists(_logPath + _debugFile))
            {
                FileStream dummyStream = System.IO.File.Create(_logPath + _debugFile);
                dummyStream.Close();
            }
        }

        public string Path
        {
            set { _logPath = value; }
            get { return _logPath; }
        }

        // these functions to append the path for logs
        public void WriteMessage(int requestId, string entry)
        {
            // timestamp
            entry = requestId + " " + entry;

            Write(_logPath + _messagesFile, entry);

            Console.WriteLine(_proxyName + ": " + entry);
            Write(_logPath + _debugFile, entry);
        }
        public void WriteDebug(int requestId, string entry)
        {
            // timestamp
            entry = requestId + " " + DateTime.Now + " " + entry;

            Console.WriteLine(_proxyName + ": " + entry);
            Write(_logPath + _debugFile, entry);
        }

        // private helper function
        private void Write(string filePath, string entry)
        {
            System.IO.StreamWriter s = null;

            try
            {
                s = System.IO.File.AppendText(filePath);
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
