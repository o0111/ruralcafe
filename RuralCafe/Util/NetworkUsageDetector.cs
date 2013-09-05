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
    public class NetworkUsageDetector
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

        private class MeasuringChunk
        {
            public double timeInterval;
            public long partSize;
            public double speed;

            public MeasuringChunk(double timeInterval, long partSize)
            {
                this.timeInterval = timeInterval;
                this.partSize = partSize;
                this.speed = partSize / timeInterval;
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
        /// Only the best (fastest) n % of the chunks are considered. With 10 % for 10 s this means only 1 chunk.
        /// </summary>
        private static double BEST_N_PERCENT = 0.2;
        /// <summary>
        /// The default time to measure for calls. 10 s.
        /// </summary>
        private static TimeSpan MEASUREMENT_DEFAULT_MS = new TimeSpan(0, 0, 10);

        #region static

        /// <summary>
        /// The network interface used for the local IP address.
        /// </summary>
        private static NetworkInterface _networkInterface = Utils.GetNetworkInterfaceFor(HttpUtils.LOCAL_IP_ADDRESS);

        /// <summary>
        /// The proxy where to integrate the measuring results
        /// </summary>
        private static RCLocalProxy _proxy;

        /// <summary>
        /// The number of downloads currently running.
        /// </summary>
        private static int _downloadsRunning = 0;

        /// <summary>
        /// If we're currently measuring.
        /// </summary>
        private static bool _currentlyMeasuring = false;

        /// <summary>
        /// The lock object.
        /// </summary>
        private static object _lockObj = new object();

        /// <summary>
        /// Initializes the static part by setting the proxy.
        /// </summary>
        /// <param name="proxy">The proxy.</param>
        public static void Initialize(RCLocalProxy proxy)
        {
            _proxy = proxy;
        }

        /// <summary>
        /// Declares a download is starting. If this is the first at the moment, measuring will begin.
        /// </summary>
        public static void DownloadStarted()
        {
            int value = System.Threading.Interlocked.Increment(ref _downloadsRunning);
            if(value == 1)
            {
                // There was no download running before.
                StartNewMeasurementIfNotRunning();
            }
        }

        /// <summary>
        /// Declares a download has stopped. If this was the last, measuring will not be restarted after the current period.
        /// </summary>
        public static void DownloadStopped()
        {
            System.Threading.Interlocked.Decrement(ref _downloadsRunning);
        }

        /// <summary>
        /// Starts a new Thread measuring, if there is none running currently.
        /// </summary>
        private static void StartNewMeasurementIfNotRunning()
        {
            bool start = false;
            lock(_lockObj)
            {
                if(!_currentlyMeasuring)
                {
                    start = true;
                    _currentlyMeasuring = true;
                }
            }
            if(start)
            {
                NetworkUsageDetector newDetector = new NetworkUsageDetector();
                // Start in an own thread.
                new Thread(newDetector.Measure).Start();
            }
        }

        #endregion
        #region instance

        /// <summary>
        /// A timer used to save parts (chunks) in certain intervals.
        /// </summary>
        private Timer _partSavingTimer;
        /// <summary>
        /// total time elapsed -> total bytes downloaded
        /// </summary>
        private SortedList<double, long> _parts = new SortedList<double, long>();
        /// <summary>
        /// A stopwatch for measuring time. Also used as lock object.
        /// </summary>
        private Stopwatch _stopwatch = new Stopwatch();
        /// <summary>
        /// The bytes received value of the network interface.
        /// </summary>
        private long _bytesReceived;

        /// <summary>
        /// Measures for 10s and saves the results.
        /// </summary>
        private void Measure()
        {
            // Start
            _stopwatch.Restart();
            _bytesReceived = _networkInterface.GetIPv4Statistics().BytesReceived;
            // Start the timer that saves the results each second
            _partSavingTimer = new Timer(SavePartOfMeasurement, null, PART_SAVING_INTERVAL_MS, PART_SAVING_INTERVAL_MS);

            // Sleep 10 s
            Thread.Sleep(MEASUREMENT_DEFAULT_MS);

            // Stop
            _partSavingTimer.Change(Timeout.Infinite, Timeout.Infinite);
            // Save last part
            SavePartOfMeasurement(null);
            _stopwatch.Stop();

            // Save results
            _proxy.IncludeDownloadInCalculation(GetMeasuringResults());

            // Declare we're not measuring any more
            lock(_lockObj)
            {
                _currentlyMeasuring = false;
            }
            // Restart if necessary
            if(_downloadsRunning > 0)
            {
                StartNewMeasurementIfNotRunning();
            }
        }

        /// <summary>
        /// Saves a part of the measurement. The currently elapsed time and bytes received.
        /// </summary>
        /// <param name="o">Ignored.</param>
        private void SavePartOfMeasurement(object o)
        {
            double elapsedTime = _stopwatch.Elapsed.TotalSeconds;
            long bytesDownloaded = _networkInterface.GetIPv4Statistics().BytesReceived - _bytesReceived;
            lock (_parts)
            {
                _parts[elapsedTime] = bytesDownloaded;
            }
        }

        /// <summary>
        /// Gets the results.
        /// </summary>
        /// <returns>The measurement results.</returns>
        private NetworkUsageResults GetMeasuringResults()
        {
            List<MeasuringChunk> chunks = new List<MeasuringChunk>();
            double elapsedSeconds = _parts.Last().Key;
            long bytesDownloaded = _parts.Last().Value;

            // Determine the elapsedTime and bytesDownloaded values for each part
            double lastElapsedTime = 0;
            long lastBytesDownloaded = 0;
            foreach (KeyValuePair<double, long> pair in _parts)
            {
                MeasuringChunk chunk = new MeasuringChunk(pair.Key - lastElapsedTime, pair.Value - lastBytesDownloaded);
                chunks.Add(chunk);
                lastElapsedTime = pair.Key;
                lastBytesDownloaded = pair.Value;
                // XXX Debug Console Write
                Console.WriteLine("Time = {0:0.0000}, Bytes = {1,7}, Speed = {2,10:0.00}",
                    chunk.timeInterval, chunk.partSize, chunk.speed);
            }

            // Sort the chunks by speed, descending
            chunks.Sort((a, b) => a.speed > b.speed ? -1 : (a.speed == b.speed ? 0 : 1));

            // Get the index to which we must consider.
            int toIndex = (int)(chunks.Count * BEST_N_PERCENT);
            // We always want to consider at least 1!
            if (toIndex == 0)
            {
                toIndex = 1;
            }

            // Calculate weighted average
            double weightedSum = 0;
            for (int i = 0; i < toIndex; i++)
            {
                weightedSum += chunks[i].speed * chunks[i].timeInterval;
            }
            double weightedAvg = weightedSum / chunks.Take(toIndex).Sum(x => x.timeInterval);

            return new NetworkUsageResults((long)weightedAvg, bytesDownloaded, elapsedSeconds);
        }

        #endregion
    }
}
