namespace BPMN.Flowzer;

public class FlowzerList<T> : List<T>
{
    public FlowzerList() { }

    public FlowzerList(IEnumerable<T> collection) : base(collection) { }

    public override int GetHashCode()
    {
        unchecked
        {
            return this.Aggregate(17, (hash, item) => hash * 23 + (item?.GetHashCode() ?? 0));
        }
    }

    public override bool Equals(object? obj)
    {
        if (obj is FlowzerList<T> other)
        {
            return this.SequenceEqual(other);
        }
        return false;
    }

    // Eine Factory-Methode, um eine FlowzerList von einer List zu erstellen
    public static FlowzerList<T> FromList(List<T> list)
    {
        return new FlowzerList<T>(list);
    }
}

public static class EnumerableExtensions
{
    public static FlowzerList<T> ToFlowzerList<T>(this IEnumerable<T> source)
    {
        return new FlowzerList<T>(source);
    }
}