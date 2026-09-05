using MuckDPI.Service;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

Directory.SetCurrentDirectory(AppContext.BaseDirectory);

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddWindowsService(options => options.ServiceName = "MuckDPI");
builder.Services.AddHostedService<DpiWorker>();
builder.Build().Run();

