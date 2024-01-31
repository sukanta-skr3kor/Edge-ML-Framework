//*********************************************************************************************
//* File             :   AnomalyData.cs
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

using CsvHelper.Configuration.Attributes;

namespace Sukanta.Edge.ML.Model
{
    public class AnomalyData
    {
        public string ParameterName { get; set; }

        [BooleanTrueValues("Yes")]
        [BooleanFalseValues("No")]
        public bool IsAnomaly { get; set; }

        public double ActualValue { get; set; }

        public double ExpectedValue { get; set; }

        public string Time { get; set; }
    }
}
