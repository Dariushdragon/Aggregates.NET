﻿using Aggregates;
using Aggregates.Messages;

namespace Shared
{
    [Versioned("SagaEcho", "Samples")]
    public interface SagaEcho : IEvent
    {
        DateTime Timestamp { get; set; }
        string Message { get; set; }
    }
}
