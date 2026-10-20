namespace AIFramework.SampleApi.Contracts;

public sealed record CommonResult<T>(bool Succeeded, T? Data, string? Error)
{
    public static CommonResult<T> Success(T? data) => new(true, data, null);

    public static CommonResult<T> Failure(string error) => new(false, default, error);
}

