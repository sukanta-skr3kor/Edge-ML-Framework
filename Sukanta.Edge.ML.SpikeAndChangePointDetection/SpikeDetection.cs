//*********************************************************************************************
//* File             :   SpikeDetection.cs
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
    /// SpikeDetection
    /// </summary>
    public class SpikeDetection
    {
        private Task _spikeDetectionTask;
        private Task _spikeDetectionModelBuilderTask;
        private CancellationTokenSource _spikeDetectionCts;
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
        /// SpikeDetection Algorithm settings
        /// </summary>
        private SpikeDetectionAlgorithm _spikeDetection { get; set; }

        /// <summary>
        /// SignalR Client
        /// </summary>
        private MLAlertNotificationClient _notifcationClient;

        /// <summary>
        /// SpikeDetection
        /// </summary>
        /// <param name="dataServiceProvider"></param>
        /// <param name="redisDataBus"></param>
        /// <param name="mLAlertNotificationClient"></param>
        /// <param name="spikeDetectionSettings"></param>
        public SpikeDetection(DataServiceProvider dataServiceProvider, RedisDataBus redisDataBus, MLAlertNotificationClient mLAlertNotificationClient, SpikeDetectionAlgorithm spikeDetectionSettings)
        {
            _dataServiceProvider = dataServiceProvider;
            _redisDataBus = redisDataBus;
            _spikeDetection = spikeDetectionSettings;
            _notifcationClient = mLAlertNotificationClient;

            _spikeDetectionCts = new CancellationTokenSource();
            _shutdownToken = _spikeDetectionCts.Token;

            BaseModelsRelativePath = _spikeDetection.ModelFilePath ?? BaseModelsRelativePath;
            ResultCsvRelativePath = _spikeDetection.ResultCsvFilePath ?? ResultCsvRelativePath;
        }

        /// <summary>
        /// Start
        /// </summary>
        public void Start()
        {
            if (_spikeDetection.Enabled)
            {
                if (_spikeDetection.BuildLocalModel)
                {
                    //Create the directory if not exits
                    if (!Directory.Exists(BaseModelsRelativePath))
                    {
                        Directory.CreateDirectory(BaseModelsRelativePath);
                    }

                    _spikeDetectionModelBuilderTask = Task.Run(() => BuildModel(_shutdownToken), _shutdownToken);
                }

                LoggerHelper.Information("Starting Spike Detection...");
                _spikeDetectionTask = Task.Run(() => ExecuteSpikeDetection(_shutdownToken), _shutdownToken);
                LoggerHelper.Information("Spike Detection Started");
            }
        }

        /// <summary>
        /// BuildModel
        /// </summary>
        /// <param name="shutdownToken"></param>
        private void BuildModel(CancellationToken shutdownToken)
        {
            int PValueSize = _spikeDetection.PValueSize;
            int SeasonalitySize = _spikeDetection.SeasonalitySize;
            float ConfidenceInterval = _spikeDetection.Confidence;
            int TrainingSize = _spikeDetection.TrainingDataSize > 0 ? _spikeDetection.TrainingDataSize : 5000;

            string outputColumnName = nameof(MachineDataSpikePrediction.Prediction);
            string inputColumnName = nameof(MachineTimeSeriesData.Value);

            int ExecutionInterval = _spikeDetection.LocalModelBuildIntervalMinutes * 1000 * 60;

            Task.Run(async () =>
            {
                while (true)
                {
                    try
                    {
                        foreach (Parameter parameter in _spikeDetection.MachineParameters)
                        {
                            List<MachineData> machineDatas = new List<MachineData>();

                            //Add data from Redis DB
                            machineDatas = _dataServiceProvider.GetMachineData(parameter.Id, TrainingSize);

                            if (machineDatas.Count > 0)
                            {
                                machineDatas.AddRange(machineDatas);

                                // Convert data to IDataView.
                                IDataView dataView = _mlContext.Data.LoadFromEnumerable(machineDatas);

                                SsaSpikeEstimator trainigPipeLine = _mlContext.Transforms.DetectSpikeBySsa(
                                    outputColumnName,
                                    inputColumnName,
                                    confidence: ConfidenceInterval,
                                    pvalueHistoryLength: PValueSize,
                                    trainingWindowSize: TrainingSize,
                                    seasonalityWindowSize: SeasonalitySize + 1);

                                ITransformer trainedModel = trainigPipeLine.Fit(dataView);

                                string modelFilePath = Path.Combine(BaseModelsRelativePath, parameter.Id + MLConstants.SPIKE_MODELFILE);

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
        /// DetectSpike in machine data
        /// </summary>
        /// <param name="machineDatas"></param>
        private void ExecuteSpikeDetection(CancellationToken _shutdownToken)
        {
            int ExecutionInterval = _spikeDetection.ExecutionIntervalSeconds * 1000;
            List<SpikeAlert> spikeAlerts = null;
            ConcurrentDictionary<string, MachineData> dataDict = new ConcurrentDictionary<string, MachineData>();

            Task.Run(async () =>
            {
                while (true)
                {
                    try
                    {
                        string[] modelFiles = Directory.GetFiles(BaseModelsRelativePath, "*.zip");

                        foreach (Parameter parameter in _spikeDetection.MachineParameters)
                        {
                            int dataIndex = 0;
                            string fileName = Path.Combine(BaseModelsRelativePath, parameter.Id + MLConstants.SPIKE_MODELFILE).ToLowerInvariant();
                            string modelFile = modelFiles.FirstOrDefault(x => x.ToLowerInvariant() == fileName);

                            if (modelFile == null)
                            {
                                LoggerHelper.Error($"SpikeDetection model file doesn't exist for parameter '{parameter.Id}'");
                            }

                            if (!string.IsNullOrEmpty(modelFile))
                            {
                                if (modelFile.ToLowerInvariant().Contains(MLConstants.SPIKE_MODELFILE.ToLowerInvariant()))
                                {
                                    List<MachineData> machineDatas = _dataServiceProvider.GetMachineData(parameter.Id, _spikeDetection.PredictionDataSize);

                                    if (machineDatas.Count > 0)
                                    {
                                        dataDict.TryAdd(parameter.Id, machineDatas[0]);

                                        // Convert data to IDataView.
                                        IDataView dataView = _mlContext.Data.LoadFromEnumerable(machineDatas);

                                        ITransformer trainedModel = _mlContext.Model.Load(modelFile, out DataViewSchema modelInputSchema);

                                        // The transformed data.
                                        IDataView transformedData = trainedModel.Transform(dataView);

                                        // Getting the data of the newly created column as an IEnumerable
                                        IEnumerable<MachineDataSpikePrediction> predictions =
                                            _mlContext.Data.CreateEnumerable<MachineDataSpikePrediction>(transformedData, reuseRowObject: false);

                                        //Send SignalR nptification
                                        if (_spikeDetection.NotificationEnabled)
                                        {
                                            spikeAlerts = new List<SpikeAlert>();

                                            if (CheckDataNotDuplicated(dataDict, parameter.Id, machineDatas[0]))
                                            {
                                                foreach (MachineDataSpikePrediction prediction in predictions)
                                                {
                                                    bool isSpike = prediction.Prediction[0] == 1 ? true : false;

                                                    if (isSpike)
                                                    {
                                                        SpikeAlert spikeAlert = new SpikeAlert()
                                                        {
                                                            ParameterName = parameter.Id,
                                                            IsSpike = isSpike,
                                                            Value = machineDatas[dataIndex].Value,
                                                            Time = machineDatas[dataIndex].Time
                                                        };
                                                        spikeAlerts.Add(spikeAlert);

                                                        await _notifcationClient.SendSpikeAlertForParameter(spikeAlert.ParameterName, spikeAlert).ConfigureAwait(false);
                                                    }
                                                    dataIndex++;
                                                }
                                            }
                                        }

                                        //Generate csv file
                                        if (_spikeDetection.ResultCsvGenerationEnabled && spikeAlerts?.Count > 0)
                                        {
                                            GenerateCsvFile(spikeAlerts);
                                        }
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception exp)
                    {
                        LoggerHelper.Error(exp, $"Error executing spike detection");
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
        /// <param name="spikeAlerts"></param>
        /// <returns></returns>
        private void GenerateCsvFile(List<SpikeAlert> spikeAlerts)
        {
            CsvConfiguration csvConfig = new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                HasHeaderRecord = true,
                Delimiter = ",",
            };

            try
            {
                string resultFilePath = Path.Combine(ResultCsvRelativePath, DateTime.Now.ToString("yyyyMMdd") + MLConstants.SPIKE_RESULTFILE);

                if (!Directory.Exists(_spikeDetection.ResultCsvFilePath))
                {
                    Directory.CreateDirectory(_spikeDetection.ResultCsvFilePath);
                }

                if (File.Exists(resultFilePath))//Append to existing file
                {
                    csvConfig.HasHeaderRecord = false;//No header

                    using (FileStream stream = File.Open(resultFilePath, FileMode.Append))
                    using (StreamWriter writer = new StreamWriter(stream))
                    using (CsvWriter csv = new CsvWriter(writer, csvConfig))
                    {
                        foreach (SpikeAlert spikeAlert in spikeAlerts)
                        {
                            csv.WriteRecord(spikeAlert);
                            csv.NextRecord();
                        }
                    }
                }
                else//Create New file
                {
                    using (StreamWriter writer = new StreamWriter(resultFilePath))
                    using (CsvWriter csv = new CsvWriter(writer, csvConfig))
                    {
                        csv.WriteHeader<SpikeAlert>();
                        csv.NextRecord();

                        foreach (SpikeAlert spikeAlert in spikeAlerts)
                        {
                            csv.WriteRecord(spikeAlert);
                            csv.NextRecord();
                        }
                    }
                }
            }
            catch (Exception exp)
            {
                LoggerHelper.Error(exp, "Error creating the Csv file for Spike Detection");
            }
        }

        /// <summary>
        /// Stop
        /// </summary>
        public void Stop()
        {
            try
            {
                _spikeDetectionCts?.Cancel();

                //wait till task finishes
                _spikeDetectionTask?.Wait();
                _spikeDetectionTask = null;

                _spikeDetectionModelBuilderTask?.Wait();
                _spikeDetectionModelBuilderTask = null;

                _spikeDetectionCts?.Dispose();
                _spikeDetectionCts = null;
            }
            catch (Exception exp)
            {
                LoggerHelper.Error(exp, "Error while shutting spike detection task.");
            }
            Task.Delay(100);
        }

    }
}
