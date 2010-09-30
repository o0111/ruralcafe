using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Web;
using System.Net;

namespace RuralCafe
{
    class Util
    {
        private static Dictionary<string, string> _extMap = new Dictionary<string, string>();

        private static Object filesystemLock = new Object();

        // Creates a directory for a file (locked method)
        public static bool CreateDirectoryForFile(string fileName)
        {
            try
            {
                lock (filesystemLock)
                {
                    int offset = fileName.LastIndexOf("\\");
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
            catch (Exception e)
            {
                //LogDebug("problem creating directory for file: " + fileName + " " + e.StackTrace + " " + e.Message);
                return false;
            }

            return true;
        }
        public static bool DeleteFile(string fileName)
        {
            try
            {
                // check if it exists
                FileInfo f = new FileInfo(fileName);
                if (f.Exists)
                {
                    f.Delete();
                    return true;
                }
            }
            catch (Exception e)
            {
                //LogDebug("problem deleting file: " + fileName + " " + e.StackTrace + " " + e.Message);
                return false;
            }

            return true;
        }
        public static bool DeleteZeroSizedFile(string fileName)
        {
            try
            {
                // check if it exists
                FileInfo f = new FileInfo(fileName);
                if (f.Exists)
                {
                    if (f.Length == 0)
                    {
                        f.Delete();
                    }
                    /*
                    else
                    {
                        LogDebug("page exists: " + request.RequestUri + " skipping");
                        return false;
                    }*/
                }
            }
            catch (Exception e)
            {
                //LogDebug("problem deleting file: " + fileName + " " + e.StackTrace + " " + e.Message);
                return false;
            }

            return true;
        }
        public static FileStream CreateFile(string fileName)
        {
            FileStream fs;
            try
            {
                fs = new FileStream(fileName, FileMode.Create);
                if (fs.CanSeek)
                {
                    fs.Seek(0, SeekOrigin.Begin);
                }
            }
            catch (Exception e)
            {
                //LogDebug("problem creating file: " + fileName + " " + e.StackTrace + " " + e.Message);
                return null;
            }

            return fs;
        }        

        // helper function to get the file size on disk of a page, returns -1 for non existent
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

                if (f.Length >= 0)
                {
                    return f.Length;
                }
            }
            catch (Exception e)
            {
                //LogDebug("problem getting file info: " + fileName + " " + e.StackTrace + " " + e.Message);
                return -1;
            }

            return -1;
        }
        public static string GetFileExtensionFromFileName(string fileName)
        {
            string fileExtension = "";
            int offset1 = fileName.LastIndexOf("\\");
            int offset2 = fileName.LastIndexOf(".");
            if (offset2 > offset1)
            {
                fileExtension = fileName.Substring(offset2);
            }
            return fileExtension;
        }
        public static string GetFileExtension(string stringUri)
        {
            string fileExtension = "";
            int offset1 = stringUri.LastIndexOf("/");
            int offset2 = stringUri.LastIndexOf(".");
            if (offset2 > offset1)
            {
                fileExtension = stringUri.Substring(offset2);
            }
            fileExtension = stringUri.Split('?')[0];
            return fileExtension;
        }
        public static string GetContentType(string k)
        {
            if (_extMap.ContainsKey(k))
            {
                return _extMap[k];
            }
            // JJJ: not sure how squid or whatever does this
            // content/unknown, but the problem is that after caching how do we know the content type without the extension in place
            return "text/html";
        }

        public static string GetContentTypeOfFile(string fileName)
        {
            // only try fo xhtml and html for pages without extensions
            string fileExtension = GetFileExtensionFromFileName(fileName);
            if (fileExtension.StartsWith(".asp"))
            {
                return "text/html";
            }
            if (fileExtension.StartsWith(".php"))
            {
                return "text/html";
            }
            if (!fileExtension.Equals(""))
            {
                return GetContentType(fileExtension);
            }

            try
            {
                // make sure file exists
                FileInfo f = new FileInfo(fileName);
                if (!f.Exists)
                {
                    return "content/unknown";
                }

                // read beginning of the file
                string fileContents = ReadFileAsString(fileName).Trim();
                if (fileContents != null)
                {
                    if (fileContents.StartsWith("<!DOCTYPE html") ||
                        fileContents.StartsWith("<html>"))
                    {
                        return "text/html";
                    }
                }
            }
            catch (Exception e)
            {
                //LogDebug("problem getting content type of file " + e.StackTrace + " " + e.Message);
            }

            return "content/unknown";
        }

        // check if the URI is parseable by RuralCafe
        public static bool IsParseable(RequestObject requestObject)
        {
            /*
            string contentType = requestObject._webRequest.ContentType;
            if (contentType.Contains("text") || 
                contentType.Contains("htm"))
            {
                return true;
            }
            return false;
             */
            string contentType = GetContentTypeOfFile(requestObject._cacheFileName);

            if (contentType.Contains("htm"))
            {
                return true;
            }

            string fileExtension = GetFileExtension(requestObject._uri);
            if (fileExtension.Contains(".asp") ||
                fileExtension.Contains(".php"))
            {
                return true;
            }
            return false;
        }

        public static bool IsValidUri(string Uri)
        {
            try
            {
                HttpWebRequest tempRequest = (HttpWebRequest)WebRequest.Create(Uri);
            }
            catch (Exception e)
            {
                return false;
            }
            if (Uri.Trim().Length == 0)
            {
                return false;
            }
            if (Uri.Equals("http://"))
            {
                return false;
            }
            return true;
        }

        // reads in a file as a string so we can parse it easily
        public static string ReadFileAsString(string filePath)
        {
            string str = "";

            try
            {
                FileInfo f = new FileInfo(filePath);
                if (!f.Exists)
                {
                    return str;
                }

                //int offset = 0;
                //byte[] buffer = new byte[1024]; // magic number 32

                // open the file stream
                FileStream fs = f.Open(FileMode.Open, FileAccess.Read);

                // loop and get the bytes we need if we couldn't get it in one go
                StreamReader r = new StreamReader(fs);
                string t;
                while ((t = r.ReadLine()) != null)
                {
                    // append to the string
                    str += t;
                }

                r.Close();
                fs.Close();
            }
            catch (Exception e)
            {
                // do nothing
                //LogDebug("Could not read file as string " + e.StackTrace + " " + e.Message);
            }

            return str;
        }

        public static string GetPageContent(string pageContent)
        {
            return pageContent;
        }
        public static string GetPageTitle(string pageContent)
        {
            int index1 = pageContent.IndexOf("<title>");
            int index2 = pageContent.IndexOf("</title>");
            // make sure the title tag exists
            if (index1 < 0 || index2 < 0)
            {
                return "";
            }
            // make sure there's something between the two indices
            index1 += "<title>".Length;
            if (index1 >= index2)
            {
                return "";
            }

            string title = pageContent.Substring(index1, index2 - index1);

            return title;
        }

        private static void SetSuffix(string k, string v)
        {
            _extMap.Add(k, v);
        }

        public static void FillExtMap()
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
            SetSuffix(".text", "text/plain");
            SetSuffix(".c", "text/plain");
            SetSuffix(".cc", "text/plain");
            SetSuffix(".c++", "text/plain");
            SetSuffix(".h", "text/plain");
            SetSuffix(".pl", "text/plain");
            SetSuffix(".txt", "text/plain");
            SetSuffix(".java", "text/plain");
            SetSuffix(".xml", "text/html");
            SetSuffix(".ico", "image/x-icon");
        }
    }
}
