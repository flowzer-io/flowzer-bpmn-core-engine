namespace WebApiEngine.Shared;

public class ApiStatusResult
{
    public ApiStatusResult()
    {
    }

    public ApiStatusResult(string? errorMessage)
    {
        ErrorMessage = errorMessage;
        Successful = false;
    }

    public bool Successful { get; set; }
    public string? ErrorMessage { get; set; }
}
public class ApiStatusResult<T>: ApiStatusResult
{
    public ApiStatusResult()
    {
    }

    public ApiStatusResult(string? errorMessage): base(errorMessage)
    {
    }
  
    public ApiStatusResult(T result)
    {
        Result = result;
        Successful = true;
    }
    public T? Result { get; set; }
}