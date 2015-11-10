# Installation and startup #

  * Install .NET 4.0 or higher and Visual Studio 2012 or newer.
  * Check out the source code from https://ruralcafe.googlecode.com/svn/
  * Open `trunk/RuralCafe.sln` with Visual Studio.
  * Click Start (Debug). If this prompts you to grant RuralCafe administrator rights, deny and instead restart Visual Studio with administrator rights (Right click, "Run as administrator"). Otherwise you won't be able to debug.

# Configuration #

After starting a setting window will appear. See **[Configuration](Configuration.md)** for explanation on the settings.

To add new settings or change the default settings, you must use Visual Studio. Right click the RuralCafe project in the Solution Explorer and select "Properties". Then select "Setting". Here you can add or modify default settings.

# Publishing #

To publish the most recent version of RuralCafe, use Visual Studio. Right click on the RuralCafe project in the Solution Explorer on the right and choose "Properties". Navigate to "Publish". Scroll down and enter the current revision under "Publish Version". Click "Publish Now". This will publish to a ftp server, you need to know the credentials.

Whenever you add a new file you want to be copied to the output directory (you usually want this for Trotro pages, otherwise the UI might not work for the published version), you must configure Visual Studio to do so. For this left click the file in the Solution Explorer and in the properties below choose "Copy if newer" for "Copy to output directory". Sadly you cannot do this for whole folders.

# Bugs #

Please help us by sending in bug reports along with your operating system, configuration, a description of the physical deployment scenario, and a description of the bug.