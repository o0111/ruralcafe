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
using System.Net;

namespace RuralCafe
{
    /// <summary>
    /// Package used to transfer a group of prefetched files from the remote to local proxy
    /// </summary>
    public class Package
    {
        private LinkedList<RCRequest> _rcRequests;
        private long _contentSize;
        private long _indexSize;

        /// <summary>
        /// Constructor for a RuralCafe package.
        /// </summary>
        public Package()
        {
            _rcRequests = new LinkedList<RCRequest>();
            _contentSize = 0;
            _indexSize = 0;
        }

        /// <summary>Size of the package contents.</summary>
        public long ContentSize
        {
            set { _contentSize = value; }
            get { return _contentSize; }
        }
        /// <summary>Size of the package index.</summary>
        public long IndexSize
        {
            set { _indexSize = value; }
            get { return _indexSize; }
        }
        /// <summary>List of the RCRequests in this package.</summary>
        public LinkedList<RCRequest> RCRequests
        {
            get { return _rcRequests; }
        }

        /// <summary>
        /// Adds a RCRequest to the package given the quota limitation.
        /// Only called by the remote proxy.
        /// XXX: quota is currently simple algorithm.
        /// XXX: should be changed to check for compressed size rather than actual size.
        /// </summary>
        /// <param name="requestHandler">Calling handler for this method.</param>
        /// <param name="rcRequest">RCRequest to add.</param>
        /// <param name="quota">Quota limit.</param>
        /// <returns>Error message.</returns>
        public bool Pack(RemoteRequestHandler requestHandler, RCRequest rcRequest, ref long quota)
        {
            if (rcRequest.Uri.Contains(' '))
            {
                requestHandler.Logger.Debug("object contains spaces: " + rcRequest.Uri);
                return false;
            }

            if (_rcRequests.Contains(rcRequest))
            {
                requestHandler.Logger.Debug("object exists in package: " + rcRequest.Uri);
                return false;
            }

            rcRequest.FileSize = Util.GetFileSize(rcRequest.CacheFileName);
            if (rcRequest.FileSize <= 0)
            {
                return false;
            }

            // quota check
            if ((quota - rcRequest.FileSize) < 0)
            {
                requestHandler.Logger.Debug("object doesn't fit in quota: " + rcRequest.Uri);
                return false;
            }

            _rcRequests.AddLast(rcRequest);
            quota -= rcRequest.FileSize;

            return true;
        }
        
        /// <summary>
        /// Adds all the requests to the package.
        /// </summary>
        /// <param name="requests"></param>
        /// <returns></returns>
        public LinkedList<RCRequest> Pack(RemoteRequestHandler requestHandler, LinkedList<RCRequest> requests, ref long quota)
        {
            LinkedList<RCRequest> addedObjects = new LinkedList<RCRequest>();
            // add files that were completed to the package
            foreach (RCRequest request in requests)
            {
                // add to the package
                if (Pack(requestHandler, request, ref quota))
                {
                    addedObjects.AddLast(request);
                }
            }
            
            return addedObjects;
        }

        /// <summary>
        /// Unpacks the package contents and indexes them.
        /// </summary>
        /// <param name="indexPath">Path to the index.</param>
        /// <param name="requestHandler">Calling handler for this method.</param>
        /// <returns>Total unpacked content size.</returns>
        public static long Unpack(LocalRequestHandler requestHandler, RCSpecificResponseHeaders headers,
            string indexPath)
        {
            long packageIndexSize = headers.RCPackageIndexSize;
            long packageContentSize = headers.RCPackageContentSize;
            if (packageIndexSize == 0 || packageContentSize == 0)
            {
                // This is an internal error that should not happen!
                return 0;
            }
            
            string packageFileName = requestHandler.PackageFileName;
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
                    requestHandler.Logger.Warn("problem unpacking: " + entry, e);
                    return unpackedBytes;
                }

                if (!Util.IsValidUri(currUri))
                {
                    requestHandler.Logger.Warn("problem unpacking (invalid uri): " + currUri);
                    return unpackedBytes;
                }

                // FIXME: a new RCRequest shouldn't be created just to know the cache file name.
                // The cache file name logic should be moved into another class, Util e.g.
                RCRequest rcRequest = new RCRequest(requestHandler, (HttpWebRequest)WebRequest.Create(currUri.Trim()));
                string cacheFileName = rcRequest.CacheFileName;

                unpackedBytes += currFileSize;

                if (!Util.IsNotTooLongFileName(cacheFileName))
                {
                    // We can't save the file
                    requestHandler.Logger.Warn("problem unpacking, filename too long for uri: " + currUri);
                    return unpackedBytes;
                }
                // make sure the file doesn't already exist for indexing purposes only
                FileInfo ftest = new FileInfo(cacheFileName);
                bool existed = ftest.Exists;

                // try to delete the old version
                if (!Util.DeleteFile(cacheFileName))
                {
                    return unpackedBytes;
                }

                // create directory if it doesn't exist
                if (!Util.CreateDirectoryForFile(cacheFileName))
                {
                    return unpackedBytes;
                }

                // create the file if it doesn't exist
                FileStream currFileFS = Util.CreateFile(cacheFileName);
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
                    requestHandler.Logger.Error("unexpected package size: " + rcRequest.CacheFileName +
                        "(" + bytesReadOfCurrFile + " / " + currFileSize + ")");
                    return unpackedBytes * -1;
                }

                currFileFS.Close();

                // add the file to Lucene
                if (Util.IsParseable(cacheFileName))
                {
                    if (!existed)
                    {
                        string document = Util.ReadFileAsString(cacheFileName);
                        string title = Util.GetPageTitle(document);
                        string content = Util.GetPageContent(document);
                        // XXX: Why always with "Content-Type: text/html" ???
                        IndexWrapper.IndexDocument(indexPath, "Content-Type: text/html", rcRequest.Uri, title, content);
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