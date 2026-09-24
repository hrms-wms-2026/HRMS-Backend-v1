namespace ONEVO.Application.Common.Exceptions;

public class CoverageOrderConflictException : Exception
{
    public CoverageOrderConflictException(string message) : base(message) { }

    public CoverageOrderConflictException(string message, Exception innerException)
        : base(message, innerException) { }
}
