//*********************************************************************************************
//* File             :   IMLAlertNotification.cs
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
using System.Threading.Tasks;

namespace Sukanta.Edge.ML.NotificationHub
{
    /// <summary>
    /// AlertNotification contract
    /// </summary>
    public interface IMLAlertNotification
    {
        /// <summary>
        /// Send Anomaly Alerts For a Parameter
        /// </summary>
        /// <param name="parameterName"></param>
        /// <param name="anomalyAlert"></param>
        /// <returns></returns>
        Task SendAnomalyAlertForParameter(string parameterName, AnomalyAlert anomalyAlert);

        /// <summary>
        /// Send Forecasted Values For Parameter
        /// </summary>
        /// <param name="parameterName"></param>
        /// <param name="forecastedValues"></param>
        /// <returns></returns>
        Task SendForecastedValuesForParameter(string parameterName, string forecastedValues);

        /// <summary>
        /// Send Spikes For arameter
        /// </summary>
        /// <param name="parameterName"></param>
        /// <param name="spikeAlert"></param>
        /// <returns></returns>
        Task SendSpikeAlertForParameter(string parameterName, SpikeAlert spikeAlert);

        /// <summary>
        /// Send ChangePoit Value For arameter
        /// </summary>
        /// <param name="parameterName"></param>
        /// <param name="changePointAlert"></param>
        /// <returns></returns>
        Task SendChangePointAlertForParameter(string parameterName, ChangePointAlert changePointAlert);
    }
}