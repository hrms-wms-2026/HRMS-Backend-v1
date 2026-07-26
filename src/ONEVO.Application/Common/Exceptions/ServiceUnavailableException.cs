namespace ONEVO.Application.Common.Exceptions;

public sealed class ServiceUnavailableException : Exception
{
    public ServiceUnavailableException(string errorCode)
        : base(errorCode)
    {
        ErrorCode = errorCode;
    }

    public string ErrorCode { get; }
}
