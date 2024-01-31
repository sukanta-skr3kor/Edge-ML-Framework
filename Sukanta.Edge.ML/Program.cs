//*********************************************************************************************
//* File             :   Program.cs
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

using Sukanta.Edge.ML.Common;
using Sukanta.Edge.ML.Model;
using Sukanta.LoggerLib;
using Sukanta.Reflection;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using Microsoft.Extensions.Configuration;
using Serilog;
using System;
using System.IO;
using System.Net;

namespace Sukanta.Edge.RuleEngine
{
    /// <summary>
    /// Start of program
    /// </summary>
    public class Program
    {
        /// <summary>
        /// Main entry point
        /// </summary>
        /// <param name="args"></param>
        public static void Main(string[] args)
        {
            //Base directory setting     
            DirectoryInfo baseDirectory = Reflections.GetEntryAssemblyLocation();
            Directory.SetCurrentDirectory(baseDirectory.FullName);

            //Configure logger
            Log.Logger = LoggerHelper.ConfigureLogger();

            //Dislay framework info
            DisplayFrameworkInformation();

            CreateWebHostBuilder(args).Build().Run();
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="args"></param>
        /// <returns></returns>
        public static IWebHostBuilder CreateWebHostBuilder(string[] args)
        {
            //Configuration load
            IConfigurationRoot config = new ConfigurationBuilder()
                   .SetBasePath(Reflections.GetCurrentAssemblyLocation().FullName)
                   .AddJsonFile("AppSettings.json", optional: false, reloadOnChange: true)
                   .AddJsonFile("MLConfiguration/Anomaly.json", optional: true, reloadOnChange: true)
                   .AddJsonFile("MLConfiguration/SpikeDetection.json", optional: true, reloadOnChange: true)
                   .AddJsonFile("MLConfiguration/ChangePointDetection.json", optional: true, reloadOnChange: true)
                   .AddJsonFile("MLConfiguration/Forecasting.json", optional: true, reloadOnChange: true)
                   .Build();


            IConfigurationSection appSettingsSection = config.GetSection("AppSettings");

            AppSettings appSettings = appSettingsSection.Get<AppSettings>();


            //Bind to localhost only, for security reasons
            string BindAddress = config["AppSettings:Binding"];

            //If nothing mentioned bind to localhost
            if (string.IsNullOrEmpty(BindAddress))
            {
                BindAddress = "localhost";
            }

            //Url
            string url = $"http://{BindAddress}:{config["AppSettings:HttpPort"]}";

            //Webhost
            return WebHost.CreateDefaultBuilder(args)
            .UseConfiguration(config)
             .UseKestrel(options =>
             {
                 options.AddServerHeader = false;

                 options.Limits.MaxRequestBodySize = null;

                 if (appSettings.UseHttps)
                 {
                     options.Listen(IPAddress.Any, appSettings.Port, listenOptions =>
                     {
                         listenOptions.UseHttps(appSettings.CertificateFile, DataEncrypter.Decrypt(appSettings.CertificatePassword));
                     });
                 }

                 if (appSettings.UseMutualTls)
                 {
                     options.Listen(IPAddress.Any, appSettings.MutualTlsPort, listenOptions =>
                     {
                         listenOptions.UseHttps(appSettings.CertificateFile, DataEncrypter.Decrypt(appSettings.CertificatePassword), kerstelOptions =>
                         {
                             kerstelOptions.ClientCertificateMode = ClientCertificateMode.RequireCertificate;
                             kerstelOptions.SslProtocols = System.Security.Authentication.SslProtocols.Tls12;
                         });
                     });
                 }

                 if (appSettings.UseHttp)
                 {
                     options.Listen(IPAddress.Any, appSettings.HttpPort);
                 }
             })
            .UseUrls(url)
            .UseStartup<Startup>();
        }

        /// <summary>
        /// Print RuleEngine Framework Information
        /// </summary>
        private static void DisplayFrameworkInformation()
        {
            LoggerHelper.Information("******************************************************************");
            LoggerHelper.Information($@"Edge ML Service Application v{Reflections.GetAppVersion()}");
            LoggerHelper.Information($"Copyright (c) 20242-{DateTime.Today.Year} SIEMENS ADV");
            LoggerHelper.Information("******************************************************************");
        }
    }
}
