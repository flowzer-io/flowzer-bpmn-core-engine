namespace WebApiEngine.Shared;

public class ApiStatusResult<T>
{
    public ApiStatusResult()
    {
    }

    public ApiStatusResult(T result)
    {
        Result = result;
        Successful = true;
    }

    public ApiStatusResult(string? errorMessage)
    {
        ErrorMessage = errorMessage;
        Successful = false;
    }

    public bool Successful { get; set; }
    public string? ErrorMessage { get; set; }
    public T? Result { get; set; }
}