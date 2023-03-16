from QuantConnect import *
from QuantConnect.Algorithm import QCAlgorithm

import datetime
import json
import math

##
# TODO:
# find offline data source
##

class MyTestAlgorithm(QCAlgorithm):

    def Initialize(self):
        # backtest set up params
        self.cash = 1000000.0                           # Starting cash
        self.addons = True                              # Periodic cash deposits
        self.deposit = 1000.0                           # Periodic cash addons
        self.SetStartDate(2016,1,1)                     # Step back from start to warm up
        self.SetEndDate(2023,1,1)                       # Plus one day to calc taxes for 2017
        self.SetCash(self.cash)                         # Set starting cash 

        # Buying selling lower limit
        self.harvest_limit_config = 300
        self.buy_limit_config = 1000
        # Securities of different types
        self.symbol_config = {'VOO': ['VOO', 'IVV', 'SPLG'],
                              'VO':  ['VO',  'IJH', 'SPMD'],
                              'VB':  ['VB',  'IJR', 'SLY'],
                              'VXUS':['VXUS','IXUS','CWI'],
                              'BND': ['BND', 'IUSB','SPAB'],
                              'BNDX':['BNDX','IAGG','BWX'],
                              'VTIP':['VTIP','SCHP','TIP'],
                              'VNQ': ['VNQ', 'IVR', 'RWR'],
                              'VNQI':['VNQI','IFGL','RWX'],
                              'GLD': ['GLD', 'IAU', 'DBP'],
                              'COMT':['COMT','DBC', 'GSG']}
        # Fixed weight allocation
        self.weight_config = {'VOO' : 0.35,
                              'VO'  : 0.1,
                              'VB'  : 0.05,
                              'VXUS': 0.15,
                              'BND' : 0.05,
                              'BNDX': 0.05,
                              'VTIP': 0.05,
                              'VNQ' : 0.05,
                              'VNQI': 0.05,
                              'GLD' : 0.05,
                              'COMT': 0.05}
        # age adjustment
        self.use_age_adjustment = True
        self.birth_year = 1992
        self.age_weighted_types = [
            'BND',
            'BNDX',
            'VTIP',
            'GLD',
            'COMT']
        # outside allocation
        self.use_outside_allocation = True
        self.outside_allocation_config = {
            'VOO' : {
                'initial': 10000,
                'increase': 100,
                'year': 2022
            },
            'BND' : {
                'initial': 50000,
                'increase': 5000,
                'year': 2022
            },
        }
        # last sold times
        self.refresh_last_sold_times = True
        self.default_last_sold_times = datetime.datetime(2015, 1, 1).timestamp()
        
        # set utility constants
        self.symbols = [v for vl in self.symbol_config.values() for v in vl]
        self.symbol_types = self.symbol_config.keys()
        self.symbol_to_type = {v: k for k, vl in self.symbol_config.items() for v in vl}
        self.last_sold_times = {k: self.default_last_sold_times for k in self.symbols}
        self.DeserializeLastSoldTimes()
        
        # set up symbols and indicators
        for symbol in self.symbols:
            self.AddEquity(symbol, Resolution.Daily)    # Add securities to portfolio

        # schedule rebalancing
        # self.Schedule.On(self.DateRules.WeekStart('VOO', daysOffset=2), \
        #                 self.TimeRules.BeforeMarketClose('VOO', 60), \
        #                 self.Rebalance)
        
        self.Schedule.On(self.DateRules.Today, \
                        self.TimeRules.Now, \
                        self.Rebalance)
    
    def SerializeLastSoldTimes(self):
        self.ObjectStore.Save("last_sold_times", json.dumps(self.last_sold_times))
    
    def DeserializeLastSoldTimes(self):
        if self.refresh_last_sold_times:
            self.ObjectStore.Delete("last_sold_times")
            return
        if self.ObjectStore.ContainsKey("last_sold_times"):
            self.last_sold_times.update(json.loads(self.ObjectStore.Read("last_sold_times")))
    
    def GetOutsideAllocation(self, type):
        if not self.use_outside_allocation:
            return 0
        if type not in self.outside_allocation_config:
            return 0
        allocation_config = self.outside_allocation_config[type]
        initial = allocation_config['initial']
        increase = allocation_config['increase']
        year = allocation_config['year']
        if self.Time.year < year:
            return 0
        else:
            return initial + increase * (self.Time.year - year)

    def GetWeight(self, type):
        if not self.use_age_adjustment:
            return self.weight_config[type]
        age = self.Time.year - self.birth_year
        age_weight = age / 100.0
        age_weighted_sum = sum(v for k, v in self.weight_config.items() if k in self.age_weighted_types)
        none_age_weighted_sum = sum(v for k, v in self.weight_config.items() if k not in self.age_weighted_types)
        if type in self.age_weighted_types:
            return age_weight / age_weighted_sum * self.weight_config[type]
        else:
            return (1 - age_weight) / none_age_weighted_sum * self.weight_config[type]

    def SellSecurity(self, symbol):
        # default asynchronous = False
        self.Liquidate(symbol)
        self.last_sold_times[symbol] = Time.DateTimeToUnixTimeStamp(self.Time)
        self.SerializeLastSoldTimes()

    def Rebalance(self):
        self.Debug("Rebalance Run") 
        if not self.IsMarketOpen('VOO'):
            self.Debug("Market Closed") 
            return
        # add periodic cash deposit to portfolio if enabled, backtest only
        if self.addons:
            self.Portfolio.SetCash(float(self.Portfolio.Cash) + self.deposit)

        # Sell anything not in config
        if self.Portfolio.Invested:
            for active_key, active_security in self.Portfolio.items():
                if active_key not in self.symbols:
                    self.SellSecurity(active_key.Value)

        # Sell anything shorted
        if self.Portfolio.Invested:
            for active_key, active_security in self.Portfolio.items():
                if active_security.Quantity < 0:
                    self.SellSecurity(active_key.Value)

        # Prepare to harvest
        type_to_buying_symbol = {}
        symbol_to_profit = {s: self.Portfolio[s].UnrealizedProfit 
                          if self.Portfolio.Invested and s in self.Portfolio else 0 
                          for s in self.symbols}
        symbol_to_value = {s: self.Portfolio[s].HoldingsValue 
                          if self.Portfolio.Invested and s in self.Portfolio else 0 
                          for s in self.symbols}

        # find per category symbol that has the most gain/value that are not wash sale buy
        for type in self.symbol_types:
            symbols_to_profit_ratio = \
                {symbol: symbol_to_profit[symbol] / (symbol_to_value[symbol] + 0.1)
                 for symbol in self.symbol_config[type]
                 if Time.DateTimeToUnixTimeStamp(self.Time) - self.last_sold_times[symbol] > 35 * 24 * 60 * 60}
            if len(symbols_to_profit_ratio):
                type_to_buying_symbol[type] = \
                    max(symbols_to_profit_ratio, key=symbols_to_profit_ratio.get)
        
        # loss harvesting
        if self.Portfolio.Invested:
            for active_key, active_security in self.Portfolio.items():
                if active_key in self.symbols:
                    type = self.symbol_to_type[active_key.Value]
                    if type in type_to_buying_symbol and active_key != type_to_buying_symbol[type]:
                        if active_security.UnrealizedProfit < -1 * self.harvest_limit_config:
                            self.SellSecurity(active_key.Value)

        # compute quantity to buy
        cash = float(self.Portfolio.Cash)
        type_to_values = {type: sum((self.Portfolio[symbol].HoldingsValue 
                                    if self.Portfolio.Invested and symbol in self.Portfolio else 0) + self.GetOutsideAllocation(type)
                                    for symbol in self.symbol_config[type]) for type in self.symbol_types}
        total_values = sum(type_to_values.values()) + cash
        type_to_desired_buying_values = {type: max(0, total_values * self.GetWeight(type) - type_to_values[type]) for type in self.symbol_types}
        type_to_desired_buying_values_cleaned = {k: v for k, v in type_to_desired_buying_values.items() if v > self.buy_limit_config}
        type_to_desired_buying_values_cleaned_sum = sum(v for v in type_to_desired_buying_values_cleaned.values())
        symbol_to_buying_quantity = {type_to_buying_symbol[type]: 
                                     math.floor(cash / type_to_desired_buying_values_cleaned_sum * v / (self.Securities[type_to_buying_symbol[type]].Price)) 
                                     for type, v in type_to_desired_buying_values_cleaned.items() if type in type_to_buying_symbol}
        for symbol, buying_quantity in symbol_to_buying_quantity.items():
            if buying_quantity > 0:
                self.MarketOrder(symbol, buying_quantity)

    def OnData(self, data):
        pass