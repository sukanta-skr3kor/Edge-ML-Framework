//*********************************************************************************************
//* File             :   DataServiceProvider.cs
//* Author           :   Rout, Sukanta  
//* Date             :   2/1/2024
//* Description      :   Initial version
//* Version          :   1.0
//*-------------------------------------------------------------------------------------------
//* dd-MMM-yyyy	: Version 1.x, Changed By : xxx
//*
//*                 - 1)
//*                 - 2)
//*                 - 3)
//*                 - 4)
//*
//*********************************************************************************************
using Sukanta.DataBus.Abstraction;
using Sukanta.DataBus.Redis;
using Sukanta.Edge.ML.Model;
using Sukanta.LoggerLib;
using StackExchange.Redis;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Sukanta.Edge.DataService
{
    /// <summary>
    /// Data Provider(Save and provide data to algorithms)
    /// </summary>
    public class DataServiceProvider : IDataServiceProvider, IDisposable
    {
        private CancellationTokenSource _dataPersisTaskCts;
        private readonly CancellationToken _shutdownToken;
        private Task _dataPersistTask = null;
        private static bool isDataSubscribed = false;

        /// <summary>
        /// RedisDataBusSubscriber
        /// </summary>
        private IRedisDataBusSubscriber _redisDataBusSubscriber;

        /// <summary>
        /// RedisDataBus
        /// </summary>
        private RedisDataBus _redisDataBus;

        /// <summary>
        /// Settings
        /// </summary>
        private AppSettings _settings;

        /// <summary>
        /// Subscribe topic
        /// </summary>
        private string _topic;

        /// <summary>
        /// Data Service
        /// </summary>
        /// <param name="redisDataBusSubscriber"></param>
        /// <param name="redisDataBus"></param>
        /// <param name="settings"></param>
        /// <param name="topic"></param>
        public DataServiceProvider(IRedisDataBusSubscriber redisDataBusSubscriber, RedisDataBus redisDataBus, AppSettings settings, string topic = null)
        {
            _redisDataBusSubscriber = redisDataBusSubscriber;
            _redisDataBus = redisDataBus;
            _settings = settings;
            _dataPersisTaskCts = new CancellationTokenSource();
            _shutdownToken = _dataPersisTaskCts.Token;

            if (string.IsNullOrEmpty(topic) && !string.IsNullOrEmpty(_settings.DataBusSettings.SubscribeTopic))
            {
                topic = _settings.DataBusSettings.SubscribeTopic;
                _topic = topic;
            }

            if (_settings.DataBusSettings.DBPersistEnabled)
            {
                _dataPersistTask = Task.Run(() => PersistData(_shutdownToken).ConfigureAwait(false), _shutdownToken);
            }
        }

        /// <summary>
        /// Collect And Save DataBus Messages to Redis DB
        /// </summary>
        /// <param name="_shutdownToken"></param>
        /// <returns></returns>
        private async Task PersistData(CancellationToken _shutdownToken)
        {
            DataBusMessage dataBusMessage = null;
            Stopwatch stopwatch = new Stopwatch();
            int CollectionInterval = _settings.DataBusSettings.CollectionIntervalSeconds * 1000;

            while (true)
            {
                try
                {
                    //Connect first
                    if (!_redisDataBusSubscriber.IsConnected)
                    {
                        if (_redisDataBusSubscriber.TryConnect())
                        {
                            LoggerHelper.Information("Connected to DataBus");
                        }
                    }

                    //Subscribe to message
                    if (!isDataSubscribed)
                    {
                        await _redisDataBusSubscriber.SubscribeToDataBusAsync<DataBusMessage>(_topic).ConfigureAwait(false);
                        isDataSubscribed = true;
                        LoggerHelper.Information($"Connected to DataBus, topic '{_topic}' subscribed for message successfully");
                    }

                    //Start the watch
                    stopwatch.Start();

                    if (_redisDataBusSubscriber.HasData())
                    {
                        dataBusMessage = _redisDataBusSubscriber.GetDataBusMessage();

                        if (dataBusMessage != null && !string.IsNullOrEmpty(dataBusMessage.Id))
                        {
                            await _redisDataBus.AddStreamAsync(dataBusMessage, _settings.DataBusSettings.StreamLength, StreamSourceType.DataService).ConfigureAwait(false);
                        }
                    }

                    //stop watch after reading
                    stopwatch.Stop();

                    //calculate time taken t read all parameters
                    long executionTime = stopwatch.ElapsedMilliseconds;

                    //Reset stopwatch
                    stopwatch.Reset();

                    long sleepTime = CollectionInterval - executionTime;
                    TimeSpan sleepTimeBeforeNextCollection = TimeSpan.FromMilliseconds(sleepTime);

                    //Check sleep time should be > 0
                    if (sleepTimeBeforeNextCollection.Milliseconds > 0 && sleepTimeBeforeNextCollection.Milliseconds < CollectionInterval)
                    {
                        //Sleep for the specified interval
                        await Task.Delay(sleepTimeBeforeNextCollection);
                    }
                    else
                    {
                        await Task.Delay(0);//no sleep
                    }
                }
                catch (Exception exp)
                {
                    LoggerHelper.Error(exp, exp.Message);
                }

                finally
                {
                    stopwatch.Reset();
                }
            }
        }

        /// <summary>
        /// Get Machine Data
        /// </summary>
        /// <param name="streamSource"></param>
        /// <param name="count"></param>
        /// <returns></returns>
        public List<MachineData> GetMachineData(string streamSource, int count = 1000)
        {
            List<MachineData> timeSeriesDatas = new List<MachineData>();

            try
            {
                StreamEntry[] streamEntries = _redisDataBus.ReadStreamAsync(streamSource, count, StreamSourceType.DataService).GetAwaiter().GetResult();

                foreach (StreamEntry stream in streamEntries)
                {
                    if (stream.Values.Length >= 3)
                    {
                        try
                        {
                            float.TryParse(stream.Values[2].Value.ToString(), out float value);

                            timeSeriesDatas.Add(new MachineData()
                            {
                                Id = stream.Values[1].Value,
                                Value = value,
                                Time = stream.Values[3].Value
                            });
                        }
                        catch
                        {
                            float.TryParse(stream.Values[1].Value.ToString(), out float value);

                            timeSeriesDatas.Add(new MachineData()
                            {
                                Id = stream.Values[0].Value,
                                Value = value,
                                Time = stream.Values[2].Value
                            });
                        }
                    }
                }
            }
            catch (Exception exp)
            {
                LoggerHelper.Error(exp, exp.Message);
            }

            return timeSeriesDatas;
        }

        /// <summary>
        /// Get Machine TimeSerie Data
        /// </summary>
        /// <param name="streamSource"></param>
        /// <param name="count"></param>
        /// <returns></returns>
        public List<MachineTimeSeriesData> GetMachineTimeSeriesData(string streamSource, int count = 1000)
        {
            List<MachineTimeSeriesData> timeSeriesDatas = new List<MachineTimeSeriesData>();

            try
            {
                StreamEntry[] streamEntries = _redisDataBus.ReadStreamAsync(streamSource, count, StreamSourceType.DataService).GetAwaiter().GetResult();

                foreach (StreamEntry stream in streamEntries)
                {
                    if (stream.Values.Length >= 3)
                    {
                        try
                        {
                            double.TryParse(stream.Values[2].Value.ToString(), out double value);

                            timeSeriesDatas.Add(new MachineTimeSeriesData()
                            {
                                Id = stream.Values[1].Value,
                                Value = value,
                                Time = stream.Values[3].Value
                            });
                        }
                        catch
                        {
                            double.TryParse(stream.Values[1].Value.ToString(), out double value);

                            timeSeriesDatas.Add(new MachineTimeSeriesData()
                            {
                                Id = stream.Values[0].Value,
                                Value = value,
                                Time = stream.Values[2].Value
                            });
                        }
                    }
                }
            }
            catch (Exception exp)
            {
                LoggerHelper.Error(exp, exp.Message);
            }

            return timeSeriesDatas;
        }

        /// <summary>
        ///Stop data insertion
        /// </summary>
        /// <param name="hubClient"></param>
        /// <returns></returns>
        public void Stop()
        {
            //cleanup
            Dispose(true);
        }

        /// <summary>
        /// IDisposable dispose
        /// </summary>
        public void Dispose()
        {
            Stop();
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// IDisposable.
        /// </summary>
        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                //wait
                _dataPersisTaskCts?.Cancel();

                try
                {
                    _dataPersistTask?.Wait();//wait 
                    _dataPersistTask = null;

                }
                catch (Exception exp)
                {
                    LoggerHelper.Error(exp, "Error while shutting down DataServiceProvider.");
                }

                _dataPersisTaskCts?.Dispose();
                _dataPersisTaskCts = null;
            }
        }

    }
}
