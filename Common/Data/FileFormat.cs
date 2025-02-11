﻿/*
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

namespace QuantConnect.Data
{
    /// <summary>
    /// Specifies the format of data in a subscription
    /// </summary>
    public enum FileFormat
    {
        /// <summary>
        /// Comma separated values (0)
        /// </summary>
        Csv,

        /// <summary>
        /// Binary file data (1)
        /// </summary>
        Binary,

        /// <summary>
        /// Only the zip entry names are read in as symbols (2)
        /// </summary>
        ZipEntryName,

        /// <summary>
        /// Reader returns a BaseDataCollection object (3)
        /// </summary>
        /// <remarks>Lean will unfold the collection and consume it as individual data points</remarks>
        UnfoldingCollection,

        /// <summary>
        /// Data stored using an intermediate index source (4)
        /// </summary>
        Index,

        /// <summary>
        /// Data type inherits from BaseDataCollection.
        /// Reader method can return a non BaseDataCollection type which will be folded, based on unique time,
        /// into an instance of the data type (5)
        /// </summary>
        FoldingCollection,

        /// <summary>
        /// Our own custom data  (6)
        /// </summary>
        CustomData
    }
}
