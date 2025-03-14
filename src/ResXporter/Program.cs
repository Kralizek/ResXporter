using Spectre.Console.Cli;
using Microsoft.Extensions.DependencyInjection;

using Spectre.Console.Cli.Extensions.DependencyInjection;

var services = new ServiceCollection();

var registrar = new DependencyInjectionRegistrar(services);

var app = new CommandApp(registrar);