//*********************************************************************************************
//* File             :   ForecastingEngine.cs
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
using System.Collections.Generic;
using System.Dynamic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Sukanta.Edge.ML.Forecasting
{
    /// <summary>
    /// Forecasting
    /// </summary>
    public class ForecastingEngine
    {
        private Task _forcastingTask;
        private Task _forecastingModelBuilderTask;
        private CancellationTokenSource _forecastingCts;
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
        private ForecastingAlgorithm _forecasting { get; set; }

        /// <summary>
        /// SignalR Client
        /// </summary>
        private MLAlertNotificationClient _notifcationClient;

        /// <summary>
        /// ForecastingEngine
        /// </summary>
        /// <param name="dataServiceProvider"></param>
        /// <param name="redisDataBus"></param>
        /// <param name="mLAlertNotificationClient"></param>
        /// <param name="forecastingSettings"></param>
        public ForecastingEngine(DataServiceProvider dataServiceProvider, RedisDataBus redisDataBus, MLAlertNotificationClient mLAlertNotificationClient,
            ForecastingAlgorithm forecastingSettings)
        {
            _dataServiceProvider = dataServiceProvider;
            _redisDataBus = redisDataBus;
            _forecasting = forecastingSettings;
            _notifcationClient = mLAlertNotificationClient;

            _forecastingCts = new CancellationTokenSource();
            _shutdownToken = _forecastingCts.Token;

            BaseModelsRelativePath = _forecasting.ModelFilePath ?? BaseModelsRelativePath;
            ResultCsvRelativePath = _forecasting.ResultCsvFilePath ?? ResultCsvRelativePath;
        }

        /// <summary>
        /// Start
        /// </summary>
        public void Start()
        {
            if (_forecasting.Enabled)
            {
                if (_forecasting.BuildLocalModel)
                {
                    //Create the directory if not exists
                    if (!Directory.Exists(BaseModelsRelativePath))
                    {
                        Directory.CreateDirectory(BaseModelsRelativePath);
                    }

                    _forecastingModelBuilderTask = Task.Run(() => BuildModel(_shutdownToken), _shutdownToken);
                }

                LoggerHelper.Information("Starting Forecasting Engine...");
                _forcastingTask = Task.Run(() => ExecuteForecasting(_shutdownToken), _shutdownToken);
                LoggerHelper.Information("Forecasting Engine Started");
            }
        }

        /// <summary>
        /// BuildModel
        /// </summary>
        /// <param name="shutdownToken"></param>
        private void BuildModel(CancellationToken shutdownToken)
        {
            int WindowSize = _forecasting.WindowSize;
            int numberOfValuesToPredit = _forecasting.NumberOfValuesToPredit;
            float confidenceLevel = _forecasting.Confidence;
            int SeriesLength = _forecasting.TrainingDataSize > 0 ? _forecasting.TrainingDataSize : 5000;
            int TrainingSize = _forecasting.TrainingDataSize > 0 ? _forecasting.TrainingDataSize : 5000;

            string outputColumnName = nameof(MachineDataForecastResult.Forecast);
            string inputColumnName = nameof(MachineData.Value);

            int ExecutionInterval = _forecasting.LocalModelBuildIntervalMinutes * 1000 * 60;

            Task.Run(async () =>
            {
                while (true)
                {
                    try
                    {
                        //Add data from Redis DB
                        foreach (Parameter parameter in _forecasting.MachineParameters)
                        {
                            List<MachineData> machineDatas = new List<MachineData>();

                            machineDatas = _dataServiceProvider.GetMachineData(parameter.Id, TrainingSize);

                            if (machineDatas.Count > 0)
                            {
                                machineDatas.AddRange(machineDatas);

                                // Convert data to IDataView.
                                IDataView dataView = _mlContext.Data.LoadFromEnumerable(machineDatas);

                                SsaForecastingEstimator pipelinePrediction = _mlContext.Forecasting.ForecastBySsa(outputColumnName, inputColumnName,
                                                windowSize: WindowSize, seriesLength: SeriesLength, trainSize: TrainingSize,
                                                horizon: numberOfValuesToPredit, confidenceLevel: confidenceLevel);

                                SsaForecastingTransformer trainedModel = pipelinePrediction.Fit(dataView);

                                TimeSeriesPredictionEngine<MachineData, MachineDataForecastResult> forecastEngine = trainedModel.CreateTimeSeriesEngine<MachineData,
                                    MachineDataForecastResult>(_mlContext);

                                string modelFilePath = Path.Combine(BaseModelsRelativePath, parameter.Id + MLConstants.FORECASING_MODELFILE);

                                // Save the trained model to a .ZIP file
                                forecastEngine.CheckPoint(_mlContext, modelFilePath);
                            }
                            else
                            {
                                LoggerHelper.Warning($"No data received for {parameter.Id} from databus");
                            }
                        }
                    }
                    catch (Exception exp)
                    {
                        LoggerHelper.Error(exp, $"Error executing Forecasting Model Builder");
                    }

                    await Task.Delay(ExecutionInterval);//sleep
                }
            }, shutdownToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Forecast machine values
        /// </summary>
        /// <param name="_shutdownToken"></param>
        private void ExecuteForecasting(CancellationToken _shutdownToken)
        {
            int ExecutionInterval = _forecasting.ExecutionIntervalSeconds * 1000;
            int horizon = _forecasting.NumberOfValuesToPredit;

            Task.Run(async () =>
            {
                while (true)
                {
                    try
                    {
                        string[] modelFiles = Directory.GetFiles(BaseModelsRelativePath, "*.zip");

                        foreach (Parameter parameter in _forecasting.MachineParameters)
                        {
                            string fileName = Path.Combine(BaseModelsRelativePath, parameter.Id + MLConstants.FORECASING_MODELFILE).ToLowerInvariant();
                            string modelFile = modelFiles.FirstOrDefault(x => x.ToLowerInvariant() == fileName);

                            if (modelFile == null)
                            {
                                LoggerHelper.Error($"Foreasting model file doesn't exist for parameter '{parameter.Id}'");
                            }

                            if (!string.IsNullOrEmpty(modelFile))
                            {
                                if (modelFile.ToLowerInvariant().Contains(MLConstants.FORECASING_MODELFILE.ToLowerInvariant()))
                                {
                                    ITransformer trainedModel = _mlContext.Model.Load(modelFile, out DataViewSchema modelInputSchema);

                                    TimeSeriesPredictionEngine<MachineData, MachineDataForecastResult> forecastEngine =
                                    trainedModel.CreateTimeSeriesEngine<MachineData, MachineDataForecastResult>(_mlContext);

                                    //Predict
                                    MachineDataForecastResult forecast = forecastEngine.Predict(horizon);

                                    //Generate csv file
                                    if (_forecasting.ResultCsvGenerationEnabled)
                                    {
                                        GenerateCsvFile(parameter.Id, forecast);
                                    }

                                    //Send notification to server
                                    if (_forecasting.NotificationEnabled)
                                    {
                                        await _notifcationClient.SendForecastedValuesForParameter(parameter.Id, string.Join(", ", forecast.Forecast)).ConfigureAwait(false);
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception exp)
                    {
                        LoggerHelper.Error(exp, $"Error executing Forecasting algorithm");
                    }

                    await Task.Delay(ExecutionInterval);//sleep  
                }
            }, _shutdownToken).ConfigureAwait(false);
        }


        /// <summary>
        ///  Generate Csv File
        /// </summary>
        /// <param name="parameterName"></param>
        /// <param name="forecast"></param>
        private void GenerateCsvFile(string parameterName, MachineDataForecastResult forecast)
        {
            CsvConfiguration csvConfig = new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                HasHeaderRecord = true,
                Delimiter = ",",
            };

            try
            {
                string resultFilePath = Path.Combine(ResultCsvRelativePath, DateTime.Now.ToString("yyyyMMdd") + MLConstants.FORECASING_RESULTFILE);

                if (!Directory.Exists(_forecasting.ResultCsvFilePath))
                {
                    Directory.CreateDirectory(_forecasting.ResultCsvFilePath);
                }

                if (File.Exists(resultFilePath))//Append to existing file
                {
                    csvConfig.HasHeaderRecord = false;//No header

                    using (FileStream stream = File.Open(resultFilePath, FileMode.Append))
                    using (StreamWriter writer = new StreamWriter(stream))
                    using (CsvWriter csv = new CsvWriter(writer, csvConfig))
                    {
                        dynamic forecastData = new ExpandoObject();
                        forecastData.Time = DateTime.Now;
                        forecastData.ParameterName = parameterName;
                        forecastData.Values = forecast.Forecast;

                        csv.WriteRecord(forecastData);
                        csv.NextRecord();
                    }
                }
                else//Create New file
                {
                    using (StreamWriter writer = new StreamWriter(resultFilePath))
                    using (CsvWriter csv = new CsvWriter(writer, csvConfig))
                    {
                        dynamic forecastData = new ExpandoObject();
                        forecastData.Time = DateTime.Now;
                        forecastData.ParameterName = parameterName;
                        forecastData.Values = forecast.Forecast;

                        csv.WriteRecord(forecastData);
                        csv.NextRecord();
                    }
                }
            }
            catch (Exception exp)
            {
                LoggerHelper.Error(exp, "Error creating the Csv file for Forecasting Data");
            }
        }

        /// <summary>
        /// Stop
        /// </summary>
        public void Stop()
        {
            try
            {
                _forecastingCts?.Cancel();

                //wait till task finishes
                _forcastingTask?.Wait();
                _forcastingTask = null;

                _forecastingModelBuilderTask?.Wait();
                _forecastingModelBuilderTask = null;

                _forecastingCts?.Dispose();
                _forecastingCts = null;
            }
            catch (Exception exp)
            {
                LoggerHelper.Error(exp, "Error while shutting Forecasting task.");
            }
            Task.Delay(100);
        }

    }
}
