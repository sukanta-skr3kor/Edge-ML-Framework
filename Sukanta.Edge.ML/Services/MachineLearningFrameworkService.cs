//*********************************************************************************************
//* File             :   MachineLearningFrameworkService.cs
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

using Sukanta.DataBus.Redis;
using Sukanta.Edge.DataService;
using Sukanta.Edge.ML.Forecasting;
using Sukanta.Edge.ML.Model;
using Sukanta.Edge.ML.NotificationHub;
using Sukanta.Edge.ML.SpikeAndChangePointDetection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Sukanta.Edge.ML.Services
{
    /// <summary>
    /// MachineLearning Framework Service
    /// </summary>
    public class MachineLearningFrameworkService : BackgroundService
    {
        /// <summary>
        /// Result files Deleteion Task
        /// </summary>
        private Task _resultFileDeletionManagerTask = null;

        /// <summary>
        /// RuleDataProvider
        /// </summary>
        private DataServiceProvider _dataServiceProvider;

        /// <summary>
        /// RedisDataBus Publisher 
        /// </summary>
        private IRedisDataBusPublisher _redisDataBusPublisher;

        /// <summary>
        /// Settings
        /// </summary>
        private AppSettings _appSettings;

        /// <summary>
        /// RedisDataBus
        /// </summary>
        private RedisDataBus _redisDataBus;

        /// <summary>
        /// SpikeDetection
        /// </summary>
        private SpikeDetection _spikeDetection;

        /// <summary>
        /// ChangePointDetection
        /// </summary>
        private ChangePointDetection _changePointDetection;

        /// <summary>
        /// AnomalyDetection
        /// </summary>
        private AnomalyDetection.AnomalyDetection _anomalyDetection;

        /// <summary>
        /// Forecasting
        /// </summary>
        private ForecastingEngine _forecastingEngine;

        /// <summary>
        /// AnomalyDetection Algorithm Settings
        /// </summary>
        private AnomalyDetectionAlgorithm _anomalyDetectionSettings;

        /// <summary>
        /// SpikeDetection Algorithm Settings
        /// </summary>
        private SpikeDetectionAlgorithm _spikeDetectionSettings;

        /// <summary>
        /// ChangePointDetection Algorithm Settings
        /// </summary>
        private ChangePointDetectionAlgorithm _changePointDetectionSettings;

        /// <summary>
        /// Forecasting Algorithm Settings
        /// </summary>
        private ForecastingAlgorithm _forecastingSettings;

        /// <summary>
        ///Alert Notification SignalR Client  
        /// </summary>
        private MLAlertNotificationClient _mLAlertNotificationClient;

        /// <summary>
        ///  MachineLearning Framework Service
        /// </summary>
        /// <param name="dataServiceProvider"></param>
        /// <param name="redisDataBusPublisher"></param>
        /// <param name="redisDataBus"></param>
        /// <param name="mLAlertNotificationClient"></param>
        /// <param name="settings"></param>
        /// <param name="anomalyDetectionSettings"></param>
        /// <param name="spikeDetectionSettings"></param>
        /// <param name="changePointDetectionSettings"></param>
        /// <param name="forecastingSettings"></param>
        public MachineLearningFrameworkService(DataServiceProvider dataServiceProvider, IRedisDataBusPublisher redisDataBusPublisher, RedisDataBus redisDataBus,
                    MLAlertNotificationClient mLAlertNotificationClient, IOptions<AppSettings> settings, IOptions<AnomalyDetectionAlgorithm> anomalyDetectionSettings,
                   IOptions<SpikeDetectionAlgorithm> spikeDetectionSettings, IOptions<ChangePointDetectionAlgorithm> changePointDetectionSettings,
                   IOptions<ForecastingAlgorithm> forecastingSettings)
        {
            _appSettings = settings.Value;
            _redisDataBusPublisher = redisDataBusPublisher;
            _redisDataBus = redisDataBus;
            _dataServiceProvider = dataServiceProvider;
            _anomalyDetectionSettings = anomalyDetectionSettings.Value;
            _spikeDetectionSettings = spikeDetectionSettings.Value;
            _changePointDetectionSettings = changePointDetectionSettings.Value;
            _forecastingSettings = forecastingSettings.Value;
            _mLAlertNotificationClient = mLAlertNotificationClient;

            _anomalyDetection = new AnomalyDetection.AnomalyDetection(_dataServiceProvider, _redisDataBus, _mLAlertNotificationClient, _anomalyDetectionSettings);
            _spikeDetection = new SpikeDetection(_dataServiceProvider, _redisDataBus, _mLAlertNotificationClient, _spikeDetectionSettings);
            _changePointDetection = new ChangePointDetection(_dataServiceProvider, _redisDataBus, _mLAlertNotificationClient, _changePointDetectionSettings);
            _forecastingEngine = new ForecastingEngine(_dataServiceProvider, _redisDataBus, _mLAlertNotificationClient, _forecastingSettings);
        }

        /// <summary>
        /// StartAsync
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public override async Task StartAsync(CancellationToken cancellationToken)
        {
            _anomalyDetection.Start();//Anomaly Task
            _spikeDetection.Start();//Spike task
            _changePointDetection.Start();//Changepoit task
            _forecastingEngine.Start();//forecasting task

            //Result file delete task
            _resultFileDeletionManagerTask = Task.Run(() => DeleteResultFiles());

            LoggerLib.LoggerHelper.Information("ML framework Services Started successfully");
            await Task.Delay(1000);
        }

        /// <summary>
        /// StopAsync
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            _spikeDetection.Stop();
            _changePointDetection.Stop();
            _anomalyDetection.Stop();
            _forecastingEngine.Stop();

            LoggerLib.LoggerHelper.Information("ML framework Services Stopped");
            await Task.Delay(1000);
        }

        /// <summary>
        /// Delete Result Files
        /// </summary>
        /// <returns></returns>
        private async Task DeleteResultFiles()
        {
            TimeSpan delayTimeSpan = TimeSpan.FromHours(24);//1 day interval

            await Task.Run(async () =>
            {
                while (true)
                {
                    try
                    {
                        if (_appSettings.NoOfDaysToKeepResultFile != -1)//if set -1 then no dlete, keep for ever
                        {
                            string[] resultFiles = Directory.GetFiles(_appSettings.ResultFileBasePath, "*.csv");

                            foreach (string file in resultFiles)
                            {
                                FileInfo fileInfo = new FileInfo(file);
                                DateTime createTime = File.GetCreationTime(fileInfo.FullName);

                                if (createTime.Day <= (DateTime.Now.Day - _appSettings.NoOfDaysToKeepResultFile))
                                {
                                    File.Delete(fileInfo.FullName);
                                }
                            }
                        }
                    }
                    catch { }
                    await Task.Delay(delayTimeSpan);
                }
            }).ConfigureAwait(false);
        }

        /// <summary>
        /// Do not implement this
        /// </summary>
        /// <param name="stoppingToken"></param>
        /// <returns></returns>
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await Task.Delay(0);
        }
    }
}
