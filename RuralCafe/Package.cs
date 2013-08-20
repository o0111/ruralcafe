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
        private List<RCRequest> _rcRequests;
        private long _contentSize;
        private long _indexSize;
        private string _packageFileName;

        /// <summary>
        /// Constructor for a RuralCafe package.
        /// </summary>
        public Package()
        {
            this._rcRequests = new List<RCRequest>();
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
        public List<RCRequest> RCRequests
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

            _rcRequests.Add(requestObject);
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
                List<GlobalCacheItem> items = cacheManager.GetGlobalCacheItemsAsRequests(_rcRequests);

                // create the package index file
                foreach (GlobalCacheItem cacheItem in items)
                {
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
                        cacheItem.filesize, cacheItem.url));
                    tw.WriteLine(cacheItem.responseHeaders);

                    _contentSize += cacheItem.filesize;
                }
            }

            // calculate the index size
            _indexSize = Utils.GetFileSize(_packageFileName);
            if (_indexSize <= 0)
            {
                throw new IOException("Problem getting file info for package file.");
            }
        }

        /// <summary>
        /// Unpacks the package contents and indexes them.
        /// </summary>
        /// <param name="requestHandler">Calling handler for this method.</param>
        /// <param name="rcheaders">The rc specific headers.</param>
        /// <returns>Total unpacked content size.</returns>
        public static long Unpack(LocalRequestHandler requestHandler, RCSpecificResponseHeaders rcheaders)
        {
            long packageIndexSize = rcheaders.RCPackageIndexSize;
            long packageContentSize = rcheaders.RCPackageContentSize;
            if (packageIndexSize == 0 || packageContentSize == 0)
            {
                // This is an internal error that should not happen!
                requestHandler.Logger.Warn("problem unpacking: package index or content size is 0.");
                return -1;
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
                    return -1;
                }
                bytesOfIndexRead += read;
            }

            // split the big package file into pieces
            string[] stringSeparator = new string[] { "\r\n" };
            System.Text.UTF8Encoding enc = new System.Text.UTF8Encoding();
            string package = enc.GetString(packageIndexBuffer);
            string[] packageContentArr = package.Split(stringSeparator, StringSplitOptions.RemoveEmptyEntries);

            long unpackedBytes = 0;
            HashSet<GlobalCacheItemToAdd> itemsToAdd = new HashSet<GlobalCacheItemToAdd>();

            try
            {
                for (int i = 0; i < packageContentArr.Length; i += 2)
                {
                    // Index format is 2 lines:
                    // <httpMethod> <statusCode> <fileSize> <URL> (Url is last, as it can have spaces)
                    // <headers> (JSON)
                    string[] firstLineArray = packageContentArr[i].Split(new string[] { " " }, 4, StringSplitOptions.None);

                    if (firstLineArray.Length != 4)
                    {
                        requestHandler.Logger.Error("unparseable entry: " + packageContentArr[i]);
                        return -1;
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
                        return -1;
                    }

                    string currUri = firstLineArray[3];

                    string headersJson = packageContentArr[i + 1];
                    NameValueCollection headers = JsonConvert.DeserializeObject<NameValueCollection>(headersJson,
                        new NameValueCollectionConverter());

                    if (!HttpUtils.IsValidUri(currUri))
                    {
                        requestHandler.Logger.Warn("problem unpacking (invalid uri): " + currUri);
                        return -1;
                    }

                    try
                    {
                        if (requestHandler.Proxy.ProxyCacheManager.CreateOrUpdateFileAndWrite(httpMethod, currUri,
                            currFileSize, packageFs))
                        {
                            unpackedBytes += currFileSize;

                            GlobalCacheItemToAdd newItem = new GlobalCacheItemToAdd();
                            newItem.url = currUri;
                            newItem.httpMethod = httpMethod;
                            newItem.headers = headers;
                            newItem.statusCode = statusCode;

                            itemsToAdd.Add(newItem);
                        }
                    }
                    catch (Exception e)
                    {
                        requestHandler.Logger.Warn("Problem unpacking: ", e);
                        return -1;
                    }
                }
            }
            finally
            {
                if (packageFs != null)
                {
                    packageFs.Close();
                }
                // Add all Database entries
                if (!requestHandler.Proxy.ProxyCacheManager.AddCacheItemsForExistingFiles(itemsToAdd))
                {
                    // Adding to the DB failed
                    unpackedBytes = - 1;
                }
            }

            return unpackedBytes;
        }
    }
}