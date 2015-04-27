﻿using Aggregates.Contracts;
using Aggregates.Internal;

using NServiceBus;
using NServiceBus.ObjectBuilder;
using NServiceBus.ObjectBuilder.Common;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates.Unit.Aggregate
{
    [TestFixture]
    public class RouteAggregateTests
    {
        private Moq.Mock<IBuilder> _builder;
        private Moq.Mock<IStoreEvents> _store;
        private Moq.Mock<IEventStream> _stream;
        private Moq.Mock<IMessageCreator> _eventFactory;
        private Moq.Mock<IRouteResolver> _resolver;
        private IUnitOfWork _uow;
        private Guid _id;

        [SetUp]
        public void Setup()
        {
            _id = Guid.NewGuid();
            _builder = new Moq.Mock<IBuilder>();
            _store = new Moq.Mock<IStoreEvents>();
            _stream = new Moq.Mock<IEventStream>();
            _eventFactory = new Moq.Mock<IMessageCreator>();
            _resolver = new Moq.Mock<IRouteResolver>();

            _builder.Setup(x => x.CreateChildBuilder()).Returns(_builder.Object);
            _builder.Setup(x => x.Build<IRouteResolver>()).Returns(_resolver.Object);
            _builder.Setup(x => x.Build<IMessageCreator>()).Returns(_eventFactory.Object);
            _builder.Setup(x => x.Build<IStoreEvents>()).Returns(_store.Object);

            _stream.Setup(x => x.StreamId).Returns(String.Format("{0}", _id));
            _stream.Setup(x => x.StreamVersion).Returns(0);
            _stream.Setup(x => x.Events).Returns(new List<IWritableEvent>());

            _uow = new Aggregates.Internal.UnitOfWork(_builder.Object, new DefaultRepositoryFactory());
        }

        [Test]
        public void has_route()
        {
            var root = _uow.For<_AggregateStub>().New(_id);

            _resolver.Setup(x => x.Resolve(root, typeof(String))).Returns(e => { }).Verifiable();
            root.TestRouteFor(typeof(String), Moq.It.IsAny<Object>());
            _resolver.Verify(x => x.Resolve(root, typeof(String)), Moq.Times.AtLeastOnce);
        }
        [Test]
        public void no_route()
        {
            var root = _uow.For<_AggregateStub>().New(_id);

            _resolver.Setup(x => x.Resolve(root, typeof(String))).Returns(e => { });
            Assert.DoesNotThrow(() => root.TestRouteFor(typeof(Int32), Moq.It.IsAny<Object>()));
        }
    }
}