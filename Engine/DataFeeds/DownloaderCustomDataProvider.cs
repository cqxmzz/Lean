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
 *
*/

using System;
using System.IO;
using System.Linq;
using QuantConnect.Util;
using QuantConnect.Data;
using QuantConnect.Logging;
using QuantConnect.Securities;
using QuantConnect.Interfaces;
using QuantConnect.Configuration;
using System.Collections.Concurrent;
using System.Text;
using System.Text.RegularExpressions;

namespace QuantConnect.Lean.Engine.DataFeeds
{
    /// <summary>
    /// Data provider which downloads data using an <see cref="IDataDownloader"/> or <see cref="IBrokerage"/> implementation
    /// </summary>
    public class DownloaderCustomDataProvider
    {
        /// <summary>
        /// Synchronizer in charge of guaranteeing a single operation per file path
        /// </summary>
        private readonly static KeyStringSynchronizer DiskSynchronizer = new();
        private static readonly Regex DateCheck = new Regex(@"\d{8}", RegexOptions.Compiled);

        private bool _customDataDownloadError;
        private readonly IDataDownloader _dataDownloader;
        private readonly IDataCacheProvider _dataCacheProvider = new DiskDataCacheProvider(DiskSynchronizer);
        private string _path;
        private Type _baseDataType;

        /// <summary>
        /// Creates a new instance using a target data downloader used for testing
        /// </summary>
        public DownloaderCustomDataProvider(IDataDownloader dataDownloader, Type baseDataType = null)
        {
            _dataDownloader = dataDownloader;
            _baseDataType = baseDataType;
        }

        /// <summary>
        /// Determines if it should downloads new data and retrieves data from disc
        /// </summary>
        /// <param name="key">A string representing where the data is stored</param>
        /// <returns>A <see cref="Stream"/> of the data requested</returns>
        public void Download(int dataUpdatePeriod, Symbol symbol)
        {
            if (NeedToDownload(_path, dataUpdatePeriod))
            {
                DiskSynchronizer.Execute(symbol.ID.Symbol, singleExecution: true, () => {
                    var resolution = Resolution.Daily;
                    if (symbol.SecurityType != SecurityType.Base)
                    {
                        if (!_customDataDownloadError)
                        {
                            _customDataDownloadError = true;
                            // custom data writter doesn't support it
                            Log.Trace($"DownloaderDataProvider.Get(): only custom data is supported, requested: {symbol}");
                        }
                        return;
                    }

                    // we will download until yesterday so we are sure we don't get partial data
                    DateTime startTimeUtc = new DateTime(1998, 1, 2);
                    DateTime endTimeUtc = DateTime.UtcNow.Date.AddDays(dataUpdatePeriod == 0 ? 0 : -1);

                    try
                    {
                        CustomDataWriter writer = null;
                        var getParams = new DataDownloaderGetParameters(symbol, resolution, startTimeUtc, endTimeUtc);

                        var data = _dataDownloader.Get(getParams)
                            .Where(baseData =>
                            {
                                if(symbol.SecurityType == SecurityType.Base)
                                {
                                    return true;
                                }
                                return false;
                            })
                            // for canonical symbols, downloader will return data for all of the chain
                            .GroupBy(baseData => baseData.Symbol);

                        foreach (var dataPerSymbol in data)
                        {
                            if (writer == null)
                            {
                                writer = new CustomDataWriter(resolution, dataPerSymbol.Key, Globals.DataFolder, dataCacheProvider: _dataCacheProvider);
                                _path = writer.GetZipOutputFileName(Globals.DataFolder, endTimeUtc, dataPerSymbol.Key);
                            }

                            // Save the data
                            writer.Write(dataPerSymbol);
                        }
                    }
                    catch (Exception e)
                    {
                        Log.Error(e);
                    }                
                });
            }
        }

        public string GetFilePath(int dataUpdatePeriod, Symbol symbol)
        {
            Download(dataUpdatePeriod, symbol);
            return _path;
        }

        /// <summary>
        /// Main filter to determine if this file needs to be downloaded
        /// </summary>
        /// <param name="filePath">File we are looking at</param>
        /// <returns>True if should download</returns>
        protected bool NeedToDownload(string filePath, int dataUpdatePeriod)
        {
            // Only download if it doesn't exist or is out of date.
            // Files are only "out of date" for non date based files (hour, daily, margins, etc.) because this data is stored all in one file
            return !File.Exists(filePath) || FilePathIsOutOfDate(filePath, dataUpdatePeriod);
        }

        protected bool FilePathIsOutOfDate(string filePath, int dataUpdatePeriod)
        {
            var fileName = Path.GetFileName(filePath);
            // helper to determine if file is date based using regex, matches a 8 digit value because we expect YYYYMMDD
            return !DateCheck.IsMatch(fileName) && DateTime.Now - TimeSpan.FromDays(dataUpdatePeriod) > File.GetLastWriteTime(filePath);
        }
    }
}
