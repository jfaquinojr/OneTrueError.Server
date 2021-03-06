﻿using System;
using System.Threading.Tasks;
using DotNetCqs;
using FluentAssertions;
using NSubstitute;
using OneTrueError.Api.Core.Applications.Events;
using OneTrueError.Api.Core.Invitations.Commands;
using OneTrueError.Api.Core.Messaging.Commands;
using OneTrueError.App.Core.Applications;
using OneTrueError.App.Core.Invitations;
using OneTrueError.App.Core.Invitations.CommandHandlers;
using OneTrueError.App.Core.Users;
using OneTrueError.Infrastructure.Configuration;
using Xunit;

namespace OneTrueError.App.Tests.Core.Applications.Commands
{
    public class InviteUserHandlerTests
    {
        private readonly IApplicationRepository _applicationRepository;
        private readonly ICommandBus _commandBus;
        private readonly IEventBus _eventBus;
        private readonly IInvitationRepository _invitationRepository;
        private readonly InviteUserHandler _sut;
        private readonly IUserRepository _userRepository;

        public InviteUserHandlerTests()
        {
            _invitationRepository = Substitute.For<IInvitationRepository>();
            _userRepository = Substitute.For<IUserRepository>();
            _applicationRepository = Substitute.For<IApplicationRepository>();
            _commandBus = Substitute.For<ICommandBus>();
            _eventBus = Substitute.For<IEventBus>();
            _userRepository.GetUserAsync(1).Returns(new User(1, "First"));
            _applicationRepository.GetByIdAsync(1).Returns(new Application(1, "MyApp"));
            ConfigurationStore.Instance = new TestStore();
            _sut = new InviteUserHandler(_invitationRepository, _eventBus, _userRepository, _applicationRepository,
                _commandBus);
        }

        [Fact]
        public async Task should_create_an_invite_for_a_new_user()
        {
            var cmd = new InviteUser(1, "jonas@gauffin.com") {UserId = 1};
            var members = new[] {new ApplicationTeamMember(1, 3)};
            ApplicationTeamMember actual = null;
            _applicationRepository.GetTeamMembersAsync(1).Returns(members);
            _applicationRepository.WhenForAnyArgs(x => x.CreateAsync(Arg.Any<ApplicationTeamMember>()))
                .Do(x => actual = x.Arg<ApplicationTeamMember>());

            await _sut.ExecuteAsync(cmd);

            _applicationRepository.Received().CreateAsync(Arg.Any<ApplicationTeamMember>());
            actual.EmailAddress.Should().Be(cmd.EmailAddress);
            actual.ApplicationId.Should().Be(cmd.ApplicationId);
            actual.AddedAtUtc.Should().BeCloseTo(DateTime.UtcNow, 1000);
            actual.AddedByName.Should().Be("First");
        }

        [Fact]
        public async Task should_not_allow_invites_when_the_invited_user_already_have_an_account()
        {
            var cmd = new InviteUser(1, "jonas@gauffin.com") {UserId = 2};
            var members = new[] {new ApplicationTeamMember(1, 3)};
            _userRepository.FindByEmailAsync(cmd.EmailAddress).Returns(new User(3, "existing"));
            _applicationRepository.GetTeamMembersAsync(1).Returns(members);

            await _sut.ExecuteAsync(cmd);

            await _applicationRepository.DidNotReceive().CreateAsync(Arg.Any<ApplicationTeamMember>());
        }

        [Fact]
        public async Task should_not_allow_invites_when_the_invited_user_already_have_an_pending_invite()
        {
            var cmd = new InviteUser(1, "jonas@gauffin.com") {UserId = 1};
            var members = new[] {new ApplicationTeamMember(1, cmd.EmailAddress)};
            _applicationRepository.GetTeamMembersAsync(1).Returns(members);

            await _sut.ExecuteAsync(cmd);

            await _applicationRepository.DidNotReceive().CreateAsync(Arg.Any<ApplicationTeamMember>());
        }

        [Fact]
        public async Task should_notify_the_system_of_the_invite()
        {
            var cmd = new InviteUser(1, "jonas@gauffin.com") {UserId = 1};
            var members = new[] {new ApplicationTeamMember(1, 3)};
            ApplicationTeamMember actual = null;
            _applicationRepository.GetTeamMembersAsync(1).Returns(members);
            _applicationRepository.WhenForAnyArgs(x => x.CreateAsync(Arg.Any<ApplicationTeamMember>()))
                .Do(x => actual = x.Arg<ApplicationTeamMember>());

            await _sut.ExecuteAsync(cmd);

            _applicationRepository.Received().CreateAsync(Arg.Any<ApplicationTeamMember>());
            _eventBus.Received().PublishAsync(Arg.Any<UserInvitedToApplication>());
        }

        [Fact]
        public async Task should_send_an_invitation_email()
        {
            var cmd = new InviteUser(1, "jonas@gauffin.com") {UserId = 1};
            var members = new[] {new ApplicationTeamMember(1, 3)};
            ApplicationTeamMember actual = null;
            _applicationRepository.GetTeamMembersAsync(1).Returns(members);
            _applicationRepository.WhenForAnyArgs(x => x.CreateAsync(Arg.Any<ApplicationTeamMember>()))
                .Do(x => actual = x.Arg<ApplicationTeamMember>());

            await _sut.ExecuteAsync(cmd);

            _commandBus.Received().ExecuteAsync(Arg.Any<SendEmail>());
        }
    }
}