//*********************************************************************************************
//* File             :   MachineData.cs
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

using Microsoft.ML.Data;

namespace Sukanta.Edge.ML.Model
{
    /// <summary>
    /// Base class fpr data
    /// </summary>
    public class Data
    { }

    /// <summary>
    /// TimeSeriesData
    /// </summary>
    public class MachineTimeSeriesData : Data
    {
        /// <summary>
        /// Parameter ID
        /// </summary>
        public string Id { get; set; }

        /// <summary>
        /// Value
        /// </summary>
        public double Value { get; set; }

        /// <summary>
        /// Time
        /// </summary>
        public string Time { get; set; }

    }

    /// <summary>
    /// Parameter and data
    /// </summary>
    public class MachineData : Data
    {
        [LoadColumn(0)]
        public string Id;

        [LoadColumn(1)]
        public float Value;

        [LoadColumn(2)]
        public string Time;
    }
}
