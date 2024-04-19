using Common;

namespace Events;

public class ErrorEventDefinition : EventDefinition
{
    public Error? Error { get; set; }
}