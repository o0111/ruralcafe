# DNS caching #

Windows has DNS caching implemented, which is being used by .NET and therefore by Ruralcafe. The cache size is rather small and pages get evicted quickly. In order to change this behaviour, do the following:
  1. Type `regedit` in the start menu (after clicking `Run` in Windows XP).
  1. Locate and then expand the following registry subkey: `HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\Dnscache\Parameters`
  1. To create a new item, click `Edit->New->DWORD (32-bit) Value`. After naming it, double click it change its value. Always select `Decimal` base. The items to create depend on your operating system.
  1. Windows 2000/Vista/7
    * `CacheHashTableBucketSize = 1`
    * `CacheHashTableSize = 64000`
    * `MaxCacheEntryTtlLimit = 604800`
    * `MaxNegativeCacheTtl = 300`
  1. Windows XP
    * `MaxCacheTtl = 604800`
    * `MaxNegativeCacheTtl = 300`
  1. Type `cmd` in the start menu (after clicking `Run` in Windows XP).
  1. Type `ipconfig /flushdns` into the command prompt.
  1. Restart the computer.

Of course you can change the values according to your needs. The `Ttl`s (time to live) are specified in seconds.