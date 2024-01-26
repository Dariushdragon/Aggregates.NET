﻿using Aggregates.Contracts;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Aggregates.Common
{
    public class Entity : TestSubject<FakeEntity>
    {
        [Fact]
        public void EntityHasId()
        {
            Aggregates.Id id = "test";
            Inject(id);

            Sut.Id.Should().Be((Aggregates.Id)"test");
        }
        [Fact]
        public void EntityHasBucket()
        {
            Sut.Bucket.Should().Be(Defaults.Bucket);
        }
        [Fact]
        public void EntityShouldBeNew()
        {
            // Modify factory to create NEW entity
            Fixture.Customize<FakeEntity>(x => x.FromFactory(() =>
            {
                var factory = Internal.EntityFactory.For<FakeEntity>();

                var entity = factory.Create(Fake<ILogger>(), Defaults.Bucket, Fake<Id>());

                return entity;
            }));

            Sut.Version.Should().Be(Internal.EntityFactory.NewEntityVersion);
        }
        [Fact]
        public void EntityShouldBeClean()
        {
            Sut.Dirty.Should().Be(false);
        }
        [Fact]
        public void NewEntityShouldBeDirty()
        {
            // Modify factory to create NEW entity
            Fixture.Customize<FakeEntity>(x => x.FromFactory(() =>
            {
                var factory = Internal.EntityFactory.For<FakeEntity>();

                var entity = factory.Create(Fake<ILogger>(), Defaults.Bucket, Fake<Id>());

                return entity;
            }));

            Sut.Dirty.Should().Be(true);
        }
        [Fact]
        public void EntityShouldBeDirty()
        {
            (Sut as INeedVersionRegistrar).Registrar = Fake<IVersionRegistrar>();
            Sut.ApplyEvents(Many<FakeDomainEvent.FakeEvent>());
            Sut.Dirty.Should().Be(true);
        }
        // Getting events from an entity was removed but could come back
        //[Fact]
        //public async Task ShouldGetEventStreamSize()
        //{
        //    var store = Fake<IStoreEvents>();
        //    A.CallTo(store).WithReturnType<Task<long>>().Returns(1L);
        //    Inject(store);

        //    var size = await Sut.GetSize();
        //    size.Should().Be(1);
        //}
        //[Fact]
        //public async Task ShouldGetOobSize()
        //{
        //    var store = Fake<IOobWriter>();
        //    A.CallTo(store).WithReturnType<Task<long>>().Returns(1L);
        //    Inject(store);

        //    var size = await Sut.GetSize("test");
        //    size.Should().Be(1);
        //}
        //[Fact]
        //public async Task ShouldGetEventsFromEventStream()
        //{
        //    var store = Fake<IStoreEvents>();
        //    A.CallTo(store).WithReturnType<IFullEvent[]>().Returns(Many<IFullEvent>());
        //    Inject(store);

        //    var events = await Sut.GetEvents(0, 100);
        //    events.Should().HaveCount(3);
        //}
        //[Fact]
        //public async Task ShouldGetEventsBackwardsFromEventStream()
        //{
        //    var store = Fake<IStoreEvents>();
        //    A.CallTo(store).WithReturnType<IFullEvent[]>().Returns(Many<IFullEvent>());
        //    Inject(store);

        //    var events = await Sut.GetEventsBackwards(0, 100);
        //    events.Should().HaveCount(3);
        //}
        //[Fact]
        //public async Task ShouldGetEventsFromOob()
        //{
        //    var store = Fake<IOobWriter>();
        //    A.CallTo(store).WithReturnType<IFullEvent[]>().Returns(Many<IFullEvent>());
        //    Inject(store);

        //    var events = await Sut.GetEvents(0, 100, oob: "test");
        //    events.Should().HaveCount(3);
        //}
        //[Fact]
        //public async Task ShouldGetEventsBackwardsFromOob()
        //{
        //    var store = Fake<IOobWriter>();
        //    A.CallTo(store).WithReturnType<IFullEvent[]>().Returns(Many<IFullEvent>());
        //    Inject(store);

        //    var events = await Sut.GetEventsBackwards(0, 100, oob: "test");
        //    events.Should().HaveCount(3);
        //}
        [Fact]
        public void ShouldPassDefinedRule()
        {
            Sut.State.RuleCheck = false;

            Sut.Rule("Rule Check", x => x.RuleCheck);
        }
        [Fact]
        public void ShouldFailDefinedRule()
        {
            Sut.State.RuleCheck = true;

            var e = Record.Exception(() => Sut.Rule("Rule Check", x => x.RuleCheck));
            e.Should().BeOfType<BusinessException>();
        }
    }
}
