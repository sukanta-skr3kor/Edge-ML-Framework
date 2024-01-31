﻿//*********************************************************************************************
//* File             :   MachineSrCnnAnomalyDetection.cs
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
    /// Machine SrCnnAnomalyDetection
    /// </summary>
    public class MachineSrCnnAnomalyDetection
    {
        [VectorType]
        public double[] Prediction { get; set; }
    }
}
