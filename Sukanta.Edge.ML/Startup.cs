//*********************************************************************************************
//* File             :   Startup.cs
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

using Sukanta.DataBus.Redis;
using Sukanta.Edge.DataService;
using Sukanta.Edge.ML.Model;
using Sukanta.Edge.ML.NotificationHub;
using Sukanta.Edge.ML.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.ML;
using Microsoft.OpenApi.Models;
using Newtonsoft.Json.Converters;

namespace Sukanta.Edge.RuleEngine
{
    /// <summary>
    /// Startup class
    /// </summary>
    public class Startup
    {
        /// <summary>
        /// Notification hub path
        /// </summary>
        private const string NOTIFICATION_HUB_PATH = "/hubs/ml/alert";

        /// <summary>
        /// Configurations
        /// </summary>
        public IConfiguration Configuration { get; }

        /// <summary>
        /// Startup
        /// </summary>
        /// <param name="configuration"></param>
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        /// <summary>
        ///This method gets called by the runtime. Use this method to add services to the container.
        /// </summary>
        /// <param name="services"></param>
        public void ConfigureServices(IServiceCollection services)
        {
            //Configurations
            services.Configure<AppSettings>(Configuration.GetSection("AppSettings"));
            services.Configure<AnomalyDetectionAlgorithm>(Configuration.GetSection("AnomalyDetectionAlgorithm"));
            services.Configure<ChangePointDetectionAlgorithm>(Configuration.GetSection("ChangePointDetectionAlgorithm"));
            services.Configure<SpikeDetectionAlgorithm>(Configuration.GetSection("SpikeDetectionAlgorithm"));
            services.Configure<ForecastingAlgorithm>(Configuration.GetSection("ForecastingAlgorithm"));

            ConfigureDependencyForServices(services);

            //Cors
            services.AddCors(options =>
            {
                options.AddPolicy("AllowAll",
                    builder => builder
                    .AllowAnyMethod()
                    .AllowAnyHeader()
                    .AllowCredentials()
                    .WithExposedHeaders("Content-Disposition")
                    .SetIsOriginAllowed((hosts) => true));
            });

            //SignalR
            services.AddSignalR(config =>
            {
                config.EnableDetailedErrors = true;
            });

            //Controllers
            services.AddControllers().AddNewtonsoftJson(opts =>
            {
                opts.SerializerSettings.Converters.Add(new StringEnumConverter());
            });

            //Swaggerapi doc
            services.AddSwaggerGen(c =>
            {
                c.SwaggerDoc("V1", new OpenApiInfo
                {
                    Version = "V1.0",
                    Title = "Edge ML API v1.0",
                    Description = "API For ML Apps"
                });
            });

            services.AddSwaggerGenNewtonsoftSupport();
        }

        /// <summary>
        /// Configure
        /// </summary>
        /// <param name="app"></param>
        /// <param name="env"></param>
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            app.UseRouting();

            app.UseCors("AllowAll");

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
                endpoints.MapHub<MLAlertNotificationHub>(NOTIFICATION_HUB_PATH);
            });

            //Swagger api doc
            app.UseSwagger();
            app.UseSwaggerUI(options =>
            {
                options.SwaggerEndpoint("/swagger/V1/swagger.json", "V1");
                options.RoutePrefix = "swagger";
            });
        }

        /// <summary>
        /// ConfigureServices(DI)
        /// </summary>
        /// <param name="services"></param>
        private void ConfigureDependencyForServices(IServiceCollection services)
        {
            //MLContext engine
            services.AddSingleton((mLContext) =>
            {
                return new MLContext();
            });

            //Redis Pub-Sub DataBus subscriber
            services.AddSingleton<IRedisDataBusSubscriber>(serviceProvider =>
            {
                string redisServer = serviceProvider.GetService<IOptions<AppSettings>>().Value.DataBusSettings.Server;
                return new RedisDataBusSubscriber(redisServer);
            });

            //Redis Pub-Sub DataBus pulisher
            services.AddSingleton<IRedisDataBusPublisher>(serviceProvider =>
            {
                string redisServer = serviceProvider.GetService<IOptions<AppSettings>>().Value.DataBusSettings.Server;
                return new RedisDataBusPublisher(redisServer);
            });

            //Redis DB 
            services.AddSingleton(serviceProvider =>
            {
                string redisServer = serviceProvider.GetService<IOptions<AppSettings>>().Value.DataBusSettings.Server;
                return new RedisDataBus(redisServer);//pass connection string
            });

            //DataServiceProvider
            services.AddSingleton(serviceProvider =>
            {
                AppSettings settings = serviceProvider.GetService<IOptions<AppSettings>>().Value;

                RedisDataBusSubscriber subscriber = new RedisDataBusSubscriber(settings.DataBusSettings.Server, settings.DataBusSettings.SubscribeTopic);
                RedisDataBus redisDataBus = new RedisDataBus(settings.DataBusSettings.Server);
                return new DataServiceProvider(subscriber, redisDataBus, settings);//pass connection string, topic
            });

            //AlertNotificationClient SignalR Hub
            services.AddSingleton(serviceProvider =>
            {
                AppSettings appSettings = serviceProvider.GetService<IOptions<AppSettings>>().Value;
                return new MLAlertNotificationClient(appSettings);
            });

            //ML framework main service
            services.AddHostedService<MachineLearningFrameworkService>();
        }

    }
}
