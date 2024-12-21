using RedShirt.Example.JobWorker.Core.Models;

namespace RedShirt.Example.JobWorker.Core.Services;

public interface ISourceMessageConverter
{
    IJobDataModel? Convert(string input);
}