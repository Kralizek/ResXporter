using Spectre.Console.Cli;
using Microsoft.Extensions.DependencyInjection;

using ResXporter;
using ResXporter.Commands;
using ResXporter.Exporters;

using Spectre.Console.Cli.Extensions.DependencyInjection;

var services = new ServiceCollection();

services.AddSingleton(TimeProvider.System);
services.AddKeyedScoped<IExporter, JetBrainsCsvExporter>(Exporter.JetBrainsCsv);

var registrar = new DependencyInjectionRegistrar(services);

var app = new CommandApp(registrar);

app.Configure(config =>
{
    config.AddCommand<ExportCommand>("export");
});

return await app.RunAsync(args);