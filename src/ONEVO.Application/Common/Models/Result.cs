namespace ONEVO.Application.Common.Models;

public class Result<T>
{
    public bool IsSuccess { get; }
    public T? Value { get; }
    public string? Error { get; }
    public int? StatusCode { get; }

    private Result(bool isSuccess, T? value, string? error, int? statusCode)
    {
        IsSuccess = isSuccess;
        Value = value;
        Error = error;
        StatusCode = statusCode;
    }

    public static Result<T> Success(T value) => new(true, value, null, null);

    public static Result<T> Failure(string error, int statusCode = 400)
        => new(false, default, error, statusCode);

    public static Result<T> NotFound(string error)
        => new(false, default, error, 404);

    public static Result<T> Forbidden(string error = "Access denied.")
        => new(false, default, error, 403);

    public static Result<T> Conflict(string error)
        => new(false, default, error, 409);

    public static Result<T> UnprocessableEntity(string error)
        => new(false, default, error, 422);
}

public class Result
{
    public bool IsSuccess { get; }
    public string? Error { get; }
    public int? StatusCode { get; }

    private Result(bool isSuccess, string? error, int? statusCode)
    {
        IsSuccess = isSuccess;
        Error = error;
        StatusCode = statusCode;
    }

    public static Result Success() => new(true, null, null);

    public static Result Failure(string error, int statusCode = 400)
        => new(false, error, statusCode);

    public static Result NotFound(string error)
        => new(false, error, 404);

    public static Result Forbidden(string error = "Access denied.")
        => new(false, error, 403);

    public static Result Conflict(string error)
        => new(false, error, 409);

    public static Result UnprocessableEntity(string error)
        => new(false, error, 422);
}
