namespace ConfluenceChatBot.Models
{
    public record Result<T>
    {
        public bool IsSuccess { get; }
        public T? Value { get; }
        public string Error { get; }

        private Result(T? value, bool isSuccess, string error)
        {
            Value = value;
            IsSuccess = isSuccess;
            Error = error;
        }

        public static Result<T> Ok(T value) => new(value, true, string.Empty);
        public static Result<T> Fail(string error) => new(default, false, error);
    }

    public static class ResultExtensions
    {
        
        // ------------------------------
        // For async Task<Result<T>>
        // ------------------------------
        public static async Task<Result<U>> BindAsync<T, U>(this Task<Result<T>> resultTask,Func<T, Task<Result<U>>> func)
        {
            var result = await resultTask;
            return result.IsSuccess ? await func(result.Value!) : Result<U>.Fail(result.Error);
        }

        public static async Task<Result<U>> MapAsync<T, U>(this Task<Result<T>> resultTask,Func<T, Task<U>> func)
        {
            var result = await resultTask;
            if (!result.IsSuccess) return Result<U>.Fail(result.Error);

            var value = await func(result.Value!);
            return Result<U>.Ok(value);
        }

        // ------------------------------
        // Synchronous Bind / Map for Result<T>
        // ------------------------------
        public static Result<U> Bind<T, U>(this Result<T> result,Func<T, Result<U>> func)
        {
            return result.IsSuccess ? func(result.Value!) : Result<U>.Fail(result.Error);
        }

        public static Result<U> Map<T, U>(this Result<T> result,Func<T, U> func)
        {
            return result.IsSuccess ? Result<U>.Ok(func(result.Value!)) : Result<U>.Fail(result.Error);
        }
    }
}
