using Hangfire.Common;
using Hangfire.Storage;
using Hangfire.Storage.Monitoring;

namespace Mealplan.Tests.Fakes;

/// <summary>
/// Monitoring over in-memory job lists. Only what PendingCrawl reads is
/// implemented; the rest of the interface throws.
/// </summary>
public sealed class FakeMonitoringApi : IMonitoringApi
{
    public Dictionary<string, List<Job?>> Enqueued { get; } = [];

    public Dictionary<string, List<Job?>> Fetched { get; } = [];

    public List<Job?> Processing { get; } = [];

    public List<Job?> Scheduled { get; } = [];

    public IList<QueueWithTopEnqueuedJobsDto> Queues() =>
        Enqueued.Keys.Union(Fetched.Keys)
            .Select(name => new QueueWithTopEnqueuedJobsDto { Name = name })
            .ToList();

    public JobList<EnqueuedJobDto> EnqueuedJobs(string queue, int from, int perPage) =>
        Page(Enqueued.GetValueOrDefault(queue), from, perPage, job => new EnqueuedJobDto { Job = job });

    public JobList<FetchedJobDto> FetchedJobs(string queue, int from, int perPage) =>
        Page(Fetched.GetValueOrDefault(queue), from, perPage, job => new FetchedJobDto { Job = job });

    public JobList<ProcessingJobDto> ProcessingJobs(int from, int count) =>
        Page(Processing, from, count, job => new ProcessingJobDto { Job = job });

    public JobList<ScheduledJobDto> ScheduledJobs(int from, int count) =>
        Page(Scheduled, from, count, job => new ScheduledJobDto { Job = job });

    private static JobList<TDto> Page<TDto>(
        List<Job?>? jobs,
        int from,
        int count,
        Func<Job?, TDto> dto) =>
        new((jobs ?? []).Skip(from).Take(count)
            .Select((job, i) => new KeyValuePair<string, TDto>($"{from + i}", dto(job))));

    public IList<ServerDto> Servers() => throw new NotSupportedException();

    public JobDetailsDto JobDetails(string jobId) => throw new NotSupportedException();

    public StatisticsDto GetStatistics() => throw new NotSupportedException();

    public JobList<SucceededJobDto> SucceededJobs(int from, int count) => throw new NotSupportedException();

    public JobList<FailedJobDto> FailedJobs(int from, int count) => throw new NotSupportedException();

    public JobList<DeletedJobDto> DeletedJobs(int from, int count) => throw new NotSupportedException();

    public long ScheduledCount() => throw new NotSupportedException();

    public long EnqueuedCount(string queue) => throw new NotSupportedException();

    public long FetchedCount(string queue) => throw new NotSupportedException();

    public long FailedCount() => throw new NotSupportedException();

    public long ProcessingCount() => throw new NotSupportedException();

    public long SucceededListCount() => throw new NotSupportedException();

    public long DeletedListCount() => throw new NotSupportedException();

    public IDictionary<DateTime, long> SucceededByDatesCount() => throw new NotSupportedException();

    public IDictionary<DateTime, long> FailedByDatesCount() => throw new NotSupportedException();

    public IDictionary<DateTime, long> HourlySucceededJobs() => throw new NotSupportedException();

    public IDictionary<DateTime, long> HourlyFailedJobs() => throw new NotSupportedException();
}
