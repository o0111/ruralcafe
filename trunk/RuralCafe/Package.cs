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
using System.IO.Compression;

namespace RuralCafe
{
    public class Package
    {
        private LinkedList<RequestObject> _package;
        public long _contentSize;
        public long _indexSize;
        public long _compressedSize;

        public Package()
        {
            _package = new LinkedList<RequestObject>();
            _contentSize = 0;
            _indexSize = 0;
            _compressedSize = 0;
        }

        public LinkedList<RequestObject> GetObjects()
        {
            return _package;
        }

        // XXX: only gets called by the remote proxy
        // XXX: QUOTA STUFF - currently simple algorithm
        // XXX: should be changed to check for compressed size rather than actual size
        public string AddToPackage(RequestObject requestObject, long quota)
        {
            if (_package.Contains(requestObject))
            {
                //LogDebug("object exists in package: " + requestObject._uri);
                return "object exists in package: " + requestObject._uri;
            }

            requestObject._fileSize = Util.GetFileSize(requestObject._cacheFileName);
            // make sure the object has some content
            if (requestObject._fileSize <= 0)
            {
                //LogDebug("object has no content: " + requestObject._uri);
                return "object has no content: " + requestObject._uri;
            }

            // make sure the url has no spaces
            if (requestObject._uri.Contains(' '))
            {
                //LogDebug("object contains spaces: " + requestObject._uri);
                return "object contains spaces: " + requestObject._uri;
            }

            // see if the thing fits
            if ((quota - requestObject._fileSize) < 0)
            {
                // exceeded quota
                // don't add this page, or add partial page, and quit
                //LogDebug("object too large: " + requestObject._uri + " " + requestObject._fileSize + " bytes" +
                //    " > " + _quota + " bytes");
                return "object doesn't fit in quota: " + requestObject._uri;
            }

            // add this page to the package
            _package.AddLast(requestObject);

            return "";
        }

        // unpack the package contents
        // given the URI's and the sizes
        public static long UnpackPackage(LocalRequest request)
        {
            string packageIndexSizeStr = request._webResponse.GetResponseHeader("Package-IndexSize");
            string packageContentSizeStr = request._webResponse.GetResponseHeader("Package-ContentSize");
            long packageIndexSize = Int64.Parse(packageIndexSizeStr);
            long packageContentSize = Int64.Parse(packageContentSizeStr);
            string packageFileName = request._requestObject._packageIndexFileName;
            string unpackedPackageFileName = packageFileName.Replace(".gzip", "");

            GZipWrapper.GZipDecompress(packageFileName, unpackedPackageFileName, packageIndexSize + packageContentSize);
            FileStream packageFs = new FileStream(unpackedPackageFileName, FileMode.Open);

            // read the package index
            Byte[] packageIndexBuffer = new Byte[packageIndexSize];
            packageFs.Read(packageIndexBuffer, 0, (int)packageIndexSize);

            // split the big package file into pieces
            string[] stringSeparator = new string[] { "\r\n" };
            System.Text.UTF8Encoding enc = new System.Text.UTF8Encoding();
            string package = enc.GetString(packageIndexBuffer);
            string[] packageContentArr = package.Split(stringSeparator, StringSplitOptions.RemoveEmptyEntries);

            Byte[] bufferOverflow = new Byte[1024];
            int bufferOverflowCount = 0;
            int bytesRead = 0;
            long bytesReadOfCurrFile = 0;
            long unpackedBytes = 0;
            Byte[] buffer = new Byte[1024];
            string[] packageEntryArr;
            string currUri = "";
            long currFileSize = 0;
            foreach (string entry in packageContentArr)
            {
                stringSeparator = new string[] { " " };
                packageEntryArr = entry.Split(stringSeparator, StringSplitOptions.RemoveEmptyEntries);
                currUri = packageEntryArr[0];

                try
                {
                    currFileSize = Int64.Parse(packageEntryArr[1]);
                }
                catch (Exception e)
                {
                    request.LogDebug("problem unpacking: " + entry + " " + e.StackTrace + " " + e.Message);
                    return unpackedBytes;
                }

                if (!Util.IsValidUri(currUri))
                {
                    request.LogDebug("problem unpacking: " + currUri);
                    return unpackedBytes;
                }
                RequestObject requestObject = new RequestObject(request._proxy, currUri);

                unpackedBytes += currFileSize;

                request.LogDebug("unpacking: " + requestObject._uri + " - " + currFileSize + " bytes");

                // make sure the file doesn't already exist
                bool existed = false;
                FileInfo ftest = new FileInfo(requestObject._cacheFileName);
                if (ftest.Exists)
                {
                    existed = true;
                }

                if (!Util.DeleteFile(requestObject._cacheFileName))
                {
                    return unpackedBytes;
                }

                // create directory if it doesn't exist
                if (!Util.CreateDirectoryForFile(requestObject._cacheFileName))
                {
                    return unpackedBytes;
                }

                // create the file if it doesn't exist
                FileStream currFileFS = Util.CreateFile(requestObject._cacheFileName);
                if (currFileFS == null)
                {
                    return unpackedBytes;
                }
                // check for overflow from previous file, and use it up first
                if (bufferOverflowCount > 0)
                {
                    Buffer.BlockCopy(bufferOverflow, 0, buffer, 0, bufferOverflowCount);
                    bytesRead = bufferOverflowCount;
                }
                else
                {
                    bytesRead = packageFs.Read(buffer, 0, 1024);
                }

                // reset for current file
                bytesReadOfCurrFile = 0;
                while (bytesRead != 0 && bytesReadOfCurrFile < currFileSize)
                {
                    // check if we read too much
                    if (bytesReadOfCurrFile + bytesRead > currFileSize)
                    {
                        // bytes left must be less than 1024, fine to convert to Int
                        int bytesLeftOfCurrFile = ((int)(currFileSize - bytesReadOfCurrFile));
                        currFileFS.Write(buffer, 0, bytesLeftOfCurrFile);
                        // done with this file
                        bytesReadOfCurrFile = currFileSize;

                        // handle overflow
                        bufferOverflowCount = bytesRead - bytesLeftOfCurrFile;
                        Buffer.BlockCopy(buffer, bytesLeftOfCurrFile, bufferOverflow, 0, bytesRead - bytesLeftOfCurrFile);
                    }
                    else
                    {
                        // append what we read
                        currFileFS.Write(buffer, 0, bytesRead);
                        // update bytesReadOfCurrFile
                        bytesReadOfCurrFile += bytesRead;

                        bytesRead = packageFs.Read(buffer, 0, 1024);
                    }
                }

                if (bytesReadOfCurrFile != currFileSize)
                {
                    // ran out of bytes for this file
                    request.LogDebug("error, unexpected package size: " + requestObject._cacheFileName +
                        "(" + bytesReadOfCurrFile + " / " + currFileSize + ")");
                    return unpackedBytes * -1;
                }

                currFileFS.Close();

                // add the file to Lucene
                if (Util.IsParseable(requestObject))
                {
                    string document = Util.ReadFileAsString(requestObject._cacheFileName);
                    string title = Util.GetPageTitle(document);
                    string content = Util.GetPageContent(document);

                    //request.LogDebug("indexing: " + requestObject._uri);
                    if (!existed)
                    {
                        CacheIndexer.IndexDocument(((LocalProxy)request._proxy)._indexPath, "Content-Type: text/html", requestObject._uri, title, content);
                    }
                }
            }
            if (packageFs != null)
            {
                packageFs.Close();
            }
            return unpackedBytes;
        }
    }
}
