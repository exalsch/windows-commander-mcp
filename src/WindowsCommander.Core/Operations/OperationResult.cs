namespace WindowsCommander.Core.Operations;

public sealed record OperationResult<T>(
    string OperationId,
    bool Success,
    T? Data,
    OperationError? Error)
{
    public static OperationResult<T> Ok(string operationId, T data)
    {
        return new OperationResult<T>(operationId, true, data, null);
    }

    public static OperationResult<T> Fail(string operationId, string code, string message)
    {
        return new OperationResult<T>(operationId, false, default, new OperationError(code, message));
    }
}

public sealed record OperationError(string Code, string Message);
