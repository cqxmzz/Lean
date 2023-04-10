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

using QuantConnect.Data;
using QuantConnect.Lean.Engine.DataFeeds;
using System;
using System.Linq;

namespace QuantConnect.DataSource
{
    class CustomBaseData : BaseData {};

    public abstract class DownloaderDataProviderBaseData : BaseData
    {
        // data provider
        DownloaderCustomDataProvider _dataProvider;
        // initializer
        public DownloaderDataProviderBaseData(IDataDownloader dataDownloader)
        {
            _dataProvider = new DownloaderCustomDataProvider(dataDownloader, this.GetType());
        }

        public int GetDataUpdatePeriod(bool isLiveMode)
        {
            return 7;
        }

        /// <summary>
        /// Return the URL string source of the file. This will be converted to a stream
        /// </summary>
        /// <param name="config">Configuration object</param>
        /// <param name="date">Date of this source file</param>
        /// <param name="isLiveMode">true if we're in live mode, false for backtesting mode</param>
        /// <returns>
        /// String URL of source file.
        /// </returns>
        public override SubscriptionDataSource GetSource(SubscriptionDataConfig config, DateTime date, bool isLiveMode)
        {
            // define downloader custom data provider (with custom data downloader) (with custom update period, depending on live mode)
            var fileName = _dataProvider.GetFilePath(GetDataUpdatePeriod(isLiveMode), config.Symbol);
            
            return new SubscriptionDataSource(
                fileName,
                SubscriptionTransportMedium.LocalFile,
                FileFormat.CustomData);
        }

        /// <summary>
        /// Readers the specified configuration.
        /// </summary>
        /// <param name="config">The configuration.</param>
        /// <param name="content">The content.</param>
        /// <param name="date">The date.</param>
        /// <param name="isLiveMode">if set to <c>true</c> [is live mode].</param>
        /// <returns></returns>
        public override BaseData Reader(SubscriptionDataConfig config, string content, DateTime date, bool isLiveMode)
        {
            var csv = content.Split(',')
                .Select(x => x.Trim())
                .ToList();

            return new CustomBaseData
            {
                // A one day delay is added to the end time automatically
                Time = QuantConnect.Parse.DateTime(csv[0]),
                Symbol = config.Symbol,
                Value = Parse.Int(csv[1]) / 10000m,
            };
        }
    }
}
