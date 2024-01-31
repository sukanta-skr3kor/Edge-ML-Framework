//*********************************************************************************************
//* File             :   IDataServiceProvider.cs
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

using Sukanta.Edge.ML.Model;
using System.Collections.Generic;

namespace Sukanta.Edge.DataService
{
    public interface IDataServiceProvider
    {
        List<MachineData> GetMachineData(string streamSource, int count = 1000);

        List<MachineTimeSeriesData> GetMachineTimeSeriesData(string streamSource, int count = 1000);
    }
}
