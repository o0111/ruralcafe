# Configuration #

If you are running the local proxy and the remote proxy on different machines, you might need to disable or configure your firewall to accept in/outcoming requests on that port for RuralCafe and/or Visual Studio. If you are using the Windows Firewall, see **WindowsFirewall** for an explanation how to configure it correctly.

To interact with RuralCafe, the browser must be configured to use RuralCafe as the proxy using the IP address and port of the local proxy set in the configuration (below). The  local proxy port should be used as the HTTP port, and usually 8443 as HTTPS or SSL port. These settings can be found in the connection settings of any modern browser (IE, Firefox, Chrome). Also, set the browser's homepage to http://www.ruralcafe.net/

The configuration fields are fairly straightforward and are defaulted to work as a standalone service on a single machine. In the case where the local and remote proxies are setup on seperate machines, the configuration should be identical on both machines.

## Basic Configuration ##

  * Local Proxy IP address: Set to "127.0.0.1", if you want to run the local proxy on this machine or both proxies on this machine. If you only want to run the remote proxy on this machine, delete the contents.
  * Remote Proxy IP address: Set to "127.0.0.1", if you want to run the remote proxy on this machine or both proxies on this machine. If you only want to run the local proxy on this machine, delete the contents.

## Advanced Configuration ##
### Local Proxy ###
The local proxy settings are where RuralCafe listens to requests from the browser, these are the IP address (basic configuration) and port settings that the browser should be configured with to use RuralCafe.

  * Port: The port that the local proxy listens on for HTTP requests.
  * Cache path: If you want the cache to be in a different location or have a cache (in a different location) already and want to use that, you can adjust the path here. Both absolute paths and relative paths to `<BaseDirectory>\LocalProxy` are accepted.
  * Local Max cache size (MiB): The maximum size the local cache should ocupy on your hard drive in MiB.
  * Default search page: Can at the moment either be "trotro.html" or "cip.html". CIP is deprecated and should not be used any more. It was to use RuralCafe in a completely disconnected fashion as an information portal.
  * Index path: If you want the index to be in a different location, you can adjust the path here. Both absolute paths and relative paths to `<BaseDirectory>\LocalProxy` are accepted.
  * Wiki dump file: If a local copy of Wikipedia is available, this may be set to point to it. Both absolute paths and relative paths to `<BaseDirectory>\LocalProxy` are accepted. Note that to be able to use this wikipedia image, it must first be indexed by BzReader (see below).
  * Detect network status: If checked, the system will switch the network status automatically between ONLINE, SLOW and OFFLINE, depending on your current network speed. Otherwise you can set a fixed network status.
  * Network status: Set a fixed network status, if the system should not detect it automatically.
    * OFFLINE means you can only browse the cache contents, but cannot access any online webpages.
    * SLOW means you can access online pages, but with an asynchronous queue, which may be useful for bad internet connections.
    * ONLINE means you can access any page directly and Rural Cafe will just work as a transparent proxy.
  * Base directory: All dynamically changing files will be stored here: The cache, index files, pages the crawler downloads, etc. It should be on a drive with a lot of free space, at least as much as the sum of the above two Max cache sizes.
  * Force login: If you check this, users will be forced to login, even to browse the cache and make search queries. They can still access cache pages through the URL or bookmarks, though. In any case, users have to login once they want to download pages for SLOW network status.
  * Show survey: If you check this, users will be shown a satusfaction survey in each surf session, but not more than once a day. This is only useful, if you want to collect data about their behaviour and satisfaction.
  * Edit blacklist: Opens the default text editor to edit the blacklist file. All domains you enter there will be blocked by the local proxy.

### Remote Proxy ###
The remote proxy settings are where RuralCafe's remote proxy listens to requests from the local proxy. These settings are also used by the local proxy to know where to forward requests to the remote proxy.

  * Port: The port that the remote proxy listens on for HTTP requests.
  * Cache path: If you want the cache to be in a different location or have a cache (in a different location) already and want to use that, you can adjust the path here. Both absolute paths and relative paths to`<BaseDirectory>\RemoteProxy` are accepted.
  * Remote Max cache size (MiB): The maximum size the remote cache should ocupy on your hard drive in MiB.
  * Default depth: A setting of 1 means that only the page requested (and its embedded objects) are downloaded by the remote proxy for SLOW network status. Setting it to 2 makes it also download pages linked in that page, and so on. Settings higher than 1 have a strong performance impact.
  * Max. download speed: You can define a maximum download speed for test purposes here.
  * Default quota: Each request is granted this size for download. Decrease, to decrease the number of elements downloaded for each pages. Note that for bigger pages there will be elements missing then.
  * Default richness: The default richness of downloaded pages. Each user can adjust this personally for the current session.
    * Normal: All embedded objects are downloaded.
    * Low: Images, videos and audio files are not downloaded. This can increase performance.
  * Edit blacklist: Opens the default text editor to edit the blacklist file. All domains you enter there will be blocked by the remote proxy.


### Gateway Proxy ###
The gateway proxy settings are used by the remote proxy to connect to the Internet if there is another upstream proxy in the network. This is not yet fully supported, so expect strange behaviour for non-default values.

  * IP address: The IP address of the gateway proxy.
  * Port: The por of the gateway proxy.
  * Username: The username for authentication at the gateway proxy.
  * Password: The password for authentication at the gateway proxy.


### Shared ###

  * Log level: By adjusting this, you set how much information will be printed in the Console Window and in the log files. For developers, setting it to DEBUG is essential. For users INFO or WARN may be appropriate.

### System Settings ###

  * DNS cache TTL (seconds): The windows DNS cache settings will be adjusted and this is the TimeToLive for positive DNS entries. Only change this, if you know, what you are doing.

# Wikipedia Indexing #

You can download wikipedia dumps here: http://en.wikipedia.org/wiki/Wikipedia:Database_download#English-language_Wikipedia
I recommend, if you have a good Internet connection, using the most recent torrent, as this will be way faster than downloaing it from Wikipedia directly.

Prior to being able to use a wikipedia image dump from wikipedia.org, the image dump must be first indexed. To do this, install BzReader using the `BzReader.v1.0.13.msi` in the repository in the BzReader folder. Start BzReader and open the downloaded dump. The indexing can take several hours.