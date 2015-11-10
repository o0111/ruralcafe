# Installation and startup #

There are two different setup versions, one that requires an active internet connection to download dependencies and check for updates, and an offline version.

  * Running one of the two "setup.exe"s will also immediately start RuralCafe after the installation. To start it another time, use the Desktop icon or Start Menu entry.
  * To stop RuralCafe, just click the X or press CLTR+C, while the window is active in the foreground.

## Online version ##

  * Go to our publish server http://195.229.110.133/ftp/RuralCafe/ and download "setup.exe". You can do this via right click and "Save as".
  * Execute "setup.exe". This will install RuralCafe along with any other requirements including .NET.
  * RuralCafe will automatically check for updates, either on startup or in intervals, depending on the current deployment setting. If a new update is available, you will be asked if you want to download and install this update.

## Offline version ##

  * Go to our publish server http://195.229.110.133/ftp/RuralCafe/Offline and download the latest ZIP archive. You can do this via right click and "Save as". Unpack it.
  * Execute "setup.exe". This will install RuralCafe. If you have an internet connection, this will also intstall any other requirements including .NET. If you don't you have to download and install the requirements before. The requirements are:
    * On Windows XP at least Service Pack 2 or higher (Use Windows Updates to install this)
    * Windows Installer 4.5 ( http://www.microsoft.com/en-us/download/details.aspx?id=8483 )
    * .NET 4.0 or higher ( http://www.microsoft.com/en-us/download/details.aspx?id=17718 )
    * SQL Server Compact 4.0 SP1 ( http://www.microsoft.com/en-us/download/details.aspx?id=30709 )
  * RuralCafe will not check for updates. You must check this website for new versions, and download and install them manually.

# Configuration #

After starting a setting window will appear. See **[Configuration](Configuration.md)** for explanation on the settings.