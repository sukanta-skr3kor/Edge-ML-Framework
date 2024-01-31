// See https://aka.ms/new-console-template for more information
using Sukanta.DataBus.Abstraction;
using Sukanta.DataBus.Redis;

Console.WriteLine("Starting Simulator, please wait...");
var _redisDataBusPublisher = new RedisDataBusPublisher("localhost:6379");
Task.Delay(1000);
Console.WriteLine("Simulator connected.");


while (true)
{
    try
    {
        DataBusMessage message = new DataBusMessage();
        message.Source = "Boiler1";
        message.Id = "Temperature";
        message.Value = new Random().NextInt64(100).ToString();
        message.Time = DateTime.UtcNow;

        _redisDataBusPublisher.PublishDataBusMessageAsync(message, "datamessage");

        Console.WriteLine("Temperature : {0} degree", message.Value);
        Thread.Sleep(5000);
    }
    catch (Exception exp)
    {
        Console.WriteLine(exp.Message);
    }
}


