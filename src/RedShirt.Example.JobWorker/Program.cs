using Microsoft.Extensions.Hosting;
using RedShirt.Example.JobWorker;

await Setup.GetHost(args).RunAsync();