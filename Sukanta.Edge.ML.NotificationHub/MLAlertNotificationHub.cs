//*********************************************************************************************
//* File             :   MLAlertNotificationHub.cs
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
using Microsoft.AspNetCore.SignalR;
using System.Threading.Tasks;

namespace Sukanta.Edge.ML.NotificationHub
{
    /// <summary>
    /// Notification server Hub
    /// </summary>
    public class MLAlertNotificationHub : Hub
    {
        /// <summary>
        /// Send Anomaly Alert For Parameter
        /// </summary>
        /// <param name="parameterName"></param>
        /// <param name="mLAlert"></param>
        /// <returns></returns>
        public async Task AnomalyAlertForParameter(string parameterName, AnomalyAlert mLAlert)
        {
            try
            {
                if (Clients != null)
                {
                    await Clients.All.SendAsync("AnomalyAlertForParameter", parameterName, mLAlert);
                }
            }
            catch { }
        }


        /// <summary>
        /// Send Forecasted Values For Parameter
        /// </summary>
        /// <param name="parameterName"></param>
        /// <param name="forecastedValues"></param>
        /// <returns></returns>
        public async Task ForecastForParameter(string parameterName, string forecastedValues)
        {
            try
            {
                if (Clients != null)
                {
                    await Clients.All.SendAsync("ForecastForParameter", parameterName, forecastedValues);
                }
            }
            catch { }
        }

        /// <summary>
        /// Send Spike Values For Parameter
        /// </summary>
        /// <param name="parameterName"></param>
        /// <param name="spikeAlert"></param>
        /// <returns></returns>
        public async Task SpikeForParameter(string parameterName, SpikeAlert spikeAlert)
        {
            try
            {
                if (Clients != null)
                {
                    await Clients.All.SendAsync("SpikeForParameter", parameterName, spikeAlert);
                }
            }
            catch { }
        }

        /// <summary>
        /// Send Change Point Values For Parameter
        /// </summary>
        /// <param name="parameterName"></param>
        /// <param name="changePointAlert"></param>
        /// <returns></returns>
        public async Task ChangePointForParameter(string parameterName, ChangePointAlert changePointAlert)
        {
            try
            {
                if (Clients != null)
                {
                    await Clients.All.SendAsync("ChangePointForParameter", parameterName, changePointAlert);
                }
            }
            catch { }
        }
    }
}
