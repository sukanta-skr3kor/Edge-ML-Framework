//*********************************************************************************************
//* File             :   MachineDataSpikePrediction.cs
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
    /// SpikePrediction
    /// </summary>
    public class MachineDataSpikePrediction
    {
        [VectorType(3)]
        public double[] Prediction { get; set; }
    }
}
