//*********************************************************************************************
//* File             :   AnomalyDetection.cs
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

using CsvHelper;
using CsvHelper.Configuration;
using Sukanta.DataBus.Redis;
using Sukanta.Edge.DataService;
using Sukanta.Edge.ML.Model;
using Sukanta.Edge.ML.NotificationHub;
using Sukanta.LoggerLib;
using Microsoft.ML;
using Microsoft.ML.TimeSeries;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Sukanta.Edge.ML.AnomalyDetection
{
    /// <summary>
    /// Anomaly Detection
    /// </summary>
    public class AnomalyDetection
    {
        private Task _anomalyDetectionTask;
        private CancellationTokenSource _anomalyDetectionCts;
        private readonly CancellationToken _shutdownToken;
        private string ResultCsvRelativePath = @"./MLResults";

        /// <summary>
        /// MLContext
        /// </summary>
        private readonly MLContext _mlContext = new MLContext();

        /// <summary>
        /// DataServiceProvider
        /// </summary>
        private IDataServiceProvider _dataServiceProvider;

        /// <summary>
        /// RedisDataBus
        /// </summary>
        private RedisDataBus _redisDataBus;

        /// <summary>
        /// AnomalyDetection Settings
        /// </summary>
        private AnomalyDetectionAlgorithm _anomalyDetection { get; set; }

        /// <summary>
        /// SignalR Client
        /// </summary>
        private MLAlertNotificationClient _notifcationClient;

        /// <summary>
        /// AnomalyDetection
        /// </summary>
        /// <param name="dataServiceProvider"></param>
        /// <param name="redisDataBus"></param>
        /// <param name="notificationClient"></param>
        /// <param name="anomalyDetectionSettings"></param>
        public AnomalyDetection(DataServiceProvider dataServiceProvider, RedisDataBus redisDataBus, MLAlertNotificationClient notificationClient,
            AnomalyDetectionAlgorithm anomalyDetectionSettings)
        {
            _dataServiceProvider = dataServiceProvider;
            _redisDataBus = redisDataBus;
            _anomalyDetection = anomalyDetectionSettings;
            _notifcationClient = notificationClient;

            _anomalyDetectionCts = new CancellationTokenSource();
            _shutdownToken = _anomalyDetectionCts.Token;
            ResultCsvRelativePath = _anomalyDetection.ResultCsvFilePath ?? ResultCsvRelativePath;
        }

        /// <summary>
        /// Start
        /// </summary>
        public void Start()
        {
            if (_anomalyDetection.Enabled)
            {
                LoggerHelper.Information("Starting Anomaly Detection...");
                _anomalyDetectionTask = Task.Run(() => ExecuteAomalyDetection(_shutdownToken), _shutdownToken);
                LoggerHelper.Information("Anomaly Detection Started");
            }
        }

        /// <summary>
        /// ExecuteAomaly Detection in machine data
        /// </summary>
        /// <param name="_shutdownToken"></param>
        private void ExecuteAomalyDetection(CancellationToken _shutdownToken)
        {
            int ExecutionInterval = _anomalyDetection.ExecutionIntervalSeconds * 1000;
            int batchSize = _anomalyDetection.BatchSize;
            double threshold = _anomalyDetection.Threshold;
            double sensitivity = _anomalyDetection.Sensitivity;
            int DataSize = _anomalyDetection.PredictionDataSize;
            List<AnomalyAlert> anomalyAlerts = null;
            ConcurrentDictionary<string, MachineTimeSeriesData> dataDict = new ConcurrentDictionary<string, MachineTimeSeriesData>();

            Task.Run(async () =>
            {
                while (true)
                {
                    try
                    {
                        //Fetch data from Redis DB
                        foreach (Parameter parameter in _anomalyDetection.MachineParameters)
                        {
                            List<MachineTimeSeriesData> machineDatas = new List<MachineTimeSeriesData>();
                            int dataIndex = 0;

                            machineDatas = _dataServiceProvider.GetMachineTimeSeriesData(parameter.Id, DataSize);

                            if (machineDatas.Count > 0)
                            {
                                machineDatas.AddRange(machineDatas);

                                dataDict.TryAdd(parameter.Id, machineDatas[0]);//Add to dictionary

                                // Convert data to IDataView.
                                IDataView dataView = _mlContext.Data.LoadFromEnumerable(machineDatas);

                                // Setup estimator arguments
                                string outputColumnName = nameof(MachineSrCnnAnomalyDetection.Prediction);
                                string inputColumnName = nameof(MachineTimeSeriesData.Value);

                                IDataView outputDataView = _mlContext.AnomalyDetection.DetectEntireAnomalyBySrCnn(dataView, outputColumnName, inputColumnName,
                                  threshold: threshold, batchSize: batchSize, sensitivity: sensitivity, detectMode: SrCnnDetectMode.AnomalyAndMargin);

                                IEnumerable<MachineSrCnnAnomalyDetection> predictions = _mlContext.Data.CreateEnumerable<MachineSrCnnAnomalyDetection>
                                                                                         (outputDataView, reuseRowObject: false);

                                //Send signalR notification
                                if (_anomalyDetection.NotificationEnabled)
                                {
                                    anomalyAlerts = new List<AnomalyAlert>();

                                    if (CheckDataNotDuplicated(dataDict, parameter.Id, machineDatas[0]))
                                    {
                                        foreach (MachineSrCnnAnomalyDetection prediction in predictions)
                                        {
                                            bool isAnomaly = prediction.Prediction[0] == 1;

                                            if (isAnomaly)
                                            {
                                                AnomalyAlert anomalyAlert = new AnomalyAlert()
                                                {
                                                    ParameterName = parameter.Id,
                                                    IsAnomaly = isAnomaly,
                                                    ActualValue = machineDatas[dataIndex].Value,
                                                    ExpectedValue = prediction.Prediction[3],
                                                    Time = machineDatas[dataIndex].Time
                                                };
                                                anomalyAlerts.Add(anomalyAlert);

                                                await _notifcationClient.SendAnomalyAlertForParameter(anomalyAlert.ParameterName, anomalyAlert).ConfigureAwait(false);
                                            }
                                            dataIndex++;
                                        }
                                    }
                                }

                                //Generate csv file
                                if (_anomalyDetection.ResultCsvGenerationEnabled && anomalyAlerts?.Count > 0)
                                {
                                    GenerateCsvFile(anomalyAlerts);
                                }
                            }
                            else
                            {
                                LoggerHelper.Warning($"No data received for {parameter.Id} from databus");
                            }
                        }
                    }
                    catch (Exception exp)
                    {
                        LoggerHelper.Error(exp, $"Error executing Anomaly detection Algorithm");
                    }
                    await Task.Delay(ExecutionInterval);//sleep interval
                }
            }, _shutdownToken).ConfigureAwait(false);
        }

        /// <summary>
        /// CheckDataNotDuplicated
        /// </summary>
        /// <param name="dataDict"></param>
        /// <param name="id"></param>
        /// <param name="machineData"></param>
        /// <returns></returns>
        private bool CheckDataNotDuplicated(ConcurrentDictionary<string, MachineTimeSeriesData> dataDict, string id, MachineTimeSeriesData machineData)
        {
            try
            {
                //Get previous value for compare if data is already sent or not
                dataDict.TryGetValue(id, out MachineTimeSeriesData tempData);

                if (tempData.Value != machineData.Value && id == tempData.Id)
                {
                    //Update here
                    dataDict.AddOrUpdate(id, machineData, (key, oldValue) => machineData);
                    return true;
                }
            }
            catch { }
            return false;
        }

        /// <summary>
        /// Generate Csv File
        /// </summary>
        /// <param name="anomalyAlerts"></param>
        /// <returns></returns>
        private void GenerateCsvFile(List<AnomalyAlert> anomalyAlerts)
        {
            CsvConfiguration csvConfig = new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                HasHeaderRecord = true,
                Delimiter = ",",
            };

            try
            {
                string resultFilePath = Path.Combine(ResultCsvRelativePath, DateTime.Now.ToString("yyyyMMdd") + MLConstants.ANOMALY_RESULTFILE);

                if (!Directory.Exists(_anomalyDetection.ResultCsvFilePath))
                {
                    Directory.CreateDirectory(_anomalyDetection.ResultCsvFilePath);
                }

                if (File.Exists(resultFilePath))//Append to existing file
                {
                    csvConfig.HasHeaderRecord = false;//No header

                    using (FileStream stream = File.Open(resultFilePath, FileMode.Append))
                    using (StreamWriter writer = new StreamWriter(stream))
                    using (CsvWriter csv = new CsvWriter(writer, csvConfig))
                    {
                        foreach (AnomalyAlert anomalyAlert in anomalyAlerts)
                        {
                            csv.WriteRecord(anomalyAlert);
                            csv.NextRecord();
                        }
                    }
                }
                else//Create New file
                {
                    using (StreamWriter writer = new StreamWriter(resultFilePath))
                    using (CsvWriter csv = new CsvWriter(writer, csvConfig))
                    {
                        csv.WriteHeader<AnomalyAlert>();
                        csv.NextRecord();

                        foreach (AnomalyAlert anomalyAlert in anomalyAlerts)
                        {
                            csv.WriteRecord(anomalyAlert);
                            csv.NextRecord();
                        }
                    }
                }
            }
            catch (Exception exp)
            {
                LoggerHelper.Error(exp, "Error creating the Csv file for Anomaly Data");
            }
        }

        /// <summary>
        /// Stop
        /// </summary>
        public void Stop()
        {
            try
            {
                _anomalyDetectionCts?.Cancel();

                //wait till task finishes
                _anomalyDetectionTask?.Wait();
                _anomalyDetectionTask = null;

                _anomalyDetectionCts?.Dispose();
                _anomalyDetectionCts = null;
            }
            catch (Exception exp)
            {
                LoggerHelper.Error(exp, "Error while shutting Anomaly detection task.");
            }
            Task.Delay(100);
        }

    }
}
