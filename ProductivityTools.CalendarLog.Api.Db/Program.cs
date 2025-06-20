using Microsoft.Extensions.Configuration;
using System.Reflection;
using DbUp;
using ProductivityTools.MasterConfiguration;

IConfigurationRoot configuration =
                new ConfigurationBuilder()
                .AddMasterConfiguration(configName: "ProductivityTools.CalendarLog.Api.json", force: true)
                .Build();

var masterConnectionString = configuration["ConnectionString"];
var connectionString = args.FirstOrDefault() ?? masterConnectionString;

EnsureDatabase.For.SqlDatabase(connectionString);
var upgrader =
    DeployChanges.To
        .SqlDatabase(connectionString)
        .WithScriptsEmbeddedInAssembly(Assembly.GetExecutingAssembly())
        .LogToConsole()
        .Build();

var result = upgrader.PerformUpgrade();

if (!result.Successful)
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine(result.Error);
    Console.ResetColor();
#if DEBUG
    Console.ReadLine();
#endif
    return -1;
}

Console.ForegroundColor = ConsoleColor.Green;
Console.WriteLine("Success!");
Console.ResetColor();
return 0;
