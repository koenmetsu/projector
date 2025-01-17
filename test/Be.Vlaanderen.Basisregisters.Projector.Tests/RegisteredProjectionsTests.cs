namespace Be.Vlaanderen.Basisregisters.Projector.Tests
{
    using System.Collections.Generic;
    using System.Linq;
    using AutoFixture;
    using ConnectedProjections;
    using FluentAssertions;
    using Infrastructure;
    using Internal;
    using Internal.Extensions;
    using Xunit;

    public class RegisteredProjectionsTests
    {
        private readonly IEnumerable<IConnectedProjection> _registeredProjections;
        private readonly RegisteredProjections _sut;
        private readonly IFixture _fixture;

        public RegisteredProjectionsTests()
        {
            _fixture = new Fixture()
                .CustomizeConnectedProjectionNames()
                .CustomizeRegisteredProjectionsCollection();

            _registeredProjections = _fixture.Create<IEnumerable<IConnectedProjection>>();
            _sut = new RegisteredProjections(_registeredProjections);
        }

        [Fact]
        public void When_requesting_the_names_then_the_names_of_all_registered_projections_are_returned()
        {
            var expectedNames = _registeredProjections.Select(projection => projection.Name);

            _sut.Names
                .Should()
                .BeEquivalentTo(expectedNames);
        }

        [Fact]
        public void When_requesting_the_name_of_an_registered_projection_then_the_typed_name_is_returned()
        {
            var projectionName = _registeredProjections.ToArray()[1].Name;

            _sut.GetName(projectionName.ToString())
                .Should()
                .Be(projectionName);
        }

        [Fact]
        public void When_requesting_the_name_of_a_unregistered_projection_then_the_typed_name_is_returned()
        {
            _sut.GetName(_fixture.Create<string>())
                .Should()
                .Be(null);
        }

        [Fact]
        public void When_checking_if_a_projection_that_is_catching_up_is_projecting_then_true_is_returned()
        {
            var projection = _fixture.Create<ConnectedProjectionName>();
            _sut.IsCatchingUp = name => name.Equals(projection);

            _sut.IsProjecting(projection)
                .Should()
                .BeTrue();
        }

        [Fact]
        public void When_checking_if_a_projection_that_is_subscribed_is_projecting_then_true_is_returned()
        {
            var projection = _fixture.Create<ConnectedProjectionName>();
            _sut.IsSubscribed = name => name.Equals(projection);

            _sut.IsProjecting(projection)
                .Should()
                .BeTrue();
        }

        [Fact]
        public void When_checking_if_a_projection_that_is_not_catching_up_or_subscribed_is_projecting_then_true_is_returned()
        {
            var projection = _fixture.Create<ConnectedProjectionName>();
            _sut.IsCatchingUp = name => !name.Equals(projection);
            _sut.IsSubscribed = name => !name.Equals(projection);

            _sut.IsProjecting(projection)
                .Should()
                .BeFalse();
        }

        [Fact]
        public void When_requesting_the_projection_states_then_states_are_returned_for_each_registered_projection()
        {
            var expectedStates = _registeredProjections
                .Shuffle()
                .Select(connectedProjection =>
                    new RegisteredConnectedProjection(
                        connectedProjection.Name,
                        _fixture.Create<ConnectedProjectionState>()))
                .ToReadOnlyList();

            _sut.IsCatchingUp = name => expectedStates
                .Where(projection => projection.State == ConnectedProjectionState.CatchingUp)
                .Select(projection => projection.Name)
                .Contains(name);
            _sut.IsSubscribed = name => expectedStates
                .Where(projection => projection.State == ConnectedProjectionState.Subscribed)
                .Select(projection => projection.Name)
                .Contains(name);

            _sut.GetStates()
                .Should()
                .HaveSameCount(_registeredProjections)
                .And.BeEquivalentTo(expectedStates);
        }


    }
}
