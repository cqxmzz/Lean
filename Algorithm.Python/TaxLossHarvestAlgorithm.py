from AlgorithmImports import *

import datetime
import json
import math
import time
import threading

##
# TODO:
# find offline data source
##

class TaxLossHarvestAlgorithm(QCAlgorithm):

    def Initialize(self):
        # backtest set up params
        self.cash = 1000000.0                           # Starting cash
        self.addons = True                              # Periodic cash deposits
        self.deposit = 1000.0                           # Periodic cash addons
        self.SetStartDate(2016,1,6)                     # Step back from start to warm up
        self.SetEndDate(2023,3,1)                       # Plus one day to calc taxes for 2017
        if not self.LiveMode:
            self.SetCash(self.cash)                         # Set starting cash 
            self.SetBrokerageModel(BrokerageName.TradierBrokerage, AccountType.Cash)
            self.SetBenchmark("VOO")

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
            self.AddEquity(symbol, Resolution.Minute if self.LiveMode else Resolution.Daily)

        # string to compose for emailing, this is shared in all the runs, but we have a lock on rebalance, so it's fine
        self.email_address = "simoncqm@gmail.com"
        self.email_body_string = ""
        self.lock = threading.Lock()
        self.has_data = False

        # schedule rebalancing
        self.Schedule.On(self.DateRules.WeekStart('VOO', daysOffset=2), \
                        self.TimeRules.BeforeMarketClose('VOO', 60), \
                        self.RebalanceRun)
        if self.LiveMode:
            self.Schedule.On(self.DateRules.WeekStart('VOO', daysOffset=1), \
                            self.TimeRules.BeforeMarketClose('VOO', 60), \
                            self.EmailRebalanceRun)
            self.Schedule.On(self.DateRules.EveryDay("VOO"), \
                            self.TimeRules.AfterMarketOpen("VOO", 60), \
                            self.EmailPortfolioRun)
        
        # for live debug
        self.Schedule.On(self.DateRules.Today, self.TimeRules.Now, self.RebalanceRun)
        # self.Schedule.On(self.DateRules.Today, self.TimeRules.Now, self.EmailRebalanceRun)
        # self.Schedule.On(self.DateRules.Today, self.TimeRules.Now, self.EmailPortfolioRun)

    def SerializeLastSoldTimes(self):
        self.ObjectStore.Save("last_sold_times", json.dumps(self.last_sold_times))
    
    def DeserializeLastSoldTimes(self):
        if self.refresh_last_sold_times:
            self.ObjectStore.Delete("last_sold_times")
            return
        if self.ObjectStore.ContainsKey("last_sold_times"):
            self.last_sold_times.update(json.loads(self.ObjectStore.Read("last_sold_times")))
    
    def OnEndOfAlgorithm(self):
        self.SerializeLastSoldTimes()
        
    def DebugString(self, message):
        self.Debug(message)
        self.email_body_string += (message + "\n")

    def Email(self, title, body):
        if self.LiveMode:
            self.Notify.Email(self.email_address, title, body)
    
    def OnData(self, data):
        self.DebugString("OnData Called")
        if not data.HasData:
            return
        for symbol in self.symbols:
            if self.Securities[symbol].Price < 0:
                return
        self.has_data = True
        self.DebugString("OnData Has Data")

    def WaitForData(self):
        if self.LiveMode:
            while not self.has_data:
                self.DebugString("No Data, wait for 1 minute")
                time.sleep(60)

    def SecurityToEmailRow(self, symbol):
        active_security = self.Portfolio[symbol]
        return "{symbol}:\tquantity: {quantity}\tvalue: {value}\tprofit: {profit}\tpercent: {percent}\n".format(
            symbol=symbol,
            quantity=active_security.Quantity,
            value=active_security.HoldingsValue,
            profit=active_security.UnrealizedProfit,
            percent=active_security.UnrealizedProfitPercent,
        )

    def SellSecurity(self, symbol, email_only):
        active_security = self.Securities[symbol]
        self.DebugString("Sell: " + self.SecurityToEmailRow(symbol))
        if not email_only:
            self.Liquidate(symbol)
            self.last_sold_times[symbol] = Time.DateTimeToUnixTimeStamp(self.Time)
            self.SerializeLastSoldTimes()
    
    def BuySecurity(self, symbol, quantity, email_only):
        if quantity <= 0:
            return
        active_security = self.Securities[symbol]
        self.DebugString("Buy: {symbol}:\tquantity: {quantity}\tprice: {price}\tvalue: {value}\n".format(
            symbol=symbol,
            quantity=quantity,
            price=active_security.Price,
            value=active_security.Price * quantity,
        ))
        if not email_only:
            self.MarketOrder(symbol, quantity)

    def EmailPortfolioRun(self):
        self.WaitForData()
        email_body = ""
        if self.Portfolio.Invested:
            for symbol in self.Portfolio.keys():
                email_body += self.SecurityToEmailRow(symbol)
        email_body += "cash: {cash}\n".format(cash=self.Portfolio.Cash)
        email_body += "total: {total}\n".format(total=self.Portfolio.TotalPortfolioValue)
        self.Email("Lean Portfolio Report", email_body)

    def RebalanceRun(self):
        self.WaitForData()
        self.lock.acquire()
        self.Rebalance(email_only=False)
        self.lock.release()

    def EmailRebalanceRun(self):
        self.WaitForData()
        self.lock.acquire()
        self.Rebalance(email_only=True)
        self.lock.release()

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

    def Rebalance(self, email_only):
        self.email_body_string = ""
        self.DebugString("Rebalance Run")
        
        # add periodic cash deposit to portfolio if enabled, backtest only
        if self.addons and not self.LiveMode:
            self.Portfolio.SetCash(float(self.Portfolio.Cash) + self.deposit)

        # market not open
        if not self.IsMarketOpen('VOO'):
            self.DebugString("Market Closed") 
            return
        
        # still orders open
        if len(self.Transactions.GetOpenOrders()) > 0:
            self.DebugString("Orders Still Open")
            return

        # Sell anything not in config
        if self.Portfolio.Invested:
            for active_key, active_security in self.Portfolio.items():
                if active_key not in self.symbols:
                    self.SellSecurity(active_key.Value, email_only)
                        
        # Sell anything shorted
        if self.Portfolio.Invested:
            for active_key, active_security in self.Portfolio.items():
                if active_security.Quantity < 0:
                    self.SellSecurity(active_key.Value, email_only)

        # Prepare to harvest
        type_to_buying_symbol = {}
        symbol_to_profit = {s: self.Portfolio[s].UnrealizedProfit 
                          if self.Portfolio.Invested and s in self.Portfolio else 0 
                          for s in self.symbols}
        self.DebugString("symbol_to_profit: " + str(symbol_to_profit.items()))
        symbol_to_value = {s: self.Portfolio[s].HoldingsValue 
                          if self.Portfolio.Invested and s in self.Portfolio else 0 
                          for s in self.symbols}
        self.DebugString("symbol_to_value: " + str(symbol_to_value.items()))
        
        # Sell something if owe cash
        while self.Portfolio.Cash < 0:
            symbols_to_profit_ratio = {symbol: symbol_to_profit[symbol] / (symbol_to_value[symbol] + 0.1) for symbol in self.Portfolio.keys()}
            symbol_to_sell_for_cash = max(symbols_to_profit_ratio, key=symbols_to_profit_ratio.get)
            selling_quantity = min(math.ceil((self.buy_limit_config - self.Portfolio.Cash) / self.Securities[symbol].Price), self.Portfolio[symbol].Quantity)
            self.BuySecurity(symbol_to_sell_for_cash, -selling_quantity, email_only)

        # find per category symbol that has the most gain/value that are not wash sale buy
        for type in self.symbol_types:
            symbols_to_profit_ratio = \
                {symbol: symbol_to_profit[symbol] / (symbol_to_value[symbol] + 0.1)
                 for symbol in self.symbol_config[type]
                 if Time.DateTimeToUnixTimeStamp(self.Time) - self.last_sold_times[symbol] > 35 * 24 * 60 * 60}
            if len(symbols_to_profit_ratio):
                type_to_buying_symbol[type] = \
                    max(symbols_to_profit_ratio, key=lambda s:(symbols_to_profit_ratio[s], s))
            self.DebugString("symbols_to_profit_ratio: " + str(symbols_to_profit_ratio.items()))
        self.DebugString("type_to_buying_symbol: " + str(type_to_buying_symbol.items()))
        
        # loss harvesting
        if self.Portfolio.Invested:
            for active_key, active_security in self.Portfolio.items():
                if active_key in self.symbols:
                    type = self.symbol_to_type[active_key.Value]
                    if type in type_to_buying_symbol and active_key != type_to_buying_symbol[type]:
                        if active_security.UnrealizedProfit < -1 * self.harvest_limit_config:
                            self.SellSecurity(active_key.Value, email_only)

        # compute quantity to buy
        self.DebugString("after harvesting.")
        cash = float(self.Portfolio.Cash)
        self.DebugString("cash: " + str(cash))
        if cash < self.buy_limit_config * 2:
            return
        cash -= self.buy_limit_config
        
        type_to_values = {type: sum((self.Portfolio[symbol].HoldingsValue
                                    if self.Portfolio.Invested and symbol in self.Portfolio else 0) + self.GetOutsideAllocation(symbol)
                                    for symbol in self.symbol_config[type]) for type in self.symbol_types}
        self.DebugString("type_to_values: " + str(type_to_values.items()))
        
        total_values = sum(type_to_values.values()) + cash
        type_to_desired_buying_values = {type: max(0, total_values * self.GetWeight(type) - type_to_values[type]) for type in self.symbol_types}
        self.DebugString("type_to_desired_buying_values: " + str(type_to_desired_buying_values.items()))
        
        type_to_desired_buying_values_sum = sum(v for v in type_to_desired_buying_values.values())
        type_to_buying_values = {k: cash / type_to_desired_buying_values_sum * v for k, v in type_to_desired_buying_values.items() if type in type_to_buying_symbol}
        self.DebugString("type_to_buying_values: " + str(type_to_buying_values.items()))
        
        for type, v in type_to_buying_values.items():
            if v > self.buy_limit_config:
                symbol = type_to_buying_symbol[type]
                price = self.Securities[symbol].Price
                if price > 0:
                    buying_quantity = math.floor(v / price)
                    self.BuySecurity(symbol, buying_quantity, email_only)
        
        self.Email("Lean Portfolio {0}Run Report".format("Faux " if email_only else ""), self.email_body_string)