using Spectre.Console.Cli;
using Microsoft.Extensions.DependencyInjection;

using ResXporter.Commands;
using ResXporter.Providers;

using Spectre.Console.Cli.Extensions.DependencyInjection;

var services = new ServiceCollection();

services.AddHttpClient();
services.AddSingleton(TimeProvider.System);
services.AddKeyedScoped<IExporter, JetBrainsCsvProvider>(Provider.JetBrainsCsv);
services.AddKeyedScoped<IExporter, MicrosoftListsProvider>(Provider.MicrosoftLists);

services.AddKeyedScoped<ILoader, JetBrainsCsvProvider>(Provider.JetBrainsCsv);
services.AddKeyedScoped<ILoader, MicrosoftListsProvider>(Provider.MicrosoftLists);

var registrar = new DependencyInjectionRegistrar(services);

var app = new CommandApp(registrar);

app.Configure(config =>
{
    config.AddCommand<ExportCommand>("export");

    config.AddCommand<ImportCommand>("import");
});

return await app.RunAsync(args);