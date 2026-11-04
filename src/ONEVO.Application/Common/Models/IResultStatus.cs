namespace ONEVO.Application.Common.Models;

/// <summary>Lets pipeline behaviors see whether a <see cref="Result"/> or <see cref="Result{T}"/> succeeded.</summary>
public interface IResultStatus
{
    bool IsSuccess { get; }
}
