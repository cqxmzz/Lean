/*
 * QUANTCONNECT.COM - Democratizing Finance, Empowering Individuals.
 * Lean Algorithmic Trading Engine v2.0. Copyright 2014 QuantConnect Corporation.
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
*/

using System;
using System.IO;
using System.Linq;
using System.Text;
using QuantConnect.Util;
using System.Globalization;
using QuantConnect.Logging;
using System.Threading.Tasks;
using QuantConnect.Interfaces;
using QuantConnect.Securities;
using System.Collections.Generic;
using QuantConnect.Data.Market;
using static QuantConnect.StringExtensions;

namespace QuantConnect.Data
{
    /// <summary>
    /// Data writer for saving an IEnumerable of BaseData into the LEAN data directory.
    /// </summary>
    public class CustomDataWriter
    {
        private static string _customFolderName = "custom";
        private static KeyStringSynchronizer _keySynchronizer = new();
        private readonly Symbol _symbol;
        private readonly string _dataDirectory;
        private readonly Resolution _resolution;
        private readonly WritePolicy _writePolicy;
        private readonly SecurityType _securityType;
        private readonly IDataCacheProvider _dataCacheProvider;

        /// <summary>
        /// Create a new lean data writer to this base data directory.
        /// </summary>
        /// <param name="symbol">Symbol string</param>
        /// <param name="dataDirectory">Base data directory</param>
        /// <param name="resolution">Resolution of the desired output data</param>
        /// <param name="dataCacheProvider">The data cache provider to use</param>
        /// <param name="writePolicy">The file write policy to use</param>
        public CustomDataWriter(Resolution resolution, Symbol symbol, string dataDirectory,
            IDataCacheProvider dataCacheProvider = null, WritePolicy? writePolicy = null) : this(
            dataDirectory,
            resolution,
            symbol.ID.SecurityType,
            dataCacheProvider,
            writePolicy
        )
        {
            _symbol = symbol;
            if (_securityType != SecurityType.Base)
            {
                throw new NotImplementedException("Sorry this security type is not yet supported by the Custom data writer: " + _securityType);
            }
        }

        /// <summary>
        /// Create a new lean data writer to this base data directory.
        /// </summary>
        /// <param name="dataDirectory">Base data directory</param>
        /// <param name="resolution">Resolution of the desired output data</param>
        /// <param name="securityType">The security type</param>
        /// <param name="dataCacheProvider">The data cache provider to use</param>
        /// <param name="writePolicy">The file write policy to use</param>
        public CustomDataWriter(string dataDirectory, Resolution resolution, SecurityType securityType,
            IDataCacheProvider dataCacheProvider = null, WritePolicy? writePolicy = null)
        {
            _dataDirectory = dataDirectory;
            _resolution = resolution;
            _securityType = securityType;
            if (writePolicy == null)
            {
                _writePolicy = resolution >= Resolution.Hour ? WritePolicy.Merge : WritePolicy.Overwrite;
            }
            else
            {
                _writePolicy = writePolicy.Value;
            }
            _dataCacheProvider = dataCacheProvider ?? new DiskDataCacheProvider();
        }

        /// <summary>
        /// Given the constructor parameters, write out the data in LEAN format.
        /// </summary>
        /// <param name="source">IEnumerable source of the data: sorted from oldest to newest.</param>
        public void Write(IEnumerable<BaseData> source)
        {
            var lastTime = DateTime.MinValue;
            var outputFile = string.Empty;
            Symbol symbol = null;
            var currentFileData = new List<TimedLine>();
            var writeTasks = new Queue<Task>();

            foreach (var data in source)
            {
                // Ensure the data is sorted as a safety check
                if (data.Time < lastTime) throw new Exception("The data must be pre-sorted from oldest to newest");

                // Update our output file
                // Only do this on date change, because we know we don't have a any data zips smaller than a day, saves time
                if (data.Time.Date != lastTime.Date)
                {
                    // Get the latest file name, if it has changed, we have entered a new file, write our current data to file
                    var latestSymbol = data.Symbol;
                    var latestOutputFile = GetZipOutputFileName(_dataDirectory, data.Time, latestSymbol);
                    if (outputFile.IsNullOrEmpty() || outputFile != latestOutputFile)
                    {
                        if (!currentFileData.IsNullOrEmpty())
                        {
                            // Launch a write task for the current file and data set
                            var file = outputFile;
                            var fileData = currentFileData;
                            var fileSymbol = symbol;
                            writeTasks.Enqueue(Task.Run(() =>
                            {
                                WriteFile(file, fileData, fileSymbol);
                            }));
                        }

                        // Reset our dictionary and store new output file
                        currentFileData = new List<TimedLine>();
                        outputFile = latestOutputFile;
                        symbol = latestSymbol;
                    }
                }

                // Add data to our current dictionary
                var line = GenerateLine(data, _securityType, _resolution);
                currentFileData.Add(new TimedLine(data.Time, line));

                // Update our time
                lastTime = data.Time;
            }

            // Finish off my processing the last file as well
            if (!currentFileData.IsNullOrEmpty())
            {
                // we want to finish ASAP so let's do it ourselves
                WriteFile(outputFile, currentFileData, symbol);
            }

            // Wait for all our write tasks to finish
            while (writeTasks.Count > 0)
            {
                var task = writeTasks.Dequeue();
                task.Wait();
            }
        }

        /// <summary>
        /// Converts the specified base data instance into a lean data file csv line
        /// </summary>
        public string GenerateLine(IBaseData data, SecurityType securityType, Resolution resolution)
        {
            var milliseconds = data.Time.TimeOfDay.TotalMilliseconds.ToString(CultureInfo.InvariantCulture);
            var longTime = data.Time.ToStringInvariant(DateFormat.TwelveCharacter);

            switch (securityType)
            {
                case SecurityType.Base:
                    switch (resolution)
                    {
                        case Resolution.Minute:
                        case Resolution.Second:
                            return ToCsv(milliseconds, Scale(data.Value));

                        case Resolution.Hour:
                        case Resolution.Daily:
                            return ToCsv(longTime, Scale(data.Value));
                    }
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(securityType), securityType, null);
            }

            throw new NotImplementedException(Invariant(
                $"LeanData.GenerateLine has not yet been implemented for security type: {securityType} at resolution: {resolution}"
            ));
        }

        /// <summary>
        /// Downloads historical data from the brokerage and saves it in LEAN format.
        /// </summary>
        /// <param name="brokerage">The brokerage from where to fetch the data</param>
        /// <param name="symbols">The list of symbols</param>
        /// <param name="startTimeUtc">The starting date/time (UTC)</param>
        /// <param name="endTimeUtc">The ending date/time (UTC)</param>
        // public void DownloadAndSave(IBrokerage brokerage, List<Symbol> symbols, DateTime startTimeUtc, DateTime endTimeUtc)
        // {
        //     if (symbols.Count == 0)
        //     {
        //         throw new ArgumentException("DownloadAndSave(): The symbol list cannot be empty.");
        //     }

        //     if (symbols.Any(x => x.SecurityType != _securityType))
        //     {
        //         throw new ArgumentException($"DownloadAndSave(): All symbols must have {_securityType} security type.");
        //     }

        //     if (symbols.DistinctBy(x => x.ID.Symbol).Count() > 1)
        //     {
        //         throw new ArgumentException("DownloadAndSave(): All symbols must have the same root ticker.");
        //     }

        //     var dataType = LeanData.GetDataType(_resolution, _tickType);

        //     var marketHoursDatabase = MarketHoursDatabase.FromDataFolder();

        //     var ticker = symbols.First().ID.Symbol;
        //     var market = symbols.First().ID.Market;

        //     var canonicalSymbol = Symbol.Create(ticker, _securityType, market);

        //     var exchangeHours = marketHoursDatabase.GetExchangeHours(canonicalSymbol.ID.Market, canonicalSymbol, _securityType);
        //     var dataTimeZone = marketHoursDatabase.GetDataTimeZone(canonicalSymbol.ID.Market, canonicalSymbol, _securityType);

        //     foreach (var symbol in symbols)
        //     {
        //         var historyRequest = new HistoryRequest(
        //             startTimeUtc,
        //             endTimeUtc,
        //             dataType,
        //             symbol,
        //             _resolution,
        //             exchangeHours,
        //             dataTimeZone,
        //             _resolution,
        //             true,
        //             false,
        //             DataNormalizationMode.Raw,
        //             _tickType
        //         );

        //         var history = brokerage.GetHistory(historyRequest)
        //             .Select(
        //                 x =>
        //                 {
        //                     // Convert to date timezone before we write it
        //                     x.Time = x.Time.ConvertTo(exchangeHours.TimeZone, dataTimeZone);
        //                     return x;
        //                 })
        //             .ToList();

        //         // Generate a writer for this data and write it
        //         var writer = new LeanDataWriter(_resolution, symbol, _dataDirectory, _tickType);
        //         writer.Write(history);
        //     }
        // }

        /// <summary>
        /// Loads an existing Lean zip file into a SortedDictionary
        /// </summary>
        private bool TryLoadFile(string fileName, string entryName, DateTime date, out SortedDictionary<DateTime, string> rows)
        {
            rows = new SortedDictionary<DateTime, string>();

            using (var stream = _dataCacheProvider.Fetch($"{fileName}#{entryName}"))
            {
                if (stream == null)
                {
                    return false;
                }

                using (var reader = new StreamReader(stream))
                {
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        rows[LeanData.ParseTime(line, date, _resolution)] = line;
                    }
                }

                return true;
            }
        }

        /// <summary>
        /// Write this file to disk with the given data.
        /// </summary>
        /// <param name="filePath">The full path to the new file</param>
        /// <param name="data">The data to write as a list of dates and strings</param>
        /// <param name="symbol">The symbol associated with this data</param>
        /// <remarks>The reason we have the data as IEnumerable(DateTime, string) is to support
        /// a generic write that works for all resolutions. In order to merge in hour/daily case I need the
        /// date of the data to correctly merge the two. In order to support writing ticks I need to allow
        /// two data points to have the same time. Thus I cannot use a single list of just strings nor
        /// a sorted dictionary of DateTimes and strings. </remarks>
        private void WriteFile(string filePath, List<TimedLine> data, Symbol symbol)
        {
            if (data == null || data.Count == 0)
            {
                return;
            }

            // because we read & write the same file we need to take a lock per file path so we don't read something that might get outdated
            // by someone writting to the same path at the same time
            _keySynchronizer.Execute(filePath, singleExecution: false, () =>
            {
                var date = data[0].Time;
                // Generate this csv entry name
                var entryName = GenerateZipEntryName(symbol, date, _resolution);

                // Check disk once for this file ahead of time, reuse where possible
                var fileExists = File.Exists(filePath);

                // If our file doesn't exist its possible the directory doesn't exist, make sure at least the directory exists
                if (!fileExists)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(filePath));
                }

                // Handle merging of files
                // Only merge on files with hour/daily resolution, that exist, and can be loaded
                string finalData = null;
                if (_writePolicy == WritePolicy.Append)
                {
                    var streamWriter = new ZipStreamWriter(filePath, entryName);
                    foreach (var tuple in data)
                    {
                        streamWriter.WriteLine(tuple.Line);
                    }
                    streamWriter.DisposeSafely();
                }
                else if (_writePolicy == WritePolicy.Merge && fileExists && TryLoadFile(filePath, entryName, date, out var rows))
                {
                    // Preform merge on loaded rows
                    foreach (var timedLine in data)
                    {
                        rows[timedLine.Time] = timedLine.Line;
                    }

                    // Final merged data product
                    finalData = string.Join("\n", rows.Values);
                }
                else
                {
                    // Otherwise just extract the data from the given list.
                    finalData = string.Join("\n", data.Select(x => x.Line));
                }

                if (finalData != null)
                {
                    var bytes = Encoding.UTF8.GetBytes(finalData);
                    _dataCacheProvider.Store($"{filePath}#{entryName}", bytes);
                }

                if (Log.DebuggingEnabled)
                {
                    var from = data[0].Time.Date.ToString(DateFormat.EightCharacter, CultureInfo.InvariantCulture);
                    var to = data[data.Count - 1].Time.Date.ToString(DateFormat.EightCharacter, CultureInfo.InvariantCulture);
                    Log.Debug($"LeanDataWriter.Write({symbol.ID}): Appended: {filePath} @ {entryName} {from}->{to}");
                }
            });
        }

        /// <summary>
        /// Generate's the zip entry name to hold the specified data.
        /// </summary>
        public string GenerateZipEntryName(Symbol symbol, DateTime date, Resolution resolution)
        {
            var formattedDate = date.ToStringInvariant(DateFormat.EightCharacter);
            var isHourOrDaily = resolution == Resolution.Hour || resolution == Resolution.Daily;

            switch (symbol.ID.SecurityType)
            {
                case SecurityType.Base:
                    if (isHourOrDaily)
                    {
                        return $"{symbol.ID.Symbol.ToLowerInvariant()}.csv";
                    }

                    return Invariant($"{formattedDate}_{symbol.ID.Symbol.ToLowerInvariant()}_{resolution.ResolutionToLower()}.csv");

                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        /// <summary>
        /// Get the output zip file
        /// </summary>
        /// <param name="baseDirectory">Base output directory for the zip file</param>
        /// <param name="time">Date/time for the data we're writing</param>
        /// <param name="symbol">The associated symbol. For example for options/futures it will be different than the canonical at <see cref="_symbol"/></param>
        /// <returns>The full path to the output zip file</returns>
        public string GetZipOutputFileName(string baseDirectory, DateTime time, Symbol symbol)
        {
            return Path.Combine(baseDirectory, GenerateRelativeZipFileDirectory(symbol, _resolution), GenerateZipFileName(symbol, time, _resolution));
        }

        /// <summary>
        /// Generates the zip file name for the specified date of data.
        /// </summary>
        protected static string GenerateZipFileName(Symbol symbol, DateTime date, Resolution resolution)
        {
            var formattedDate = date.ToStringInvariant(DateFormat.EightCharacter);
            var isHourOrDaily = resolution == Resolution.Hour || resolution == Resolution.Daily;

            switch (symbol.ID.SecurityType)
            {
                case SecurityType.Base:
                    if (isHourOrDaily)
                    {
                        return $"{symbol.ID.Symbol.ToLowerInvariant()}.zip";
                    }

                    return $"{formattedDate}.zip";
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        /// <summary>
        /// Generates the relative zip directory for the specified symbol/resolution
        /// </summary>
        protected string GenerateRelativeZipFileDirectory(Symbol symbol, Resolution resolution)
        {
            var isHourOrDaily = resolution == Resolution.Hour || resolution == Resolution.Daily;
            var securityType = symbol.SecurityType.SecurityTypeToLower();

            var market = symbol.ID.Market.ToLowerInvariant();
            var res = resolution.ResolutionToLower();
            var directory = Path.Combine(_customFolderName, securityType , market, res);
            switch (symbol.ID.SecurityType)
            {
                case SecurityType.Base:
                    return !isHourOrDaily ? Path.Combine(directory, symbol.ID.Symbol.ToLowerInvariant()) : directory;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        private class TimedLine
        {
            public string Line { get; }
            public DateTime Time { get; }
            public TimedLine(DateTime time, string line)
            {
                Line = line;
                Time = time;
            }
        }

        /// <summary>
        /// Create a csv line from the specified arguments
        /// </summary>
        private string ToCsv(params object[] args)
        {
            // use culture neutral formatting for decimals
            for (var i = 0; i < args.Length; i++)
            {
                var value = args[i];
                if (value is decimal)
                {
                    args[i] = ((decimal) value).Normalize();
                }
            }

            return string.Join(",", args);
        }

        /// <summary>
        /// Scale and convert the resulting number to deci-cents int.
        /// </summary>
        private long Scale(decimal value)
        {
            return (long)(value*10000);
        }
    }
}
