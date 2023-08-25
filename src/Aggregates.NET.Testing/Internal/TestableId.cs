﻿using System;
using System.Diagnostics.CodeAnalysis;

namespace Aggregates.Internal
{
    public class TestableId : Id
    {
        public readonly string GeneratedIdKey;

        public readonly long LongId;
        public readonly string StringId;
        public readonly Guid GuidId;

        public TestableId(string generatedIdKey, long longId, string stringId, Guid guidId) : base(generatedIdKey)
        {
            GeneratedIdKey = generatedIdKey;
            LongId = longId;
            StringId = stringId;
            GuidId = guidId;
        }

        protected override long GetLongValue()
        {
            return this.LongId;
        }
        protected override Guid GetGuidValue()
        {
            return this.GuidId;
        }
        protected override string GetStringValue()
        {
            return this.StringId;
        }

        public override string ToString()
        {
            return this.GeneratedIdKey;
        }
        public bool Equals(TestableId other)
        {
            if (ReferenceEquals(null, other) && this.Value == null) return true;
            if (ReferenceEquals(this, other)) return true;
            if (this.Value == null && other.Value == null) return true;
            return other.LongId == LongId && other.GuidId == GuidId && other.StringId == StringId;
        }
        public new bool Equals(Id other)
        {
            if (other.Value is Guid)
                return (Guid)other.Value == GuidId;
            if (other.Value is string)
                return (string)other.Value == StringId;
            if (other.Value is long)
                return (long)other.Value == LongId;
            return false;
        }
    }
}
