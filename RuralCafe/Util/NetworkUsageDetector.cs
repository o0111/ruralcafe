using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Text;
using System.Threading;

namespace RuralCafe.Util
{
    public static class NetworkUsageDetector
    {

        private static NetworkInterface _networkInterface = Utils.GetNetworkInterfaceFor(HttpUtils.LOCAL_IP_ADDRESS);
        private static Stopwatch _stopwatch = new Stopwatch();
        private static long _bytesReceived;
        private static bool _running;

        // TODO make the measuring save the per value seconds. Then take avg. of all, that are beyond threshold
        // Make threshold either fixed, or percent of overall avg. or percent of max.
        public static bool StartMeasuringIfNotRunning()
        {
            lock (_stopwatch)
            {
                if (_running)
                {
                    return false;
                }
                _running = true;
                _stopwatch.Restart();
                _bytesReceived = _networkInterface.GetIPv4Statistics().BytesReceived;
                return true;
            }
        }

        public static long GetMeasuringResults(out long bytesDownloaded)
        {
            lock (_stopwatch)
            {
                _running = false;
                _stopwatch.Stop();
                long bytesReceivedNew = _networkInterface.GetIPv4Statistics().BytesReceived;
                double elapsedSeconds = _stopwatch.Elapsed.TotalSeconds;
                bytesDownloaded = bytesReceivedNew - _bytesReceived;
                double bytesPerSec = bytesDownloaded / elapsedSeconds;
                return (long)bytesPerSec;
            }
        }

        /// <summary>
        /// Determines the network usage. Do not use.
        /// </summary>
        public static void DetermineNetworkSpeed()
        {
            NetworkInterface nic = Utils.GetNetworkInterfaceFor(HttpUtils.LOCAL_IP_ADDRESS);

            if (nic == null)
            {
                // TODO
                return;
            }
            var reads = Enumerable.Empty<double>();
            Stopwatch sw = new Stopwatch();
            long lastBr = nic.GetIPv4Statistics().BytesReceived;
            for (int i = 0; i < 1000; i++)
            {

                sw.Restart();
                Thread.Sleep(100);
                double elapsed = sw.Elapsed.TotalSeconds;
                long br = nic.GetIPv4Statistics().BytesReceived;

                double local = (br - lastBr) / elapsed;
                lastBr = br;

                // Keep last 20, ~2 seconds
                reads = new[] { local }.Concat(reads).Take(20);

                if (i % 10 == 0)
                { // ~1 second
                    double bSec = reads.Sum() / reads.Count();
                    double kbs = (bSec * 8) / 1024;
                    Console.WriteLine("Kb/s ~ " + kbs);
                }
            }
        }
    }
}
