/*
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
    public static class RCLogger
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
}
