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
        /// The results of a network usage measurement.
        /// </summary>
        public class NetworkUsageResults
        {
            private long _speedBs;
            private long _bytesDownloaded;
            private double _elapsedSeconds;

            /// <summary>
            /// Constructs a new results object.
            /// </summary>
            /// <param name="speedBs">The speed in bytes/second.</param>
            /// <param name="bytesDownloaded">The bytes downloaded.</param>
            /// <param name="elapsedSeconds">The time elapsed in seconds.</param>
            public NetworkUsageResults(long speedBs, long bytesDownloaded, double elapsedSeconds)
            {
                this._speedBs = speedBs;
                this._bytesDownloaded = bytesDownloaded;
                this._elapsedSeconds = elapsedSeconds;
            }
            /// <summary>
            /// The speed in bytes/second.
            /// </summary>
            public long SpeedBs
            {
                get { return _speedBs; }
            }
            /// <summary>
            /// The bytes downloaded.
            /// </summary>
            public long BytesDownloaded
            {
                get { return _bytesDownloaded; }
            }
            /// <summary>
            /// The time elapsed in seconds.
            /// </summary>
            public double ElapsedSeconds
            {
                get { return _elapsedSeconds; }
            }
        }

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
        /// <summary>
        /// After this time, a time and bytes are saved to create chunks, from which some
        /// are then ignored afterwards.
        /// </summary>
        private static int PART_SAVING_INTERVAL_MS = 1000;
        /// <summary>
        /// All chunks below that percentage of the max speed are ignored. (10 %)
        /// </summary>
        private static double PERCENT_IGNORE_THRESHOLD = 0.1;
        /// <summary>
        /// The default time to measure for asynchronous calls. 10 s.
        /// </summary>
        private static long MEASUREMENT_DEFAULT_MS = 10000;

        /// <summary>
        /// A delegate for callbacks that want to evaluate the measuring results.
        /// </summary>
        /// <param name="results">The results</param>
        public delegate void NetworkUsageDetectorDelegate(NetworkUsageResults results);

        /// <summary>
        /// The network interface used for the local IP address.
        /// </summary>
        private static NetworkInterface _networkInterface = Utils.GetNetworkInterfaceFor(HttpUtils.LOCAL_IP_ADDRESS);
        /// <summary>
        /// A stopwatch for measuring time. Also used as lock object.
        /// </summary>
        private static Stopwatch _stopwatch = new Stopwatch();
        /// <summary>
        /// The bytes received value of the network interface.
        /// </summary>
        private static long _bytesReceived;
        /// <summary>
        /// Whether we are currently measuring speed.
        /// </summary>
        private static bool _running;
        /// <summary>
        /// A timer used to call the callback function after measurement.
        /// </summary>
        private static Timer _timer;
        /// <summary>
        /// The callback status.
        /// </summary>
        private static CallBackStatus _callBackStatus;
        /// <summary>
        /// A timer used to save parts (chunks) in certain intervals.
        /// </summary>
        private static Timer _partSavingTimer;
        /// <summary>
        /// total time elapsed -> total bytes downloaded
        /// </summary>
        private static SortedList<double, long> _parts;

        /// <summary>
        /// Starts to measure, if it is not altready measuring.
        /// 
        /// The calling method should call GetMeasuringResults once, if and only if it gets true returned.
        /// </summary>
        /// <returns>True if measuring started, false if it was already running.</returns>
        public static bool StartMeasuringIfNotRunning()
        {
            // Lock
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
        /// <returns>The measurement resulta.</returns>
        public static NetworkUsageResults GetMeasuringResults()
        {
            List<double> timeIntervals = new List<double>();
            List<long> partSizes = new List<long>();
            long bytesDownloaded;
            double elapsedSeconds;

            // Lock as long as we access static fields.
            lock (_stopwatch)
            {
                // Save the last chunk
                SavePartOfMeasurement(null);
                // Stop measurement
                long bytesReceivedOld = _bytesReceived;
                Stop();
                bytesDownloaded = _bytesReceived - bytesReceivedOld;
                elapsedSeconds = _stopwatch.Elapsed.TotalSeconds;
                
                // Determine the elapsedTime and bytesDownloaded values for each part
                double lastElapsedTime = 0;
                long lastBytesDownloaded = 0;
                foreach (KeyValuePair<double, long> pair in _parts)
                {
                    timeIntervals.Add(pair.Key - lastElapsedTime);
                    partSizes.Add(pair.Value - lastBytesDownloaded);
                    lastElapsedTime = pair.Key;
                    lastBytesDownloaded = pair.Value;
                }
            }

            // Calculate the max. speed in the whole time
            List<double> speeds = new List<double>();
            for (int i = 0; i < timeIntervals.Count; i++)
            {
                speeds.Add(partSizes[i] / timeIntervals[i]);
            }
            double maxSpeed = speeds.Max();

            // Remove those below certain percentage of max
            for (int i = 0; i < timeIntervals.Count; i++)
            {
                bool taken = speeds[i] > (maxSpeed * PERCENT_IGNORE_THRESHOLD);
                // XXX Debug Console Write
                Console.WriteLine("Time = {0:0.0000}, Bytes = {1,7}, Speed = {2,10:0.00}, Considered = {3}",
                    timeIntervals[i], partSizes[i], speeds[i], taken);
                if (!taken)
                {
                    timeIntervals.RemoveAt(i);
                    partSizes.RemoveAt(i);
                    speeds.RemoveAt(i);
                    i--;
                }
            }

            // Calculate weighted average
            double weightedSum = 0;
            for (int i = 0; i < timeIntervals.Count; i++)
            {
                weightedSum += speeds[i] * timeIntervals[i];
            }
            double weightedAvg = weightedSum / timeIntervals.Sum();

            return new NetworkUsageResults((long)weightedAvg, bytesDownloaded, elapsedSeconds);
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
        public static bool StartMeasuringIfNotRunningWithCallback(NetworkUsageDetectorDelegate callbackMethod)
        {
            // Lock
            lock (_stopwatch)
            {
                if (_running)
                {
                    return false;
                }
                Start();

                _callBackStatus = CallBackStatus.INITIAL;
                // Just call the timer once!
                _timer = new Timer(Callback, callbackMethod, MEASUREMENT_DEFAULT_MS, Timeout.Infinite);

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
                    _timer.Change(MEASUREMENT_DEFAULT_MS, Timeout.Infinite);
                    break;
                case CallBackStatus.ABORTED:
                    // Just stop
                    lock (_stopwatch)
                    {
                        Stop();
                    }
                    break;
                case CallBackStatus.READY:
                    // Get the results.
                    NetworkUsageResults results = GetMeasuringResults();
                    // And call the callbackMethod.
                    NetworkUsageDetectorDelegate callbackMethod = o as NetworkUsageDetectorDelegate;
                    callbackMethod(results);
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
            _partSavingTimer = new Timer(SavePartOfMeasurement, null, PART_SAVING_INTERVAL_MS, PART_SAVING_INTERVAL_MS);
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
