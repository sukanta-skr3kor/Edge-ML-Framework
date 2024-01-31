//*********************************************************************************************
//* File             :   MLAlgorithmType.cs
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

using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace Sukanta.Edge.ML.Model
{
    [JsonConverter(typeof(StringEnumConverter))]
    public enum MLAlgorithmType
    {
        Anomaly,
        Forecasting,
        SpikeDetection,
        ChangePointDetection
    }
}
