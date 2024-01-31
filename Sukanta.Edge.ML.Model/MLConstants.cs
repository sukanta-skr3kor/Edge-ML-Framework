//*********************************************************************************************
//* File             :   MLConstants.cs
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

namespace Sukanta.Edge.ML.Model
{
    public static class MLConstants
    {
        //Model file names
        public const string CHANGEPOINT_MODELFILE = "_ChangePointDetectionModel.zip";
        public const string FORECASING_MODELFILE = "_ForecastingModel.zip";
        public const string SPIKE_MODELFILE = "_SpikeDetectionModel.zip";

        //Result file names
        public const string CHANGEPOINT_RESULTFILE = "_ChangePointData.csv";
        public const string ANOMALY_RESULTFILE = "_AnomalyData.csv";
        public const string SPIKE_RESULTFILE = "_SpikeData.csv";
        public const string FORECASING_RESULTFILE = "_ForecastingData.csv";
    }
}

