namespace ONEVO.Application.Common.Models;

public class Result<T>
{
    public bool IsSuccess { get; }
    public T? Value { get; }
    public string? Error { get; }
    public int? StatusCode { get; }
    public string? ErrorCode { get; }

    private Result(bool isSuccess, T? value, string? error, int? statusCode, string? errorCode)
    {
        IsSuccess = isSuccess;
        Value = value;
        Error = error;
        StatusCode = statusCode;
        ErrorCode = errorCode;
    }

    public static Result<T> Success(T value) => new(true, value, null, null, null);

    public static Result<T> Failure(string error, int statusCode = 400, string? errorCode = null)
        => new(false, default, error, statusCode, errorCode);

    public static Result<T> NotFound(string error)
        => new(false, default, error, 404, null);

    public static Result<T> Forbidden(string error = "Access denied.")
        => new(false, default, error, 403, null);

    public static Result<T> Conflict(string error)
        => new(false, default, error, 409, null);

    public static Result<T> UnprocessableEntity(string error)
        => new(false, default, error, 422, null);
}

public class Result
{
    public bool IsSuccess { get; }
    public string? Error { get; }
    public int? StatusCode { get; }
    public string? ErrorCode { get; }

    private Result(bool isSuccess, string? error, int? statusCode, string? errorCode)
    {
        IsSuccess = isSuccess;
        Error = error;
        StatusCode = statusCode;
        ErrorCode = errorCode;
    }

    public static Result Success() => new(true, null, null, null);

    public static Result Failure(string error, int statusCode = 400, string? errorCode = null)
        => new(false, error, statusCode, errorCode);

    public static Result NotFound(string error)
        => new(false, error, 404, null);

    public static Result Forbidden(string error = "Access denied.")
        => new(false, error, 403, null);

    public static Result Conflict(string error)
        => new(false, error, 409, null);

    public static Result UnprocessableEntity(string error)
        => new(false, error, 422, null);
}
