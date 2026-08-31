using System;

namespace Naraka.Core.Domain
{
    public readonly struct RequestId : IEquatable<RequestId>
    {
        public RequestId(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException("RequestId cannot be empty.", nameof(value));
            }

            Value = value;
        }

        public string Value { get; }

        public bool Equals(RequestId other) => string.Equals(Value, other.Value, StringComparison.Ordinal);

        public override bool Equals(object obj) => obj is RequestId other && Equals(other);

        public override int GetHashCode() => Value == null ? 0 : StringComparer.Ordinal.GetHashCode(Value);

        public override string ToString() => Value ?? string.Empty;
    }
}
