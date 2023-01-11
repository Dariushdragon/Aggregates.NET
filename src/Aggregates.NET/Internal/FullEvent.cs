﻿using Aggregates.Contracts;
using Aggregates.Messages;
using System;

namespace Aggregates.Internal
{
    class FullEvent : IFullEvent
    {
        public IEventDescriptor Descriptor { get; set; }
        public IEvent Event { get; set; }
        public Guid? EventId { get; set; }
    }
}
