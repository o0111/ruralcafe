# Windows Firewall settings #

Follow these steps:
  * Go to the Control Panel.
  * Click "Windows Firewall".
  * Click "Advanced Settings".
  * Select "Inbound Rules" on the left.
  * Click "New rule..." on the right.
  * Select "Port" and click "Next".
  * Leave "TCP" and type in the ports used in the "Specific local ports" field. E.g. "8080, 8081, 8443, 8444". Click "Next".
  * Leave "Allow the connection" and click "Next".
  * Adjust the networks allowed to your case. If your system represents a remote proxy that should be accessible through the internet, "Public" must bi ticked. If your system represents a local proxy that should only be accessible from the local network, you should untick "Public". "Domain" and "Private" should always be ticket. Click "Next".
  * Type a name, e.g. "RuralCafe Ports" and click "Finish"