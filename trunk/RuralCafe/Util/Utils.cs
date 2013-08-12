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
using System.Web;
using System.Net;
using System.Collections.Specialized;
using System.Text.RegularExpressions;
using System.Net.Sockets;
using log4net;
using HtmlAgilityPack;
using System.Net.NetworkInformation;
using System.Threading;
using System.Diagnostics;
using Microsoft.Win32;

namespace RuralCafe.Util
{
    /// <summary>
    /// A set of utility functions for manipulating files and directories, 
    /// getting file extensions and mime types, and page contents.
    /// </summary>
    public static class Utils
    {
        /// <summary>
        /// Map from file extension to HTTP MIME type
        /// </summary>
        private static Dictionary<string, string> _extMap = new Dictionary<string, string>();
        /// <summary>
        /// The buffer size for streaming.
        /// </summary>
        private const int STREAM_BUFFER_SIZE = 32768;
        /// <summary>
        /// A Lock object on the file system.
        /// </summary>
        private static readonly Object FILE_SYSTEM_LOCK = new Object();

        #region content types and extensions

        /// <summary>
        /// Static constructor.
        /// Fills in the extension to content-type map with static mappings.
        /// </summary>
        static Utils()
        {
            SetSuffix("", "content/unknown");
            SetSuffix(".uu", "application/octet-stream");
            SetSuffix(".exe", "application/octet-stream");
            SetSuffix(".ps", "application/postscript");
            SetSuffix(".zip", "application/zip");
            SetSuffix(".sh", "application/x-shar");
            SetSuffix(".tar", "application/x-tar");
            SetSuffix(".snd", "audio/basic");
            SetSuffix(".au", "audio/basic");
            SetSuffix(".wav", "audio/x-wav");
            SetSuffix(".gif", "image/gif");
            SetSuffix(".jpg", "image/jpeg");
            SetSuffix(".jpeg", "image/jpeg");
            SetSuffix(".png", "image/png");
            SetSuffix(".htm", "text/html");
            SetSuffix(".html", "text/html");
            SetSuffix(".c", "text/plain");
            SetSuffix(".cc", "text/plain");
            SetSuffix(".c++", "text/plain");
            SetSuffix(".h", "text/plain");
            SetSuffix(".pl", "text/plain");
            SetSuffix(".txt", "text/plain");
            SetSuffix(".text", "text/plain");
            SetSuffix(".java", "text/plain");
            SetSuffix(".xml", "text/xml");
            SetSuffix(".js", "application/javascript");
            SetSuffix(".css", "text/css");
            SetSuffix(".ico", "image/x-icon");
            SetSuffix(".asp", "text/html");
            SetSuffix(".php", "text/html");
        }

        /// <summary>
        /// Adds a key value pair to the extension to content-type map.
        /// </summary>
        /// <param name="k">Key.</param>
        /// <param name="v">Value.</param>
        private static void SetSuffix(string k, string v)
        {
            _extMap.Add(k, v);
        }

        /// <summary>
        /// Gets the file extension from the file name.
        /// </summary>
        /// <param name="fileName">File name to parse.</param>
        /// <returns>File extension as a string.</returns>
        public static string GetFileExtensionFromFileName(string fileName)
        {
            string fileExtension = fileName;
            
            int offsetQM = fileExtension.LastIndexOf("?");
            if (offsetQM >= 0)
            {
                fileExtension = fileExtension.Substring(0, offsetQM);
            }

            int offsetSep = fileExtension.LastIndexOf(Path.DirectorySeparatorChar);
            if (offsetSep > 0)
            {
                fileExtension = fileExtension.Substring(offsetSep + 1);
            }

            int offsetDot = fileExtension.LastIndexOf(".");
            if (offsetDot > 0)
            {
                fileExtension = fileExtension.Substring(offsetDot);
            }
            return fileExtension;
        }
        /// <summary>
        /// Gets the file extension from a URI.
        /// </summary>
        /// <param name="stringUri">URI to parse.</param>
        /// <returns>File extension as a string.</returns>
        public static string GetFileExtension(string stringUri)
        {
            string fileExtension = stringUri;
            // throw away query terms
            fileExtension = fileExtension.Split('?')[0];
            fileExtension = fileExtension.Split('#')[0];

            int offset1 = fileExtension.LastIndexOf("/");
            int offset2 = fileExtension.LastIndexOf(".");
            if (offset2 > offset1)
            {
                return fileExtension.Substring(offset2);
            }
            
            return "";
        }

        /// <summary>
        /// Returns the HTTP "Content-Type" of an extension.
        /// </summary>
        /// <param name="extension">File extension.</param>
        /// <returns>Content type of the extension or "content/unknown" for unknown extensions.</returns>
        public static string GetContentType(string extension)
        {
            if (_extMap.ContainsKey(extension))
            {
                return _extMap[extension];
            }
            // JAY: not sure how squid or whatever does this
            // content/unknown, but the problem is that after caching how do we know the content type without the extension in place
            return "content/unknown";
        }

        /// <summary>
        /// Tries to return the content type of a file based on its extension.
        /// Also tries to peek inside the file for HTML headers.
        /// </summary>
        /// <param name="fileName">Name of the file.</param>
        /// <returns>Content type of the file.</returns>
        public static string GetContentTypeOfFile(string fileName)
        {
            string fileExtension = GetFileExtensionFromFileName(fileName);
            string contentType = "content/unknown";

            if (!fileExtension.Equals(""))
            {
                contentType = GetContentType(fileExtension);
            }
            if (!contentType.Equals("content/unknown"))
            {
                return contentType;
            }

            // only try xhtml and html for pages without extensions
            try
            {
                // make sure file exists
                FileInfo f = new FileInfo(fileName);
                if (f.Exists)
                {
                    using (StreamReader reader = new StreamReader(new FileStream(fileName, FileMode.Open)))
                    {
                        // We only want to read up to 14 chars (length of "<!DOCTYPE html")
                        char[] buffer = new char[14];
                        int charsRead = 0;
                        int pos = 0;
                        while ((charsRead = reader.Read(buffer, pos, buffer.Length - pos)) != 0)
                        {
                            pos += charsRead;
                            if (pos == buffer.Length)
                            {
                                // We have read 14 chars.
                                break;
                            }
                        }

                        string fileContents = new string(buffer).ToLower();
                        if (fileContents.StartsWith("<html>") ||
                            fileContents.StartsWith("<!doctype html"))
                        {
                            return "text/html";
                        }
                    }
                }
            }
            catch (Exception)
            {
                // do nothing
            }
            return contentType;
        }

        #endregion
        #region file utils

        /// <summary>
        /// Creates a directory for a file (locked method).
        /// JAY: this lock is probably poorly implemented.
        /// </summary>
        /// <param name="fileName">Name of the file.</param>
        /// <returns>True or false for success or failure.</returns>
        public static bool CreateDirectoryForFile(string fileName)
        {
            if (fileName == null || fileName == "")
            {
                return false;
            }

            try
            {
                lock (FILE_SYSTEM_LOCK)
                {
                    int offset = fileName.LastIndexOf(Path.DirectorySeparatorChar.ToString());
                    if (offset > 0)
                    {
                        string currPath = fileName.Substring(0, offset + 1);
                        if (!Directory.Exists(currPath) &&
                            !File.Exists(currPath))
                        {
                            System.IO.Directory.CreateDirectory(currPath);
                        }
                    }
                }
            }
            catch (Exception)
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// Deletes a file.
        /// </summary>
        /// <param name="fileName">Name of the file.</param>
        /// <returns>True or false for success or failure.</returns>
        public static bool DeleteFile(string fileName)
        {
            try
            {
                FileInfo f = new FileInfo(fileName);
                if (f.Exists)
                {
                    f.Delete();
                }
            }
            catch (Exception)
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// Creates a file. If it existed, it will be written to at the beginning.
        /// </summary>
        /// <param name="fileName">The name of the file.</param>
        /// <returns>Returns the FileStream for the created file.</returns>
        public static FileStream CreateFile(string fileName)
        {
            if (!CreateDirectoryForFile(fileName))
            {
                return null;
            }
            FileStream fs;
            try
            {
                fs = new FileStream(fileName, FileMode.Create);
                if (fs.CanSeek)
                {
                    fs.Seek(0, SeekOrigin.Begin);
                }
            }
            catch (Exception)
            {
                return null;
            }

            return fs;
        }

        /// <summary>
        /// Helper function to get the file size on disk of a page.
        /// </summary>
        /// <param name="fileName">Name of the file.</param>
        /// <returns>-1 for non-existent file. Otherwise the file size.</returns>
        public static long GetFileSize(string fileName)
        {
            FileInfo f;
            try
            {
                f = new FileInfo(fileName);

                if (!f.Exists)
                {
                    return -1;
                }
                return f.Length;
            }
            catch (Exception)
            {
                return -1;
            }
        }

        /// <summary>
        /// Reads in a file as a string for ease of parsing.
        /// </summary>
        /// <param name="fileName">Name of the file.</param>
        /// <returns>String containing the file's contents. If there is a problem return an empty string.</returns>
        public static string ReadFileAsString(string fileName)
        {
            string str = "";

            try
            {
                FileInfo f = new FileInfo(fileName);
                if (!f.Exists)
                {
                    return str;
                }

                // open the file stream
                using (FileStream fs = f.Open(FileMode.Open, FileAccess.Read))
                using (StreamReader r = new StreamReader(fs))
                {
                    str = r.ReadToEnd();
                }
            }
            catch (Exception)
            {
                // do nothing
            }

            return str;
        }

        /// <summary>
        /// Checks if a filename is too long. A filename must be shorter than 260 chars and its
        /// path must be shorter than 248 chars.
        /// </summary>
        /// <param name="fileName">The filename.</param>
        /// <returns>true, if the filename is OK; false, if it is too long.</returns>
        public static bool IsNotTooLongFileName(string fileName)
        {
            string pathName = fileName.Substring(0, fileName.LastIndexOf(Path.DirectorySeparatorChar));
            return pathName.Length < 248 && fileName.Length < 260;
        }

        #endregion
        #region stream utils

        /// <summary>
        /// Reads in incoming stream to the end and returns the result
        /// </summary>
        /// <param name="inStream">The incoming stream.</param>
        /// <returns>The content of the stream.</returns>
        public static string ReadStreamToEnd(Stream inStream)
        {
            return new StreamReader(inStream).ReadToEnd();
        }

        /// <summary>
        /// Streams all content from an input- to an output stream. Any exceptions are forwarded.
        /// </summary>
        /// <param name="inStream">The input stream.</param>
        /// <param name="outStream">The output stream.</param>
        /// <returns>The bytes written to the output stream.</returns>
        public static long Stream(Stream inStream, Stream outStream)
        {
            byte[] buffer = new byte[STREAM_BUFFER_SIZE];
            long bytesWritten = 0;
            while (true)
            {
                int read = inStream.Read(buffer, 0, buffer.Length);
                if (read <= 0)
                {
                    break;
                }
                outStream.Write(buffer, 0, read);
                bytesWritten += read;
            }
            return bytesWritten;
        }
        #endregion
        #region Other utils

        /// <summary>
        /// Converts a NameValueCollection into a Dictionary.
        /// </summary>
        /// <param name="source">The NameValueCollection.</param>
        /// <returns>The Dictionary.</returns>
        public static Dictionary<string, string[]> NVCToDictionary(NameValueCollection source)
        {
            return source.AllKeys.ToDictionary(k => k, k => source.GetValues(k));
        }

        /// <summary>
        /// Converts a Dictionary into a NameValueCollection.
        /// </summary>
        /// <param name="source">The Dictionary.</param>
        /// <returns>The NameValueCollection.</returns>
        public static NameValueCollection DictionaryToNVC(Dictionary<string, string[]> source)
        {
            NameValueCollection nameValueCollection = new NameValueCollection();
            foreach (KeyValuePair<string, string[]> kvp in source)
            {
                foreach (string value in kvp.Value)
                {
                    nameValueCollection.Add(kvp.Key, value);
                }
            }
            return nameValueCollection;
        }

        /// <summary>
        /// Writes a key to the registry. Non-existing keys will be created and existing will be overridden.
        /// 
        /// Source: http://www.codeproject.com/Articles/3389/Read-write-and-delete-from-registry-with-C
        /// </summary>
        /// <param name="rk">The base registry key.</param>
        /// <param name="subKeyPath">The subkey path.</param>
        /// <param name="keyName">The key name.</param>
        /// <param name="value">The value to store.</param>
        /// <returns></returns>
        public static void WriteRegistryKey(RegistryKey rk, string subKeyPath, string keyName, object value)
        {
            // I have to use CreateSubKey 
            // (create or open it if already exits), 
            // 'cause OpenSubKey open a subKey as read-only
            RegistryKey sk1 = rk.OpenSubKey(subKeyPath, true);
            //RegistryKey sk1 = rk.CreateSubKey(subKeyPath);
            // Save the value
            sk1.SetValue(keyName, value);
        }

        /// <summary>
        /// Gets the network interface that is being used for the given IP or null.
        /// </summary>
        /// <param name="ip">The IP address.</param>
        /// <returns>The used network iterface.</returns>
        public static NetworkInterface GetNetworkInterfaceFor(IPAddress ip)
        {
            NetworkInterface[] nics = System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces();
            NetworkInterface nic = null;
            foreach (NetworkInterface n in nics)
            {
                IPInterfaceProperties ipProps = n.GetIPProperties();
                // check if localAddr is in ipProps.UnicastAddresses
                foreach (UnicastIPAddressInformation unicastAddr in ipProps.UnicastAddresses)
                {
                    if (unicastAddr.Address.Equals(ip))
                    {
                        nic = n;
                        break;
                    }
                }
            }
            return nic;
        }

        #endregion
    }
}