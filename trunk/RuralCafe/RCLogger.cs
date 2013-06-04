﻿/*
   Copyright 2010 Jay Chen

   Licensed under the Apache License, Version 2.0 (the "License");
   you may not use this file except in compliance with the License.
   You may obtain a copy of the License at

       http://www.apache.org/licenses/LICENSE-2.0

   Unless required by applicable law or agreed to in writing, software
   distributed under the License is distributed on an "AS IS" BASIS,
   WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
   See the License for the specific language governing permissions and
   limitations under the License.

*/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using log4net;
using log4net.Core;
using log4net.Appender;
using log4net.Layout;
using log4net.Repository.Hierarchy;
using log4net.Config;



namespace RuralCafe
{
    /// <summary>
    /// All valid log levels for log4net.
    /// </summary>
    public enum LogLevel
    {
        ALL,
        DEBUG,
        INFO,
        WARN,
        ERROR,
        FATAL,
        OFF
    }

    /// <summary>
    /// Class that configures the log4net loggers.
    /// </summary>
    public abstract class RCLogger
    {
        /// <summary>
        /// Initializes and configures the logger(s).
        /// </summary>
        public static void InitLogger()
        {
            // Configure based on app.config
            XmlConfigurator.Configure();
            // Get root logger
            Hierarchy h = (Hierarchy)LogManager.GetRepository();
            Logger rootLogger = h.Root;

            // Get property for LogLevel
            LogLevel level = Properties.Settings.Default.LOGLEVEL;
            // and set root logger level accordingly
            rootLogger.Level = h.LevelMap[level.ToString()];
        }
    }

    /// <summary>
    /// Logging facility for saving event and debug messages.
    /// Used in conjunction with a single proxy.
    /// </summary>
    public class RCLogger2
    {
        string _proxyName;
        string _logPath;
        string _messagesFile;
        string _debugFile;

        /// <summary>
        /// Constructor called once by each proxy to initialize directories for logs.
        /// </summary>
        /// <param name="proxyName">Name of the calling proxy to log messages for.</param>
        /// <param name="logPath">Relative or absolute path for the logs.</param>
        public RCLogger2(string proxyName, string logPath)
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

        /// <summary>
        /// Write an entry to the message and debug logs regarding a request.
        /// </summary>
        /// <param name="requestId">Request ID.</param>
        /// <param name="entry">Log entry.</param>
        public void WriteMessage(int requestId, string entry)
        {
            // timestamp
            entry = requestId + " " + entry;

            Write(_logPath + _messagesFile, entry);

            Console.WriteLine(_proxyName + ": " + entry);
            Write(_logPath + _debugFile, entry);
        }
        /// <summary>
        /// Write an entry to the debug log regarding a request.
        /// </summary>
        /// <param name="requestId">Request ID.</param>
        /// <param name="entry">Log entry.</param>
        public void WriteDebug(int requestId, string entry)
        {
            // timestamp
            entry = requestId + " " + DateTime.Now + " " + entry;

            Console.WriteLine(_proxyName + ": " + entry);
            Write(_logPath + _debugFile, entry);
        }

        /// <summary>
        /// Write a message to the log file.
        /// </summary>
        /// <param name="filePath">Log file path.</param>
        /// <param name="entry">Log entry.</param>
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
                // JAY: not sure what to do here if the logging can't be done this is rather critical.
                // XXX: do nothing
            }
        }
    }
}
