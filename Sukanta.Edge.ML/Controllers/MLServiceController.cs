//*********************************************************************************************
//* File             :   MLServiceController.cs
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
using Sukanta.Edge.RuleEngine.Common;
using Sukanta.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.Options;
using Microsoft.ML;
using Microsoft.ML.TimeSeries;
using Microsoft.ML.Transforms.TimeSeries;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;

namespace Sukanta.Edge.ML.Controllers
{
    /// <summary>
    /// MLService Controller
    /// </summary>
    [Route("api/v1/mlservice")]
    [ApiController]
    public class MLServiceController : ControllerBase
    {
        /// <summary>
        /// RedisDataBus
        /// </summary>
        private readonly RedisDataBus _redisDataBus;

        /// <summary>
        ///  Settings
        /// </summary>
        private AppSettings _settings;

        /// <summary>
        /// DataServiceProvider
        /// </summary>
        private readonly IDataServiceProvider _dataServiceProvider;

        /// <summary>
        /// MLContext
        /// </summary>
        private readonly MLContext _mlContext;

        /// <summary>
        /// AnomalyDetection Algorithm Settings
        /// </summary>
        private readonly AnomalyDetectionAlgorithm _anomalyDetection;

        /// <summary>
        /// Forecasting Algorithm Settings
        /// </summary>
        private readonly ForecastingAlgorithm _forecasting;

        /// <summary>
        ///  MLService Controller
        /// </summary>
        /// <param name="redisDataBus"></param>
        /// <param name="dataServiceProvider"></param>
        /// <param name="mLContext"></param>
        /// <param name="settings"></param>
        /// <param name="anomalyDetectionSettings"></param>
        /// <param name="forecastingSettings"></param>
        public MLServiceController(RedisDataBus redisDataBus, DataServiceProvider dataServiceProvider, MLContext mLContext, IOptions<AppSettings> settings,
            IOptions<AnomalyDetectionAlgorithm> anomalyDetectionSettings, IOptions<ForecastingAlgorithm> forecastingSettings)
        {
            _settings = settings.Value;
            _redisDataBus = redisDataBus;
            _dataServiceProvider = dataServiceProvider;
            _mlContext = mLContext;
            _anomalyDetection = anomalyDetectionSettings.Value;
            _forecasting = forecastingSettings.Value;
        }

        /// <summary>
        /// Rule engine status
        /// </summary>
        /// <returns></returns>
        [HttpGet(@"status")]
        [ProducesResponseType((int)HttpStatusCode.OK)]
        [ProducesResponseType((int)HttpStatusCode.ServiceUnavailable)]
        public IActionResult Status()
        {
            try
            {
                return this.Ok("ONLINE");
            }
            catch (Exception exp)
            {
                return this.KnowOperationError(exp.Message);
            }
        }


        /// <summary>
        /// Timeseries anomaly
        /// </summary>
        /// <param name="parametername"></param>
        /// <param name="count"></param>
        /// <returns></returns>
        [HttpGet(@"anomaly/{parametername}/{count}")]
        [ProducesResponseType((int)HttpStatusCode.OK)]
        [ProducesResponseType((int)HttpStatusCode.BadRequest)]
        [ProducesResponseType((int)HttpStatusCode.NoContent)]
        [ProducesResponseType((int)HttpStatusCode.OK, Type = typeof(List<AnomalyData>))]
        public IActionResult DetectAnomaly(string parametername, int count = 50)
        {
            int index = 0;
            List<AnomalyData> anomalyData = new List<AnomalyData>();

            if (string.IsNullOrEmpty(parametername))
            {
                return this.KnowOperationError($"Parameter name is empty or null");
            }

            try
            {
                List<MachineTimeSeriesData> machineDatas = _dataServiceProvider.GetMachineTimeSeriesData(parametername, count);

                if (machineDatas.Count == 0)
                {
                    return this.NotFound($"No data found for the paramter '{parametername}'");
                }

                // Convert data to IDataView.
                IDataView dataView = _mlContext.Data.LoadFromEnumerable(machineDatas);

                // Setup estimator arguments
                string outputColumnName = nameof(MachineSrCnnAnomalyDetection.Prediction);
                string inputColumnName = nameof(MachineTimeSeriesData.Value);

                IDataView outputDataView = _mlContext.AnomalyDetection.DetectEntireAnomalyBySrCnn(dataView, outputColumnName, inputColumnName,
                  threshold: _anomalyDetection.Threshold, batchSize: _anomalyDetection.BatchSize, sensitivity: _anomalyDetection.Sensitivity,
                  detectMode: SrCnnDetectMode.AnomalyAndMargin);

                IEnumerable<MachineSrCnnAnomalyDetection> predictions = _mlContext.Data.CreateEnumerable<MachineSrCnnAnomalyDetection>
                                                                         (outputDataView, reuseRowObject: false);

                foreach (MachineSrCnnAnomalyDetection prediction in predictions)
                {
                    if (prediction.Prediction[0] == 1 && prediction.Prediction.Length > 3)//if anomaly
                    {
                        anomalyData.Add(new AnomalyData()
                        {
                            IsAnomaly = true,
                            ParameterName = parametername,
                            ActualValue = machineDatas[index].Value,
                            ExpectedValue = prediction.Prediction[3],
                            Time = machineDatas[index].Time
                        });
                    }

                    index++;
                }
                if (anomalyData.Count > 0)
                {
                    anomalyData = anomalyData.OrderBy(x => x.Time).ToList();
                    return this.Ok(anomalyData);
                }
                else
                {
                    return this.NoContent();
                }
            }
            catch (Exception exp)
            {
                return this.KnowOperationError(exp.Message);
            }
        }


        /// <summary>
        /// Forecast timeseries data
        /// </summary>
        /// <param name="parametername"></param>
        /// <param name="count"></param>
        /// <returns></returns>
        [HttpGet(@"forecast/{parametername}/{count}")]
        [ProducesResponseType((int)HttpStatusCode.OK)]
        [ProducesResponseType((int)HttpStatusCode.BadRequest)]
        [ProducesResponseType((int)HttpStatusCode.NoContent)]
        [ProducesResponseType((int)HttpStatusCode.OK, Type = typeof(ForecastData))]
        public IActionResult Forecast(string parametername, int count = 5)
        {
            ForecastData forecastData = new ForecastData()
            {
                ParameterName = parametername
            };

            if (string.IsNullOrEmpty(parametername))
            {
                return this.KnowOperationError($"Parameter name is empty or null");
            }

            try
            {
                string outputColumnName = nameof(MachineDataForecastResult.Forecast);
                string inputColumnName = nameof(MachineData.Value);

                List<MachineData> machineDatas = _dataServiceProvider.GetMachineData(parametername, _forecasting.TrainingDataSize);

                if (machineDatas.Count == 0)
                {
                    return this.NotFound($"No data found for the paramter '{parametername}'");
                }

                // Convert data to IDataView.
                IDataView dataView = _mlContext.Data.LoadFromEnumerable(machineDatas);

                SsaForecastingEstimator pipelinePrediction = _mlContext.Forecasting.ForecastBySsa(outputColumnName, inputColumnName,
                                windowSize: _forecasting.WindowSize, seriesLength: machineDatas.Count, trainSize: machineDatas.Count,
                                horizon: count, confidenceLevel: _forecasting.Confidence);

                SsaForecastingTransformer trainedModel = pipelinePrediction.Fit(dataView);

                TimeSeriesPredictionEngine<MachineData, MachineDataForecastResult> forecastEngine = trainedModel.CreateTimeSeriesEngine<MachineData,
                    MachineDataForecastResult>(_mlContext);

                MachineDataForecastResult result = forecastEngine.Predict();

                forecastData.PredictedValues = result.Forecast;

                if (forecastData.PredictedValues != null && forecastData.PredictedValues.Count() > 0)
                {
                    forecastData.Count = forecastData.PredictedValues.Count();
                    return this.Ok(forecastData);
                }
                else
                {
                    return NoContent();
                }
            }
            catch (Exception exp)
            {
                return this.KnowOperationError(exp.Message);
            }
        }


        /// <summary>
        ///Spike data
        /// </summary>
        /// <param name="parametername"></param>
        /// <returns></returns>
        [HttpGet(@"spike/{parametername}")]
        [ProducesResponseType((int)HttpStatusCode.OK)]
        [ProducesResponseType((int)HttpStatusCode.BadRequest)]
        [ProducesResponseType((int)HttpStatusCode.NotFound)]
        [ProducesResponseType((int)HttpStatusCode.OK, Type = typeof(List<SpikeAlert>))]
        public IActionResult GetSpikeData(string parametername)
        {
            if (string.IsNullOrEmpty(parametername))
            {
                return this.KnowOperationError($"Parameter name is empty or null");
            }

            string resultPath = _settings.ResultFileBasePath ?? "./MLResults";
            List<SpikeAlert> records = new List<SpikeAlert>();

            CsvConfiguration config = new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                IgnoreBlankLines = true,
            };

            try
            {
                string[] resultFiles = Directory.GetFiles(resultPath, "*" + MLConstants.SPIKE_RESULTFILE);

                if (resultFiles != null)
                {
                    string FileDate = DateTime.Now.ToString("yyyyMMdd");

                    string resultFilePath = Path.Combine(resultPath, FileDate + MLConstants.SPIKE_RESULTFILE);

                    if (System.IO.File.Exists(resultFilePath))
                    {
                        using (StreamReader reader = new StreamReader(resultFilePath))
                        using (CsvReader csv = new CsvReader(reader, config))
                        {
                            records = csv.GetRecords<SpikeAlert>().ToList();
                        }
                    }
                    else
                    {
                        return NotFound();
                    }
                }

                if (records.Count > 0)
                {
                    records = records.Where(x => x.ParameterName.Equals(parametername, StringComparison.OrdinalIgnoreCase))
                                 .OrderBy(x => x.Time).ToList();
                    return Ok(records);
                }

                return NoContent();
            }
            catch (Exception exp)
            {
                return this.KnowOperationError(exp.Message);
            }
        }

        /// <summary>
        ///changepoint data
        /// </summary>
        /// <param name="parametername"></param>
        /// <returns></returns>
        [HttpGet(@"changepoint/{parametername}")]
        [ProducesResponseType((int)HttpStatusCode.OK)]
        [ProducesResponseType((int)HttpStatusCode.BadRequest)]
        [ProducesResponseType((int)HttpStatusCode.NotFound)]
        [ProducesResponseType((int)HttpStatusCode.OK, Type = typeof(List<ChangePointAlert>))]
        public IActionResult GetChangePointData(string parametername)
        {
            if (string.IsNullOrEmpty(parametername))
            {
                return this.KnowOperationError($"Parameter name is empty or null");
            }

            string resultPath = _settings.ResultFileBasePath ?? "./MLResults";
            List<ChangePointAlert> records = new List<ChangePointAlert>();

            CsvConfiguration config = new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                IgnoreBlankLines = true,
            };

            try
            {
                string[] resultFiles = Directory.GetFiles(resultPath, "*" + MLConstants.CHANGEPOINT_RESULTFILE);

                if (resultFiles != null)
                {
                    string FileDate = DateTime.Now.ToString("yyyyMMdd");

                    string resultFilePath = Path.Combine(resultPath, FileDate + MLConstants.CHANGEPOINT_RESULTFILE);

                    if (System.IO.File.Exists(resultFilePath))
                    {
                        using (StreamReader reader = new StreamReader(resultFilePath))
                        using (CsvReader csv = new CsvReader(reader, config))
                        {
                            records = csv.GetRecords<ChangePointAlert>().ToList();
                        }
                    }
                    else
                    {
                        return NotFound();
                    }
                }

                if (records.Count > 0)
                {
                    records = records.Where(x => x.ParameterName.Equals(parametername, StringComparison.OrdinalIgnoreCase))
                                .OrderBy(x => x.Time).ToList();
                    return Ok(records);
                }

                return NoContent();
            }
            catch (Exception exp)
            {
                return this.KnowOperationError(exp.Message);
            }
        }

        /// <summary>
        ///  Download result file in csv format
        /// </summary>
        /// <param name="mlalgorithmtype"></param>
        /// <returns></returns>
        [HttpGet(@"result/download")]
        [ProducesResponseType((int)HttpStatusCode.OK)]
        [ProducesResponseType((int)HttpStatusCode.NotFound)]
        [ProducesResponseType((int)HttpStatusCode.BadRequest)]
        public IActionResult DownLoadResultFile(MLAlgorithmType mlalgorithmtype)
        {
            string resultPath = _settings.ResultFileBasePath ?? "./MLResults";
            FileInfo fileInfo = null;
            FileDownloadInfo fileDownloadInfo = null;
            string csvFilePath = string.Empty;

            if (!Directory.Exists(resultPath))
            {
                Directory.CreateDirectory(resultPath);
            }

            try
            {
                string FileDate = DateTime.Now.ToString("yyyyMMdd");

                if (mlalgorithmtype == MLAlgorithmType.Anomaly)
                {
                    string[] resultFiles = Directory.GetFiles(resultPath, "*" + MLConstants.ANOMALY_RESULTFILE);

                    if (resultFiles != null)
                    {
                        string resultFilePath = Path.Combine(resultPath, FileDate + MLConstants.ANOMALY_RESULTFILE);

                        if (System.IO.File.Exists(resultFilePath))
                        {
                            csvFilePath = resultFilePath;
                        }
                    }
                }
                else if (mlalgorithmtype == MLAlgorithmType.ChangePointDetection)
                {
                    string[] resultFiles = Directory.GetFiles(resultPath, "*" + MLConstants.SPIKE_RESULTFILE);

                    if (resultFiles != null)
                    {
                        string resultFilePath = Path.Combine(resultPath, FileDate + MLConstants.SPIKE_RESULTFILE);

                        if (System.IO.File.Exists(resultFilePath))
                        {
                            csvFilePath = resultFilePath;
                        }
                    }
                }
                else if (mlalgorithmtype == MLAlgorithmType.SpikeDetection)
                {
                    string[] resultFiles = Directory.GetFiles(resultPath, "*" + MLConstants.CHANGEPOINT_RESULTFILE);

                    if (resultFiles != null)
                    {
                        string resultFilePath = Path.Combine(resultPath, FileDate + MLConstants.CHANGEPOINT_RESULTFILE);

                        if (System.IO.File.Exists(resultFilePath))
                        {
                            csvFilePath = resultFilePath;
                        }
                    }
                }
                else if (mlalgorithmtype == MLAlgorithmType.Forecasting)
                {
                    string[] resultFiles = Directory.GetFiles(resultPath, "*" + MLConstants.FORECASING_RESULTFILE);

                    if (resultFiles != null)
                    {
                        string resultFilePath = Path.Combine(resultPath, FileDate + MLConstants.FORECASING_RESULTFILE);

                        if (System.IO.File.Exists(resultFilePath))
                        {
                            csvFilePath = resultFilePath;
                        }
                    }
                }
                else
                {
                    return this.KnowOperationError("Ml Algorithm type not supported by framwork");
                }

                if (!string.IsNullOrEmpty(csvFilePath))
                {
                    fileInfo = new FileInfo(csvFilePath);
                }
                else
                {
                    return this.NotFound("Result file not found");
                }

                if (fileInfo != null && fileInfo.Exists)
                {
                    FileExtensionContentTypeProvider contentProvider = new FileExtensionContentTypeProvider();

                    if (!contentProvider.TryGetContentType(fileInfo.Extension, out string contentType))
                    {
                        contentType = "application/octet-stream";
                    }

                    fileDownloadInfo = new FileDownloadInfo
                    {
                        ContentType = contentType,
                        File = fileInfo,
                        FileName = fileInfo.Name
                    };

                    return File(new FileStream(fileDownloadInfo.File.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite), fileDownloadInfo.ContentType, fileDownloadInfo.FileName);
                }

                return this.NotFound("Result file not found");
            }
            catch (Exception exp)
            {
                return this.KnowOperationError(exp.Message);
            }
        }

        /// <summary>
        /// Upload a pretrained Model file
        /// </summary>
        /// <param name="formFile"></param>
        /// <param name="mlalgorithmtype"></param>
        /// <returns></returns>
        [HttpPost(@"model/update")]
        [ProducesResponseType((int)HttpStatusCode.OK)]
        [ProducesResponseType((int)HttpStatusCode.NoContent)]
        [ProducesResponseType((int)HttpStatusCode.BadRequest)]
        public IActionResult UploadModelFile(IFormFile formFile, MLAlgorithmType mlalgorithmtype)
        {
            string modelFileBasePath = _settings.ModelFileBasePath ?? "./MLModels";

            if (formFile == null || string.IsNullOrEmpty(formFile.FileName))
            {
                return this.KnowOperationError($"File name is empty or null");
            }

            if (!formFile.FileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                return this.KnowOperationError("Not a valid model file, please choose a .zip file.");
            }

            //Check algorithm type and file type
            if (mlalgorithmtype == MLAlgorithmType.Anomaly)
            {
                return this.KnowOperationError($"Model files are not supported for {mlalgorithmtype.ToString()} type");
            }
            else if (mlalgorithmtype == MLAlgorithmType.Forecasting)
            {
                if (!formFile.FileName.ToLowerInvariant().Contains(MLConstants.FORECASING_MODELFILE.ToLowerInvariant()))
                {
                    return this.KnowOperationError($"Not a valid model file for forecasting model, please upload a file with name like '{"parameterXXXX" + MLConstants.FORECASING_MODELFILE}'");
                }
            }
            else if (mlalgorithmtype == MLAlgorithmType.SpikeDetection)
            {
                if (!formFile.FileName.ToLowerInvariant().Contains(MLConstants.SPIKE_MODELFILE.ToLowerInvariant()))
                {
                    return this.KnowOperationError($"Not a valid model file for SpikeDetection model, please upload a file with name like '{"parameterXXXX" + MLConstants.SPIKE_MODELFILE}'");
                }
            }
            else if (mlalgorithmtype == MLAlgorithmType.ChangePointDetection)
            {
                if (!formFile.FileName.ToLowerInvariant().Contains(MLConstants.CHANGEPOINT_MODELFILE.ToLowerInvariant()))
                {
                    return this.KnowOperationError($"Not a valid model file for ChangeDetection model, please upload a file with name like '{"parameterXXXX" + MLConstants.CHANGEPOINT_MODELFILE}'");
                }
            }
            else
            {
                return this.KnowOperationError($"Algorithm type or file type not supported by ML framework.");
            }

            try
            {
                if (!Directory.Exists(modelFileBasePath))
                {
                    Directory.CreateDirectory(modelFileBasePath);
                }

                FileInfo destFile = Reflections.GetRootRelativeFile(modelFileBasePath, formFile.FileName);

                if (!destFile.Directory.Exists)
                {
                    destFile.Directory.Create();
                }

                using (FileStream stream = new FileStream(destFile.FullName, FileMode.Create))
                {
                    formFile.CopyTo(stream);
                }

                return this.Ok($"{formFile.FileName} model file uploaded successfully to {destFile} location");
            }
            catch (Exception exp)
            {
                return this.KnowOperationError(exp.Message);
            }
        }

    }
}
