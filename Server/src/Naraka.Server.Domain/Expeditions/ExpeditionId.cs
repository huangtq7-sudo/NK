namespace Naraka.Server.Domain.Expeditions;

/// <summary>远征的稳定业务标识，不得使用场景名或连接标识代替。</summary>
public readonly record struct ExpeditionId
{
    public ExpeditionId(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 64)
        {
            throw new ArgumentException("ExpeditionId must contain 1-64 characters.", nameof(value));
        }

        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;
}
