//*********************************************************************************************
//* File             :   MLAlertNotificationClient.cs
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
using Microsoft.AspNetCore.SignalR.Client;
using System;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;

namespace Sukanta.Edge.ML.NotificationHub
{
    /// <summary>
    /// Alert NotificationHub Client
    /// </summary>
    public class MLAlertNotificationClient : IDisposable, IMLAlertNotification
    {
        /// <summary>
        /// NotificationHub connector
        /// </summary>
        public HubConnection NotificationHub { get; set; }

        /// <summary>
        /// ML App Settings
        /// </summary>
        private AppSettings _appSettings { get; set; }

        /// <summary>
        /// AlertNotificationClient
        /// </summary>
        /// <param name="appSettings"></param>
        public MLAlertNotificationClient(AppSettings appSettings)
        {
            _appSettings = appSettings;
            Initialize();
        }

        /// <summary>
        /// Initialize Hub
        /// </summary>
        public void Initialize()
        {
            try
            {
                if (NotificationHub == null)
                {
                    BuildNotificationHub();
                }
                else if (NotificationHub.State == HubConnectionState.Disconnected)
                {
                    NotificationHub.DisposeAsync();
                    NotificationHub = null;
                    BuildNotificationHub();
                }
            }
            catch { }
        }

        /// <summary>
        /// BuildNotificationHub
        /// </summary>
        private void BuildNotificationHub()
        {
            IHubConnectionBuilder builder = new HubConnectionBuilder()
                .WithAutomaticReconnect(new[] { TimeSpan.Zero, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5) })//Auto reconnect
                .WithUrl(_appSettings.SignalrHubUrl, options =>
                {
                    //Certificate handling 
                    if (_appSettings.UseCertificate)
                    {
                        if (!string.IsNullOrEmpty(_appSettings.CertificateFile))
                        {
                            X509Certificate2 certificateFile = new X509Certificate2(_appSettings.CertificateFile);
                            options.ClientCertificates.Add(certificateFile);
                        }
                    }
                });

            //Build hub
            NotificationHub = builder.Build();

            //On Close connection retry to connect
            NotificationHub.Closed += async (error) =>
            {
                await Task.Delay(new Random().Next(0, 5) * 1000);
                await NotificationHub.StartAsync();
            };

            //Start hub client
            NotificationHub.StartAsync();
        }

        /// <summary>
        /// Init Hub if disconnected
        /// </summary>
        private bool IsHubInitialized()
        {
            if (NotificationHub == null || NotificationHub.State == HubConnectionState.Disconnected)
            {
                Initialize();
            }

            return NotificationHub.State == HubConnectionState.Connected;
        }

        /// <summary>
        /// Dispose Hub Client
        /// </summary>
        public void Dispose()
        {
            if (NotificationHub != null)
            {
                NotificationHub.DisposeAsync();
                NotificationHub = null;
            }
        }

        /// <summary>
        /// Send Anomaly Alerts For a Parameter
        /// </summary>
        /// <param name="parameterName"></param>
        /// <param name="anomalyAlert"></param>
        /// <returns></returns>
        public async Task SendAnomalyAlertForParameter(string parameterName, AnomalyAlert anomalyAlert)
        {
            if (IsHubInitialized())
            {
                await NotificationHub.InvokeAsync("AnomalyAlertForParameter", parameterName, anomalyAlert).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Send Forecasted Values For Parameter
        /// </summary>
        /// <param name="parameterName"></param>
        /// <param name="forecastedValues"></param>
        /// <returns></returns>
        public async Task SendForecastedValuesForParameter(string parameterName, string forecastedValues)
        {
            if (IsHubInitialized())
            {
                await NotificationHub.InvokeAsync("ForecastForParameter", parameterName, forecastedValues).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Send Spike Values For Parameter
        /// </summary>
        /// <param name="parameterName"></param>
        /// <param name="spikeAlert"></param>
        /// <returns></returns>
        public async Task SendSpikeAlertForParameter(string parameterName, SpikeAlert spikeAlert)
        {
            if (IsHubInitialized())
            {
                await NotificationHub.InvokeAsync("SpikeForParameter", parameterName, spikeAlert).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Send ChangePoint Value For Parameter
        /// </summary>
        /// <param name="parameterName"></param>
        /// <param name="changePointAlert"></param>
        /// <returns></returns>
        public async Task SendChangePointAlertForParameter(string parameterName, ChangePointAlert changePointAlert)
        {
            if (IsHubInitialized())
            {
                await NotificationHub.InvokeAsync("ChangePointForParameter", parameterName, changePointAlert).ConfigureAwait(false);
            }
        }

    }
}
