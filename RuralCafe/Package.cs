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
using System.IO.Compression;
using System.Net;
using RuralCafe.Lucenenet;
using RuralCafe.Util;
using System.Collections.Specialized;
using Newtonsoft.Json;
using RuralCafe.Json;
using RuralCafe.Database;

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
        private string _packageFileName;

        /// <summary>
        /// Constructor for a RuralCafe package.
        /// </summary>
        public Package()
        {
            this._rcRequests = new LinkedList<RCRequest>();
            this._contentSize = 0;
            this._indexSize = 0;
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
        /// The package file name.
        /// </summary>
        public string PackageFileName
        {
            get { return _packageFileName; }
        }

        /// <summary>
        /// Adds a RCRequest to the package given the quota limitation.
        /// Only called by the remote proxy.
        /// XXX: quota is currently simple algorithm.
        /// XXX: should be changed to check for compressed size rather than actual size.
        /// </summary>
        /// <param name="requestHandler">Calling handler for this method.</param>
        /// <param name="requestObject">RCRequest to add.</param>
        /// <param name="quota">Quota limit.</param>
        /// <returns>True iff the request has been packed successfully.</returns>
        public bool Pack(RemoteRequestHandler requestHandler, RCRequest requestObject, ref long quota)
        {
            if (_rcRequests.Contains(requestObject))
            {
                requestHandler.Logger.Debug("object exists in package: " + requestObject.Uri);
                return false;
            }

            requestObject.FileSize = Utils.GetFileSize(requestObject.CacheFileName);
            if (requestObject.FileSize <= 0)
            {
                return false;
            }

            // quota check
            if ((quota - requestObject.FileSize) < 0)
            {
                requestHandler.Logger.Debug("object doesn't fit in quota: " + requestObject.Uri);
                return false;
            }

            _rcRequests.AddLast(requestObject);
            quota -= requestObject.FileSize;

            return true;
        }
        
        /// <summary>
        /// Adds all the requests to the package.
        /// </summary>
        /// <param name="requestHandler">The request Handler</param>
        /// <param name="childObjects">The requests to add.</param>
        /// <param name="quota">The remaining quota.</param>
        /// <returns>The added requests.</returns>
        public LinkedList<RCRequest> Pack(RemoteRequestHandler requestHandler, LinkedList<RCRequest> childObjects, ref long quota)
        {
            LinkedList<RCRequest> addedObjects = new LinkedList<RCRequest>();
            // add files that were completed to the package
            foreach (RCRequest childObject in childObjects)
            {
                // add to the package
                if (Pack(requestHandler, childObject, ref quota))
                {
                    addedObjects.AddLast(childObject);
                }
            }
            
            return addedObjects;
        }

        /// <summary>
        /// Combines all of the URIs in the package into a package index file.
        /// 
        /// Throws an exception if anything goes wrong.
        /// </summary>
        /// <param name="packageFileName">The filename of the package file to create.</param>
        /// <param name="cacheManager">The cache manager to retrieve DB data.</param>
        public void BuildPackageIndex(string packageFileName, CacheManager cacheManager)
        {
            _packageFileName = packageFileName;
            _contentSize = 0;
            if (!Utils.CreateDirectoryForFile(_packageFileName))
            {
                throw new IOException("Could not create directory for package file.");
            }
            if (!Utils.DeleteFile(_packageFileName))
            {
                throw new IOException("Could not create delete old package file.");
            }

            using (TextWriter tw = new StreamWriter(_packageFileName))
            {
                // create the package index file
                foreach (RCRequest rcRequest in _rcRequests)
                {
                    GlobalCacheItem cacheItem = cacheManager.GetGlobalCacheItem(rcRequest.GenericWebRequest.Method,
                        rcRequest.Uri);
                    if (cacheItem == null)
                    {
                        // This should generally not happen.
                        // XXX: A warning log message would be good here.
                        continue;
                    }

                    // Index format is 2 lines:
                    // <httpMethod> <statusCode> <fileSize> <URL> (Url is last, as it can have spaces)
                    // <headers>
                    tw.WriteLine(String.Format("{0} {1} {2} {3}",
                        cacheItem.httpMethod, (short)cacheItem.statusCode,
                        rcRequest.FileSize, rcRequest.Uri));
                    tw.WriteLine(cacheItem.responseHeaders);

                    _contentSize += rcRequest.FileSize;
                }
            }

            // calculate the index size
            _indexSize = Utils.GetFileSize(_packageFileName);
            if (_indexSize < 0)
            {
                throw new IOException("Problem getting file info for package file.");
            }
        }

        /// <summary>
        /// Unpacks the package contents and indexes them.
        /// </summary>
        /// <param name="requestHandler">Calling handler for this method.</param>
        /// <param name="rcheaders">The rc specific headers.</param>
        /// <param name="indexWrapper">The index wrapper.</param>
        /// <returns>Total unpacked content size.</returns>
        public static long Unpack(LocalRequestHandler requestHandler, RCSpecificResponseHeaders rcheaders,
            IndexWrapper indexWrapper)
        {
            long packageIndexSize = rcheaders.RCPackageIndexSize;
            long packageContentSize = rcheaders.RCPackageContentSize;
            if (packageIndexSize == 0 || packageContentSize == 0)
            {
                // This is an internal error that should not happen!
                requestHandler.Logger.Warn("problem unpacking: package index or content size is 0.");
                return 0;
            }
            
            string packageFileName = requestHandler.PackageFileName;
            string unpackedPackageFileName = packageFileName.Replace(".gzip", "");

            GZipWrapper.GZipDecompress(packageFileName, unpackedPackageFileName, packageIndexSize + packageContentSize);
            FileStream packageFs = new FileStream(unpackedPackageFileName, FileMode.Open);

            // read the package index
            Byte[] packageIndexBuffer = new Byte[packageIndexSize];
            int bytesOfIndexRead = 0;
            while (bytesOfIndexRead != packageIndexSize)
            {
                int read = packageFs.Read(packageIndexBuffer,
                    bytesOfIndexRead, (int)(packageIndexSize - bytesOfIndexRead));
                if(read == 0)
                {
                    // This should not happen
                    requestHandler.Logger.Warn("problem unpacking: could not read index.");
                    return 0;
                }
                bytesOfIndexRead += read;
            }

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

            for (int i = 0; i < packageContentArr.Length; i += 2)
            {
                // Index format is 2 lines:
                // <httpMethod> <statusCode> <fileSize> <URL> (Url is last, as it can have spaces)
                // <headers> (JSON)
                string[] firstLineArray = packageContentArr[i].Split(new string[]{" "}, 4, StringSplitOptions.None);

                if (firstLineArray.Length != 4)
                {
                    requestHandler.Logger.Error("unparseable entry: " + packageContentArr[i]);
                    return unpackedBytes;
                }
                string httpMethod = firstLineArray[0];
                short statusCode;
                long currFileSize;
                try
                {
                    statusCode = Int16.Parse(firstLineArray[1]);
                    currFileSize = Int64.Parse(firstLineArray[2]);
                }
                catch (Exception e)
                {
                    requestHandler.Logger.Warn("problem unpacking: " + packageContentArr[i], e);
                    return unpackedBytes;
                }

                string currUri = firstLineArray[3];

                string headersJson = packageContentArr[i + 1];
                NameValueCollection headers = JsonConvert.DeserializeObject<NameValueCollection>(headersJson,
                    new NameValueCollectionConverter());

                if (!HttpUtils.IsValidUri(currUri))
                {
                    requestHandler.Logger.Warn("problem unpacking (invalid uri): " + currUri);
                    return unpackedBytes;
                }

                string cacheFileName = requestHandler.Proxy.CachePath +
                    CacheManager.GetRelativeCacheFileName(currUri, httpMethod);

                unpackedBytes += currFileSize;

                if (!Utils.IsNotTooLongFileName(cacheFileName))
                {
                    // We can't save the file
                    requestHandler.Logger.Warn("problem unpacking, filename too long for uri: " + currUri);
                    return unpackedBytes;
                }
                // make sure the file doesn't already exist for indexing and DB purposes
                bool existed = requestHandler.Proxy.ProxyCacheManager.IsCached(httpMethod, currUri);
                if (existed)
                {
                    // Remove if it existed.
                    // FIXME do a replace instead!!! Otherwise the rc data would be lost.
                    requestHandler.Proxy.ProxyCacheManager.RemoveCacheItem(httpMethod, currUri);
                }
                
                // create the file if it doesn't exist
                FileStream currFileFS = Utils.CreateFile(cacheFileName);
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
                    bytesRead = packageFs.Read(buffer, 0, buffer.Length);
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

                        bytesRead = packageFs.Read(buffer, 0, buffer.Length);
                    }
                }

                if (bytesReadOfCurrFile != currFileSize)
                {
                    // ran out of bytes for this file
                    requestHandler.Logger.Error("unexpected package size: " + cacheFileName +
                        "(" + bytesReadOfCurrFile + " / " + currFileSize + ")");
                    return -1;
                }

                currFileFS.Close();

                // Add Database entry
                if (!requestHandler.Proxy.ProxyCacheManager.AddCacheItemForExistingFile(currUri, httpMethod,
                    headers, statusCode))
                {
                    // Adding to the DB failed
                    // Clean up: delete file and return (do not add to lucene)
                    Utils.DeleteFile(cacheFileName);
                    return -1;
                }

                // add the file to Lucene, if it is a text or HTML file
                // We have made sure the content-type header is always present in the DB!
                if (headers["Content-Type"].Contains("text/html") || headers["Content-Type"].Contains("text/plain"))
                {
                    if (!existed)
                    {
                        // XXX reading the file we just wrote. Could also stream it in
                        // local variable, that would be faster
                        string document = Utils.ReadFileAsString(cacheFileName);
                        string title = HtmlUtils.GetPageTitleFromHTML(document);

                        // Use whole document, so we can also find results with tags, etc.
                        indexWrapper.IndexDocument(currUri, title, document);
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