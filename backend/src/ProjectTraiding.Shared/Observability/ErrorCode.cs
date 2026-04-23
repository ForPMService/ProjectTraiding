namespace ProjectTraiding.Shared.Observability;

public readonly record struct ErrorCode(string Value)
{
    public override string ToString()
    {
        return Value;
    }
}
