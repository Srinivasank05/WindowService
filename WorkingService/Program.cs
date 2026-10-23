using WorkingService;


var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddWindowsService(options =>
{
    options.ServiceName = "WorkingService";
});
builder.Services.AddHostedService<Worker>();
ServiceHelper.Init(builder.Configuration);
LogWriter.Init(builder.Configuration["Service:Folder"] ?? @"F:\DockerDemo\Errorlog1");


var host = builder.Build();
host.Run();
