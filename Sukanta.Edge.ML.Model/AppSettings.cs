//*********************************************************************************************
//* File             :   AppSettings.cs
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

using System.Collections.Generic;

namespace Sukanta.Edge.ML.Model
{
    /// <summary>
    /// RuleEngine Settings
    /// </summary>
    public class AppSettings
    {
        /// <summary>
        /// Notification to server ?
        /// </summary>
        public bool NotificationEnabled { get; set; }

        /// <summary>
        /// Http port number
        /// </summary>
        public int HttpPort { get; set; }

        /// <summary>
        /// When set to true https connection and redirection will be enabled
        /// </summary>
        public bool UseHttps { get; set; }

        /// <summary>
        /// Enable http
        /// </summary>
        public bool UseHttp { get; set; }

        /// <summary>
        /// Https port number
        /// </summary>
        public int Port { get; set; }

        /// <summary>
        /// Enable mutual TLS
        /// </summary>
        public bool UseMutualTls { get; set; }

        /// <summary>
        /// TLS port number
        /// </summary>
        public int MutualTlsPort { get; set; }


        /// <summary>
        /// Binding IP Address
        /// </summary>
        public string Binding { get; set; } = "localhost";

        /// <summary>
        /// Signalr Hub Url on servr
        /// </summary>
        public string SignalrHubUrl { get; set; }

        /// <summary>
        /// Use Certificate ?
        /// </summary>
        public bool UseCertificate { get; set; }

        /// <summary>
        /// Client certificate
        /// </summary>
        public string CertificateFile { get; set; }

        /// <summary>
        /// Certificate Password
        /// </summary>
        public string CertificatePassword { get; set; }

        /// <summary>
        /// DataBus Settings
        /// </summary>
        public DataBusSettings DataBusSettings { get; set; }

        /// <summary>
        /// ModelFile Base Path
        /// </summary>
        public string ModelFileBasePath { get; set; } = "./MLModels";

        /// <summary>
        /// CSV result file base path
        /// </summary>
        public string ResultFileBasePath { get; set; } = "./MLResuts";

        /// <summary>
        /// Number Of Days To Keep the Result File 
        /// Set -1 for no deletion
        /// </summary>
        public int NoOfDaysToKeepResultFile { get; set; } = 7;//default 7 days

    }

    /// <summary>
    /// DataBus Settings
    /// </summary>
    public class DataBusSettings
    {
        /// <summary>
        /// Databus type
        /// </summary>
        public string Type { get; set; }

        /// <summary>
        /// DataBus Name
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Is Databus enabled
        /// </summary>
        public bool Enabled { get; set; }

        /// <summary>
        /// Redis Server url
        /// </summary>
        public string Server { get; set; } = "localhost:6379";

        /// <summary>
        /// Subscribe topic name
        /// </summary>
        public string SubscribeTopic { get; set; } = "datamessage";

        /// <summary>
        /// Publish topic name
        /// </summary>
        public string PublishTopic { get; set; } = "ml/alertmessage";

        /// <summary>
        /// If redis DB store enabled ?
        /// </summary>
        public bool DBPersistEnabled { get; set; }

        /// <summary>
        /// Data persist delay
        /// </summary>
        public int CollectionIntervalSeconds { get; set; } = 1;//default 1 sec

        /// <summary>
        /// If redis stream enabled ?
        /// </summary>
        public bool DBStreamEnabled { get; set; }

        /// <summary>
        /// Publish Alrt messages ?
        /// </summary>
        public bool AlertMessageEnabled { get; set; }

        /// <summary>
        /// Redis Stream length
        /// </summary>
        public int StreamLength { get; set; } = 1000;
    }

    /// <summary>
    /// parameter
    /// </summary>
    public class Parameter
    {
        /// <summary>
        /// ID of the parameter
        /// </summary>
        public string Id { get; set; }
    }

    /// <summary>
    /// SpikeDetection Algorithm Settings
    /// </summary>
    public class SpikeDetectionAlgorithm
    {
        public bool Enabled { get; set; }

        public int TrainingDataSize { get; set; } = 5000;

        public float Confidence { get; set; } = 85;

        public int SeasonalitySize { get; set; } = 8;

        public int PValueSize { get; set; } = 5;

        public bool BuildLocalModel { get; set; }

        public string ModelFilePath { get; set; }

        public int ExecutionIntervalSeconds { get; set; } = 300;

        public int LocalModelBuildIntervalMinutes { get; set; } = 60;

        public int PredictionDataSize { get; set; } = 100;

        public bool NotificationEnabled { get; set; }

        public bool ResultCsvGenerationEnabled { get; set; }

        public string ResultCsvFilePath { get; set; }

        public List<Parameter> MachineParameters { get; set; }
    }

    /// <summary>
    /// ChangePointDetection Algorithm Settings
    /// </summary>
    public class ChangePointDetectionAlgorithm
    {
        public bool Enabled { get; set; }

        public int TrainingDataSize { get; set; } = 5000;

        public float Confidence { get; set; } = 85;

        public int SeasonalitySize { get; set; } = 8;

        public int PValueSize { get; set; } = 5;

        public bool BuildLocalModel { get; set; }

        public string ModelFilePath { get; set; }

        public int ExecutionIntervalSeconds { get; set; } = 300;

        public int LocalModelBuildIntervalMinutes { get; set; } = 60;

        public int PredictionDataSize { get; set; } = 100;

        public bool NotificationEnabled { get; set; }

        public bool ResultCsvGenerationEnabled { get; set; }

        public string ResultCsvFilePath { get; set; }

        public List<Parameter> MachineParameters { get; set; }
    }

    /// <summary>
    /// Forecasting Algorithm Settings
    /// </summary>
    public class ForecastingAlgorithm
    {
        public bool Enabled { get; set; }

        public int TrainingDataSize { get; set; } = 5000;

        public float Confidence { get; set; } = 85;

        public int NumberOfValuesToPredit { get; set; } = 10;

        public int WindowSize { get; set; } = 60;

        public bool BuildLocalModel { get; set; }

        public string ModelFilePath { get; set; }

        public int ExecutionIntervalSeconds { get; set; }

        public int LocalModelBuildIntervalMinutes { get; set; } = 60;

        public bool NotificationEnabled { get; set; }

        public bool ResultCsvGenerationEnabled { get; set; }

        public string ResultCsvFilePath { get; set; }

        public List<Parameter> MachineParameters { get; set; }
    }

    /// <summary>
    /// AnomalyDetection Algorithm Settings
    /// </summary>
    public class AnomalyDetectionAlgorithm
    {
        public bool Enabled { get; set; }

        public int PredictionDataSize { get; set; } = 100;

        public double Threshold { get; set; } = 0;

        public double Sensitivity { get; set; } = 90;

        public int BatchSize { get; set; } = 512;

        public int ExecutionIntervalSeconds { get; set; } = 300;

        public bool NotificationEnabled { get; set; }

        public bool ResultCsvGenerationEnabled { get; set; }

        public string ResultCsvFilePath { get; set; }

        public List<Parameter> MachineParameters { get; set; }
    }
}

