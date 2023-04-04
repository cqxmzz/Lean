# QUANTCONNECT.COM - Democratizing Finance, Empowering Individuals.
# Lean Algorithmic Trading Engine v2.0. Copyright 2014 QuantConnect Corporation.
#
# Licensed under the Apache License, Version 2.0 (the "License");
# you may not use this file except in compliance with the License.
# You may obtain a copy of the License at http://www.apache.org/licenses/LICENSE-2.0
#
# Unless required by applicable law or agreed to in writing, software
# distributed under the License is distributed on an "AS IS" BASIS,
# WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
# See the License for the specific language governing permissions and
# limitations under the License.

from AlgorithmImports import *


from Alphas.HistoricalReturnsAlphaModel import HistoricalReturnsAlphaModel
from Portfolio.BlackLittermanOptimizationPortfolioConstructionModel import *
from Portfolio.UnconstrainedMeanVariancePortfolioOptimizer import UnconstrainedMeanVariancePortfolioOptimizer
from Risk.NullRiskManagementModel import NullRiskManagementModel

class MyTestAlgorithm(QCAlgorithm):
    '''Basic template algorithm simply initializes the date range and cash'''

    def Initialize(self):
        # Set requested data resolution
        self.UniverseSettings.Resolution = Resolution.Daily

        # Initialise the data and resolution required, as well as the cash and start-end dates for your algorithm. All algorithms must initialized.
        self.SetStartDate(2021, 1, 1)
        self.SetEndDate(2023, 1, 1)
        self.SetCash(100000)
        
        # Set TrainingMethod to be executed at 8:00 am every Sunday
        self.Train(self.DateRules.Every(DayOfWeek.Sunday), self.TimeRules.At(8 , 0), self.TrainingMethod)
        
        self.symbols = [ Symbol.Create(x, SecurityType.Equity, Market.USA) for x in [ 'AAPL', 'MSFT', 'AMZN', 'NVDA', 'GOOGL' ] ]

        optimizer = UnconstrainedMeanVariancePortfolioOptimizer()

        # set algorithm framework models
        self.SetUniverseSelection(CoarseFundamentalUniverseSelectionModel(self.StaticSP500Selector))
        self.SetAlpha(HistoricalReturnsAlphaModel(resolution = Resolution.Daily))
        self.SetPortfolioConstruction(BlackLittermanOptimizationPortfolioConstructionModel(optimizer = optimizer))
        self.SetExecution(ImmediateExecutionModel())
        self.SetRiskManagement(NullRiskManagementModel())


    def StaticSP500Selector(self, coarse):
        return self.symbols

    def OnOrderEvent(self, orderEvent):
        if orderEvent.Status == OrderStatus.Filled:
            self.Debug(orderEvent)

    def TrainingMethod(self):

        self.Log(f'Start training at {self.Time}')
        # Use the historical data to train the machine learning model
        history = self.History(["SPY"], 200, Resolution.Daily)

        # ML code:
        pass