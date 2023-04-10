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

using Newtonsoft.Json;
using QuantConnect.Configuration;
using QuantConnect.Data;
using QuantConnect.Data.UniverseSelection;
using QuantConnect.Lean.Engine.DataFeeds.Transport;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace QuantConnect.DataSource
{
    public class FredDownload : DownloaderDataProviderBaseData
    {
        public FredDownload() : base (new FREDDataDownloader()) { }

        [JsonProperty("observations")]
        public IList<Observation> Observations { get; set; }

        public class Observation
        { 
            [JsonProperty("date")]
            public DateTime Date { get; set; }
                                                      
            [JsonProperty("value")]
            public string Value { get; set; }
        }
        
    }

    public class FREDDataDownloader : IDataDownloader
    {
        private static string _auth_code = "";
        
        /// <summary>
        /// Gets the FRED API token.
        /// </summary>
        public static string AuthCode { get {
            if (IsAuthCodeSet) {
                return _auth_code;
            } else {
                return Config.Get("fred-api-key");
            }
        } private set {
            _auth_code = value;
            IsAuthCodeSet = true;
        } }
        
        /// <summary>
        /// Returns true if the FRED API token has been set.
        /// </summary>
        public static bool IsAuthCodeSet { get; private set; }

        /// <summary>
        /// Get historical data enumerable for a single symbol, type and resolution given this start and end time (in UTC).
        /// </summary>
        /// <param name="dataDownloaderGetParameters">model class for passing in parameters for historical data</param>
        /// <returns>Enumerable of base data for this symbol</returns>
        public IEnumerable<BaseData> Get(DataDownloaderGetParameters dataDownloaderGetParameters)
        {
            var objectList = new List<FredDownload>();
            var link = $"https://api.stlouisfed.org/fred/series/observations?file_type=json&observation_start=1998-01-01&api_key={AuthCode}&series_id={dataDownloaderGetParameters.Symbol.Value}";
            var reader = new RestSubscriptionStreamReader(link, null, false);
            while (!reader.EndOfStream)
            {
                BaseData instance = null;
                string line = null;
                try
                {
                    {
                        // read a line and pass it to the base data factory
                        line = reader.ReadLine();
                        var series = JsonConvert.DeserializeObject<FredDownload>(line).Observations;
                        foreach (var observation in series)
                        {
                            decimal value;
                            if (Parse.TryParse(observation.Value, NumberStyles.Any, out value))
                            {
                                objectList.Add(new FredDownload
                                {
                                    Symbol = dataDownloaderGetParameters.Symbol,
                                    Time = observation.Date,
                                    EndTime = observation.Date + TimeSpan.FromDays(1),
                                    Value = value
                                });
                            }
                        }
                    }
                } catch (Exception e) {}
            }
            return objectList;
        }
    }
}
