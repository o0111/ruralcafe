using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace RuralCafe.Util
{
    /// <summary>
    /// Can detect the network usage.
    /// </summary>
    public static class NetworkUsageDetector
    {
        /// <summary>
        /// Enum for the callback status.
        /// </summary>
        private enum CallBackStatus
        {
            /// <summary>
            /// A callback was requested, but the request is not finished yet.
            /// </summary>
            INITIAL = 0,
            /// <summary>
            /// The request is finished, so the callback can be made.
            /// </summary>
            READY = 1,
            /// <summary>
            /// The request probably failed, so the callback must be omitted.
            /// </summary>
            ABORTED = 2
        }

        // Constants
        private static int PART_SAVING_INTERVAL_MS = 1000;

        /// <summary>
        /// A delegate for callbacks that want to evaluate the measuring results.
        /// </summary>
        /// <param name="speedBS">The average speed in byted per second.</param>
        /// <param name="bytesDownloaded">The total bytes downloaded while measuring.</param>
        public delegate void NetworkUsageDetectorDelegate(long speedBS, long bytesDownloaded);

        private static NetworkInterface _networkInterface = Utils.GetNetworkInterfaceFor(HttpUtils.LOCAL_IP_ADDRESS);
        private static Stopwatch _stopwatch = new Stopwatch();
        private static long _bytesReceived;
        private static bool _running;
        private static Timer _timer;
        private static int _timerWaitMS;
        private static CallBackStatus _callBackStatus;

        private static Timer _partSavingTimer;
        // total time elapsed -> total bytes downloaded
        private static SortedList<double, long> _parts;

        // TODO make the measuring save the per value seconds. Then take avg. of all, that are beyond threshold
        // Make threshold either fixed, or percent of overall avg. or percent of max.

        /// <summary>
        /// Starts to measure, if it is not altready measuring.
        /// 
        /// The calling method should call GetMeasuringResults once, if and only if it gets true returned.
        /// </summary>
        /// <returns>True if measuring started, false if it was already running.</returns>
        public static bool StartMeasuringIfNotRunning()
        {
            // We just need to lock for the starting methods, as long as every thread that gets true returned
            // calls exactly once GetMeasuringResults.
            lock (_stopwatch)
            {
                if (_running)
                {
                    return false;
                }
                Start();
                return true;
            }
        }

        /// <summary>
        /// Stops measuring and gets the result.
        /// </summary>
        /// <param name="bytesDownloaded">The bytes downloaded will be stored in here.</param>
        /// <returns>The speed in bytes per second on average.</returns>
        public static long GetMeasuringResults(out long bytesDownloadedTotal)
        {
            long bytesReceivedOld = _bytesReceived;
            Stop();
            bytesDownloadedTotal = _bytesReceived - bytesReceivedOld;

            //List<double> timeIntervals = new List<double>();
            //List<long> partSizes = new List<long>();
            //// Determine the values for each part
            //double lastElapsedTime = 0;
            //long lastBytesDownloaded = bytesReceivedOld;
            //foreach (KeyValuePair<double, long> pair in _parts)
            //{
            //    timeIntervals.Add(pair.Key - lastElapsedTime);
            //    partSizes.Add(pair.Value - lastBytesDownloaded);
            //    lastElapsedTime = pair.Key;
            //    lastBytesDownloaded = pair.Value;
            //}

            // Calculate the max. speed in the whole time


            // Remove those below certain percentage of max


            double elapsedSeconds = _stopwatch.Elapsed.TotalSeconds;
            
            double bytesPerSec = bytesDownloadedTotal / elapsedSeconds;
            return (long)bytesPerSec;
        }

        /// <summary>
        /// Starts to measure, if it is not altready measuring.
        /// 
        /// The calling method should call MarkReadyForCallback or AbortCallback once,
        /// if and only if it gets true returned.
        /// </summary>
        /// <param name="msToWait">The ms to wait until callback should be called.</param>
        /// <param name="callbackMethod">The callback method to be called.</param>
        /// <returns>True if measuring started, false if it was already running.</returns>
        public static bool StartMeasuringIfNotRunningWithCallback(int msToWait, 
            NetworkUsageDetectorDelegate callbackMethod)
        {
            // We just need to lock for the starting methods
            lock (_stopwatch)
            {
                if (_running)
                {
                    return false;
                }
                Start();

                _callBackStatus = CallBackStatus.INITIAL;
                _timerWaitMS = msToWait;
                // Just call the timer once!
                _timer = new Timer(Callback, callbackMethod, _timerWaitMS, Timeout.Infinite);

                return true;
            }
        }

        /// <summary>
        /// Tell the network usage detector that the request is ready and the callback can be done.
        /// By the next time the timer checks, the callback will be executed.
        /// </summary>
        public static void MarkReadyForCallback()
        {
            _callBackStatus = CallBackStatus.READY;
        }

        /// <summary>
        /// Tell the network usage detector that the request has failed and the callback should not be made.
        /// </summary>
        public static void AbortCallback()
        {
            _callBackStatus = CallBackStatus.ABORTED;
        }

        /// <summary>
        /// Internal callback method for the timer, that then calls the external callback.
        /// </summary>
        /// <param name="o">The external callback delegate.</param>
        private static void Callback(object o)
        {
            switch (_callBackStatus)
            {
                case CallBackStatus.INITIAL:
                    // Reschedule timer
                    _timer.Change(_timerWaitMS, Timeout.Infinite);
                    break;
                case CallBackStatus.ABORTED:
                    // Just stop
                    Stop();
                    break;
                case CallBackStatus.READY:
                    // Get the results.
                    long bytesDownloaded;
                    long speedBS = GetMeasuringResults(out bytesDownloaded);
                    // And call the callbackMethod.
                    NetworkUsageDetectorDelegate callbackMethod = o as NetworkUsageDetectorDelegate;
                    callbackMethod(speedBS, bytesDownloaded);
                    break;
            }
        }

        /// <summary>
        /// Saves a part of the measurement. The currently elapsed time and bytes received.
        /// </summary>
        /// <param name="o">Ignored.</param>
        private static void SavePartOfMeasurement(object o)
        {
            double elapsedTime = _stopwatch.Elapsed.TotalSeconds;
            long bytesDownloaded = _networkInterface.GetIPv4Statistics().BytesReceived - _bytesReceived;
            lock (_parts)
            {
                _parts[elapsedTime] = bytesDownloaded;
            }
        }

        /// <summary>
        /// Starts measuring.
        /// </summary>
        private static void Start()
        {
            _running = true;
            _stopwatch.Restart();
            _bytesReceived = _networkInterface.GetIPv4Statistics().BytesReceived;
            // Start the timer that saves the results each second
            _parts = new SortedList<double, long>();
            //_partSavingTimer = new Timer(SavePartOfMeasurement, null, PART_SAVING_INTERVAL_MS, PART_SAVING_INTERVAL_MS);
        }

        /// <summary>
        /// Stops measuring.
        /// </summary>
        private static void Stop()
        {
            // Stop the timer that saves the results each second
            _partSavingTimer.Change(Timeout.Infinite, Timeout.Infinite);

            _running = false;
            _stopwatch.Stop();
            _bytesReceived = _networkInterface.GetIPv4Statistics().BytesReceived;
        }
    }
}
