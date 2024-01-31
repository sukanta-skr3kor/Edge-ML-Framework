//*********************************************************************************************
//* File             :   ForecastData.cs
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

using System;

namespace Sukanta.Edge.ML.Model
{
    public class ForecastData
    {
        public string ParameterName { get; set; }

        public string Time { get; set; } = DateTime.Now.ToString();

        public int Count { get; set; }

        public float[] PredictedValues { get; set; }
    }
}
