namespace Activities;

public class Transaction(string method, string protocol) : SubProcess
{
    public string Method { get; set; } = method;
    public string Protocol { get; set; } = protocol;
}