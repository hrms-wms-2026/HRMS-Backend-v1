namespace ONEVO.Application.Common.ServiceInterfaces;

public interface IBackgroundJobService
{
    string Enqueue<T>(System.Linq.Expressions.Expression<Action<T>> methodCall);
    string Schedule<T>(System.Linq.Expressions.Expression<Action<T>> methodCall, TimeSpan delay);
    void AddOrUpdateRecurring<T>(string jobId, System.Linq.Expressions.Expression<Action<T>> methodCall, string cronExpression);
}
