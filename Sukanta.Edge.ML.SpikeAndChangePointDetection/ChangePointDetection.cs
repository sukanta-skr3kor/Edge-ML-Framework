//*********************************************************************************************
//* File             :   ChangePointDetection.cs
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
using Microsoft.ML.Transforms.TimeSeries;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Sukanta.Edge.ML.SpikeAndChangePointDetection
{
    /// <summary>
    /// ChangePointDetection
    /// </summary>
    public class ChangePointDetection
    {
        private Task _changePtDetectionTask;
        private Task _changePtModelBuilderTask;
        private CancellationTokenSource _changePtDetectionCts;
        private readonly CancellationToken _shutdownToken;
        private string BaseModelsRelativePath = @"./MLModels";
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
        /// Configuration settings
        /// </summary>
        private ChangePointDetectionAlgorithm _changePointDetection { get; set; }

        /// <summary>
        /// SignalR Client
        /// </summary>
        private MLAlertNotificationClient _notifcationClient;

        /// <summary>
        /// ChangePointDetection
        /// </summary>
        /// <param name="dataServiceProvider"></param>
        /// <param name="redisDataBus"></param>
        /// <param name="mLAlertNotificationClient"></param>
        /// <param name="changePointDetectionSettings"></param>
        public ChangePointDetection(DataServiceProvider dataServiceProvider, RedisDataBus redisDataBus, MLAlertNotificationClient mLAlertNotificationClient, ChangePointDetectionAlgorithm changePointDetectionSettings)
        {
            _dataServiceProvider = dataServiceProvider;
            _redisDataBus = redisDataBus;
            _changePointDetection = changePointDetectionSettings;
            _notifcationClient = mLAlertNotificationClient;

            _changePtDetectionCts = new CancellationTokenSource();
            _shutdownToken = _changePtDetectionCts.Token;

            BaseModelsRelativePath = _changePointDetection.ModelFilePath ?? BaseModelsRelativePath;
            ResultCsvRelativePath = _changePointDetection.ResultCsvFilePath ?? ResultCsvRelativePath;
        }

        /// <summary>
        /// Start
        /// </summary>
        public void Start()
        {
            if (_changePointDetection.Enabled)
            {
                if (_changePointDetection.BuildLocalModel)
                {
                    if (!Directory.Exists(BaseModelsRelativePath))
                    {
                        Directory.CreateDirectory(BaseModelsRelativePath);
                    }

                    _changePtModelBuilderTask = Task.Run(() => BuildModel(_shutdownToken), _shutdownToken);
                }

                LoggerHelper.Information("Starting ChangePoint Detection...");
                _changePtDetectionTask = Task.Run(() => ExecuteChangePtDetection(_shutdownToken), _shutdownToken);
                LoggerHelper.Information("ChangePoint Detection Started");
            }
        }


        /// <summary>
        /// BuildModel 
        /// </summary>
        /// <param name="shutdownToken"></param>
        private void BuildModel(CancellationToken shutdownToken)
        {
            int PValueSize = _changePointDetection.PValueSize;
            int SeasonalitySize = _changePointDetection.SeasonalitySize;
            float ConfidenceInterval = _changePointDetection.Confidence;
            int TrainingSize = _changePointDetection.TrainingDataSize > 0 ? _changePointDetection.TrainingDataSize : 5000;

            string outputColumnName = nameof(MachineDataChangePointPrediction.Prediction);
            string inputColumnName = nameof(MachineTimeSeriesData.Value);

            int ExecutionInterval = _changePointDetection.LocalModelBuildIntervalMinutes * 1000 * 60;

            Task.Run(async () =>
                {
                    while (true)
                    {
                        try
                        {
                            //Add data from Redis DB
                            foreach (Parameter parameter in _changePointDetection.MachineParameters)
                            {
                                List<MachineData> machineDatas = new List<MachineData>();

                                //Add data from Redis DB
                                machineDatas = _dataServiceProvider.GetMachineData(parameter.Id, TrainingSize);

                                if (machineDatas.Count > 0)
                                {
                                    machineDatas.AddRange(machineDatas);

                                    // Convert data to IDataView.
                                    IDataView dataView = _mlContext.Data.LoadFromEnumerable(machineDatas);

                                    SsaChangePointEstimator trainigPipeLine = _mlContext.Transforms.DetectChangePointBySsa(
                                        outputColumnName,
                                        inputColumnName,
                                        confidence: ConfidenceInterval,
                                        changeHistoryLength: PValueSize,
                                        trainingWindowSize: TrainingSize,
                                        seasonalityWindowSize: SeasonalitySize + 1);

                                    ITransformer trainedModel = trainigPipeLine.Fit(dataView);

                                    string modelFilePath = Path.Combine(BaseModelsRelativePath, parameter.Id + MLConstants.CHANGEPOINT_MODELFILE);

                                    // Save/persist the trained model to a .ZIP file
                                    _mlContext.Model.Save(trainedModel, dataView.Schema, modelFilePath);
                                }
                                else
                                {
                                    LoggerHelper.Warning($"No data received for {parameter.Id} from databus");
                                }
                            }
                        }
                        catch (Exception exp)
                        {
                            LoggerHelper.Error(exp, $"Error executing ChangePoint Detecion Model Builder");
                        }

                        await Task.Delay(ExecutionInterval);//sleep
                    }
                }, shutdownToken).ConfigureAwait(false);
        }

        /// <summary>
        /// DetectChangePoint in machine data
        /// </summary>
        /// <param name="dataView"></param>
        private void ExecuteChangePtDetection(CancellationToken _shutdownToken)
        {
            int ExecutionInterval = _changePointDetection.ExecutionIntervalSeconds * 1000;
            List<ChangePointAlert> changePointAlerts = null;
            ConcurrentDictionary<string, MachineData> dataDict = new ConcurrentDictionary<string, MachineData>();

            Task.Run(async () =>
            {
                while (true)
                {
                    try
                    {
                        string[] modelFiles = Directory.GetFiles(BaseModelsRelativePath, "*.zip");

                        foreach (Parameter parameter in _changePointDetection.MachineParameters)
                        {
                            int dataIndex = 0;
                            string fileName = Path.Combine(BaseModelsRelativePath, parameter.Id + MLConstants.CHANGEPOINT_MODELFILE).ToLowerInvariant();
                            string modelFile = modelFiles.FirstOrDefault(x => x.ToLowerInvariant() == fileName);

                            if (modelFile == null)
                            {
                                LoggerHelper.Error($"ChangePointDetection model file doesn't exist for parameter '{parameter.Id}'");
                            }

                            if (!string.IsNullOrEmpty(modelFile))
                            {
                                if (modelFile.ToLowerInvariant().Contains(MLConstants.CHANGEPOINT_MODELFILE.ToLowerInvariant()))
                                {
                                    List<MachineData> machineDatas = _dataServiceProvider.GetMachineData(parameter.Id, _changePointDetection.PredictionDataSize);

                                    if (machineDatas.Count > 0)
                                    {
                                        dataDict.TryAdd(parameter.Id, machineDatas[0]);

                                        // Convert data to IDataView.
                                        IDataView dataView = _mlContext.Data.LoadFromEnumerable(machineDatas);

                                        ITransformer trainedModel = _mlContext.Model.Load(modelFile, out DataViewSchema modelInputSchema);

                                        IDataView transformedData = trainedModel.Transform(dataView);

                                        // Getting the data of the newly created column as an IEnumerable
                                        IEnumerable<MachineDataChangePointPrediction> predictions =
                                            _mlContext.Data.CreateEnumerable<MachineDataChangePointPrediction>(transformedData, reuseRowObject: false);

                                        //Send SignalR nptification
                                        if (_changePointDetection.NotificationEnabled)
                                        {
                                            changePointAlerts = new List<ChangePointAlert>();

                                            if (CheckDataNotDuplicated(dataDict, parameter.Id, machineDatas[0]))
                                            {
                                                foreach (MachineDataChangePointPrediction prediction in predictions)
                                                {
                                                    bool isChangePoint = prediction.Prediction[0] == 1 ? true : false;

                                                    if (isChangePoint)
                                                    {
                                                        ChangePointAlert changePointAlert = new ChangePointAlert()
                                                        {
                                                            ParameterName = parameter.Id,
                                                            IsChangePoint = isChangePoint,
                                                            Value = machineDatas[dataIndex].Value,
                                                            Time = machineDatas[dataIndex].Time
                                                        };
                                                        changePointAlerts.Add(changePointAlert);

                                                        await _notifcationClient.SendChangePointAlertForParameter(changePointAlert.ParameterName, changePointAlert).ConfigureAwait(false);
                                                    }
                                                    dataIndex++;
                                                }
                                            }
                                        }

                                        //Generate csv file
                                        if (_changePointDetection.ResultCsvGenerationEnabled && changePointAlerts?.Count > 0)
                                        {
                                            GenerateCsvFile(changePointAlerts);
                                        }
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception exp)
                    {
                        LoggerHelper.Error(exp, $"Error executing Change Point detection Algorithm");
                    }
                    await Task.Delay(ExecutionInterval);//sleep
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
        private bool CheckDataNotDuplicated(ConcurrentDictionary<string, MachineData> dataDict, string id, MachineData machineData)
        {
            try
            {
                //Get prv value for compare if data is already sent or not
                dataDict.TryGetValue(id, out MachineData tempData);

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
        /// <param name="changePointAlerts"></param>
        /// <returns></returns>
        private void GenerateCsvFile(List<ChangePointAlert> changePointAlerts)
        {
            CsvConfiguration csvConfig = new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                HasHeaderRecord = true,
                Delimiter = ",",
            };

            try
            {
                string resultFilePath = Path.Combine(ResultCsvRelativePath, DateTime.Now.ToString("yyyyMMdd") + MLConstants.CHANGEPOINT_RESULTFILE);

                if (!Directory.Exists(_changePointDetection.ResultCsvFilePath))
                {
                    Directory.CreateDirectory(_changePointDetection.ResultCsvFilePath);
                }

                if (File.Exists(resultFilePath))//Append to existing file
                {
                    csvConfig.HasHeaderRecord = false;//No header

                    using (FileStream stream = File.Open(resultFilePath, FileMode.Append))
                    using (StreamWriter writer = new StreamWriter(stream))
                    using (CsvWriter csv = new CsvWriter(writer, csvConfig))
                    {
                        foreach (ChangePointAlert changePointAlert in changePointAlerts)
                        {
                            csv.WriteRecord(changePointAlert);
                            csv.NextRecord();
                        }
                    }
                }
                else//Create New file
                {
                    using (StreamWriter writer = new StreamWriter(resultFilePath))
                    using (CsvWriter csv = new CsvWriter(writer, csvConfig))
                    {
                        csv.WriteHeader<ChangePointAlert>();
                        csv.NextRecord();

                        foreach (ChangePointAlert changePointAlert in changePointAlerts)
                        {
                            csv.WriteRecord(changePointAlert);
                            csv.NextRecord();
                        }
                    }
                }
            }
            catch (Exception exp)
            {
                LoggerHelper.Error(exp, "Error creating the Csv file for ChangePoint Data");
            }
        }

        /// <summary>
        /// Stop
        /// </summary>
        public void Stop()
        {
            try
            {
                _changePtDetectionCts?.Cancel();

                //wait till task finishes
                _changePtDetectionTask?.Wait();
                _changePtDetectionTask = null;

                _changePtModelBuilderTask?.Wait();
                _changePtModelBuilderTask = null;

                _changePtDetectionCts?.Dispose();
                _changePtDetectionCts = null;
            }
            catch (Exception exp)
            {
                LoggerHelper.Error(exp, "Error while shutting spike detection task.");
            }
            Task.Delay(100);
        }

    }
}
