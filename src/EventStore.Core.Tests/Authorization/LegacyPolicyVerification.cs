﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using EventStore.Core.Authorization;
using EventStore.Core.Bus;
using EventStore.Core.Data;
using EventStore.Core.Messages;
using EventStore.Core.Messaging;
using EventStore.Core.Services;
using EventStore.Core.Services.UserManagement;
using EventStore.Plugins.Authorization;
using NUnit.Framework;

namespace EventStore.Core.Tests.Authorization {
	[TestFixture(true, true)]
	[TestFixture(true, false)]
	[TestFixture(false, true)]
	[TestFixture(false, false)]
	public class LegacyPolicyVerification {
		private const string _streamWithDefaultPermissions = "StreamWithDefaultPermissions";
		private const string _streamWithCustomPermissions = "StreamWithCustomPermissions";

		private readonly IAuthorizationProvider _authorizationProvider;
		private readonly AclResponder _aclResponder;
		private static bool _allowAnonymousEndpointAccess;
		private static bool _allowAnonymousStreamAccess;

		public LegacyPolicyVerification(bool allowAnonymousEndpointAccess, bool allowAnonymousStreamAccess) {
			_aclResponder = new AclResponder();
			_allowAnonymousEndpointAccess = allowAnonymousEndpointAccess;
			_allowAnonymousStreamAccess = allowAnonymousStreamAccess;
			_authorizationProvider = new LegacyAuthorizationProviderFactory(_aclResponder, allowAnonymousEndpointAccess, allowAnonymousStreamAccess).Build();
		}

		public class PolicyVerificationParameters {
			public ClaimsPrincipal User { get; }
			public Operation Operation { get; }
			public string Stream { get; }
			public StorageMessage.EffectiveAcl StreamAcl { get; }
			public bool IsAuthorized { get; }
			public Func<bool, bool, bool> AuthorizationCheck { get; }
			public bool ShouldRequestAcl { get; }
			public Func<bool, bool> AclCheck { get; }
			public string TestName { get; }

			public PolicyVerificationParameters(ClaimsPrincipal user, Operation operation, string stream, StorageMessage.EffectiveAcl streamAcl, bool isAuthorized, bool shouldRequestAcl) {
				User = user;
				Operation = operation;
				Stream = stream;
				StreamAcl = streamAcl;
				IsAuthorized = isAuthorized;
				ShouldRequestAcl = shouldRequestAcl;
				TestName = $"{User.Identity?.Name ?? "Anonymous (empty)"} {(isAuthorized ? "is" : "is not")} authorized to perform operation {Operation}";
			}
			public PolicyVerificationParameters(ClaimsPrincipal user, Operation operation, string stream, StorageMessage.EffectiveAcl streamAcl, Func<bool, bool, bool> authorizationCheck, Func<bool, bool> aclCheck) {
				User = user;
				Operation = operation;
				Stream = stream;
				StreamAcl = streamAcl;
				AuthorizationCheck = authorizationCheck;
				AclCheck = aclCheck;
				TestName = $"Verify if Anonymous user is authorized to perform operation {Operation}";
			}

			public override string ToString() => TestName;
		}
		

		public static IEnumerable<PolicyVerificationParameters> PolicyTests() {
			StorageMessage.EffectiveAcl systemStreamPermission = new StorageMessage.EffectiveAcl(
				SystemSettings.Default.SystemStreamAcl,
				SystemSettings.Default.SystemStreamAcl,
				SystemSettings.Default.SystemStreamAcl
			);

			StorageMessage.EffectiveAcl defaultUseruserStreamPermission = new StorageMessage.EffectiveAcl(
				SystemSettings.Default.UserStreamAcl,
				SystemSettings.Default.UserStreamAcl,
				SystemSettings.Default.UserStreamAcl
			);

			StorageMessage.EffectiveAcl userStreamPermission = new StorageMessage.EffectiveAcl(
				new StreamAcl("test", "test", "test", "test", "test"),
				SystemSettings.Default.UserStreamAcl,
				SystemSettings.Default.UserStreamAcl
			);

			ClaimsPrincipal admin = CreatePrincipal("admin", SystemRoles.Admins);
			ClaimsPrincipal userAdmin = CreatePrincipal("adminuser", SystemRoles.Admins);
			ClaimsPrincipal ops = CreatePrincipal("ops", SystemRoles.Operations);
			ClaimsPrincipal userOps = CreatePrincipal("opsuser", SystemRoles.Operations);
			ClaimsPrincipal user1 = CreatePrincipal("test");
			ClaimsPrincipal user2 = CreatePrincipal("test2");
			ClaimsPrincipal userSystem = SystemAccounts.System;

			var admins = new[] {admin, userAdmin};
			var operations = new[] {ops, userOps};
			var users = new[] {user1, user2};
			var system = new[] {userSystem};
			var anonymous = new[]{new ClaimsPrincipal(), new ClaimsPrincipal(new ClaimsIdentity(new Claim[]{new Claim(ClaimTypes.Anonymous, ""), })), };
			foreach (var user in system) {
				foreach (var operation in SystemOperations()) {
					yield return new PolicyVerificationParameters(user,
						operation.Item1, operation.Item2, operation.Item3,
						true,
						false
					);
				}
			}
			foreach (var user in admins) {
				foreach (var operation in SystemOperations()) {
					yield return new PolicyVerificationParameters(user,
						operation.Item1, operation.Item2, operation.Item3,
						false,
						false
					);
				}
				foreach (var operation in AdminOperations()) {
					yield return new PolicyVerificationParameters(user,
						operation.Item1, operation.Item2, operation.Item3,
						true,
						false
					);
				}
				foreach (var operation in OpsOperations()) {
					yield return new PolicyVerificationParameters(user,
						operation.Item1, operation.Item2, operation.Item3,
						true,
						false
					);
				}

				foreach (var operation in UserOperations()) {
					yield return new PolicyVerificationParameters(user,
						operation.Item1, operation.Item2, operation.Item3,
						true,
						false
					);
				}
				
				foreach (var operation in AuthenticatedOperations()) {
					yield return new PolicyVerificationParameters(user,
						operation.Item1, operation.Item2, operation.Item3,
						true,
						false
					);
				}
				
				foreach (var operation in AnonymousOperations()) {
					yield return new PolicyVerificationParameters(user,
						operation.Item1, operation.Item2, operation.Item3,
						true,
						false
					);
				}
			}

			foreach (var user in operations) {
				foreach (var operation in SystemOperations()) {
					yield return new PolicyVerificationParameters(user,
						operation.Item1, operation.Item2, operation.Item3,
						false,
						false
					);
				}
				foreach (var operation in AdminOperations()) {
					yield return new PolicyVerificationParameters(user,
						operation.Item1, operation.Item2, operation.Item3,
						false,
						operation.Item3 != null
					);
				}
				foreach (var operation in OpsOperations()) {
					yield return new PolicyVerificationParameters(user,
						operation.Item1, operation.Item2, operation.Item3,
						true,
						false
					);
				}

				foreach (var operation in UserOperations()) {
					yield return new PolicyVerificationParameters(user,
						operation.Item1, operation.Item2, operation.Item3,
						operation.Item2 == null || operation.Item3 == defaultUseruserStreamPermission,
						operation.Item2 != null
					);
				}

				foreach (var operation in AuthenticatedOperations()) {
					yield return new PolicyVerificationParameters(user,
						operation.Item1, operation.Item2, operation.Item3,
						true,
						operation.Item2 != null
					);
				}

				foreach (var operation in AnonymousOperations()) {
					yield return new PolicyVerificationParameters(user,
						operation.Item1, operation.Item2, operation.Item3,
						true,
						operation.Item2 != null
					);
				}
			}

			foreach (var user in users) {
				foreach (var operation in SystemOperations()) {
					yield return new PolicyVerificationParameters(user,
						operation.Item1, operation.Item2, operation.Item3,
						false,
						false
					);
				}
				foreach (var operation in AdminOperations()) {
					yield return new PolicyVerificationParameters(user,
						operation.Item1, operation.Item2, operation.Item3,
						false,
						operation.Item3 != null
					);
				}
				foreach (var operation in OpsOperations()) {
					yield return new PolicyVerificationParameters(user,
						operation.Item1, operation.Item2, operation.Item3,
						false,
						false
					);
				}

				foreach (var operation in UserOperations()) {
					yield return new PolicyVerificationParameters(user,
						operation.Item1, operation.Item2, operation.Item3,
						operation.Item2 == null || user.Identity.Name != "test2" || operation.Item3 == defaultUseruserStreamPermission,
						operation.Item3 != null
					);
				}

				foreach (var operation in AuthenticatedOperations()) {
					yield return new PolicyVerificationParameters(user,
						operation.Item1, operation.Item2, operation.Item3,
						true,
						operation.Item3 != null
					);
				}
				foreach (var operation in AnonymousOperations()) {
					yield return new PolicyVerificationParameters(user,
						operation.Item1, operation.Item2, operation.Item3,
						true,
						operation.Item3 != null
					);
				}
			}

			foreach (var user in anonymous) {
				foreach (var operation in SystemOperations()) {
					yield return new PolicyVerificationParameters(user,
						operation.Item1, operation.Item2, operation.Item3,
						false,
						false
					);
				}
				foreach (var operation in AdminOperations()) {
					yield return new PolicyVerificationParameters(user,
						operation.Item1, operation.Item2, operation.Item3,
						(_, _) => false,
						(allowAnonymousStreamAccess) => allowAnonymousStreamAccess
					);
				}
				foreach (var operation in OpsOperations()) {
					yield return new PolicyVerificationParameters(user,
						operation.Item1, operation.Item2, operation.Item3,
						false,
						operation.Item3 != null
					);
				}

				foreach (var operation in UserOperations()) {
					yield return new PolicyVerificationParameters(user,
						operation.Item1, operation.Item2, operation.Item3,
						false,
						false
					);
				}

				foreach (var operation in AuthenticatedOperations()) {
					yield return new PolicyVerificationParameters(user,
						operation.Item1, operation.Item2, operation.Item3,
						false,
						false
					);
				}
				foreach (var operation in AnonymousOperations()) {
					yield return new PolicyVerificationParameters(user,
						operation.Item1, operation.Item2, operation.Item3,
						true,
						operation.Item3 != null
					);
				}
				foreach (var operation in AllowAnonymousEndpointAccessOperations()) {
					yield return new PolicyVerificationParameters(user,
						operation.Item1, operation.Item2, operation.Item3,
						(allowAnonymousEndpointAccess, _) => allowAnonymousEndpointAccess,
						(_) => operation.Item3 != null
					);
				}
				foreach (var operation in AllowAnonymousStreamAccessOperations()) {
					yield return new PolicyVerificationParameters(user,
						operation.Item1, operation.Item2, operation.Item3,
						(_, allowAnonymousStreamAccess) => allowAnonymousStreamAccess,
						(allowAnonymousStreamAccess) => allowAnonymousStreamAccess
					);
				}
			}

			IEnumerable<(Operation, string, StorageMessage.EffectiveAcl)> SystemOperations() {
				yield return CreateOperation(Operations.Node.Gossip.Update);

				yield return CreateOperation(Operations.Node.Elections.Prepare);
				yield return CreateOperation(Operations.Node.Elections.PrepareOk);
				yield return CreateOperation(Operations.Node.Elections.ViewChange);
				yield return CreateOperation(Operations.Node.Elections.ViewChangeProof);
				yield return CreateOperation(Operations.Node.Elections.Proposal);
				yield return CreateOperation(Operations.Node.Elections.Accept);
				yield return CreateOperation(Operations.Node.Elections.LeaderIsResigning);
				yield return CreateOperation(Operations.Node.Elections.LeaderIsResigningOk);
				yield return CreateOperation(Operations.Node.Gossip.Read);
			}

			IEnumerable<(Operation, string, StorageMessage.EffectiveAcl)> AdminOperations() {
				yield return (new Operation(Operations.Streams.Read).WithParameter(
						Operations.Streams.Parameters.StreamId("$$$scavenge")),
					"$$$scavenge",
					systemStreamPermission);
			}

			IEnumerable<(Operation, string, StorageMessage.EffectiveAcl)> OpsOperations() {
				yield return CreateOperation(Operations.Node.Information.Subsystems);

				yield return CreateOperation(Operations.Node.Shutdown);
				yield return CreateOperation(Operations.Node.ReloadConfiguration);
				yield return CreateOperation(Operations.Node.Scavenge.Start);
				yield return CreateOperation(Operations.Node.Scavenge.Stop);
				yield return CreateOperation(Operations.Node.Scavenge.Read);
				yield return CreateOperation(Operations.Node.MergeIndexes);
				yield return CreateOperation(Operations.Node.SetPriority);
				yield return CreateOperation(Operations.Node.Resign);

				yield return CreateOperation(Operations.Subscriptions.Create);
				yield return CreateOperation(Operations.Subscriptions.Update);
				yield return CreateOperation(Operations.Subscriptions.Delete);
				yield return CreateOperation(Operations.Subscriptions.Restart);

				yield return CreateOperation(Operations.Node.Information.Histogram);
				yield return CreateOperation(Operations.Node.Information.Options);
				yield return (new Operation(Operations.Subscriptions.ReplayParked).WithParameter(Operations.Subscriptions.Parameters.StreamId(_streamWithCustomPermissions)), _streamWithCustomPermissions, null);
				yield return (new Operation(Operations.Subscriptions.ReplayParked).WithParameter(Operations.Subscriptions.Parameters.StreamId(_streamWithDefaultPermissions)), _streamWithDefaultPermissions, null);
				
				yield return CreateOperation(Operations.Projections.UpdateConfiguration);
				
				yield return CreateOperation(Operations.Projections.ReadConfiguration);
				yield return CreateOperation(Operations.Projections.Delete);
				yield return CreateOperation(Operations.Projections.Restart);
			}

			IEnumerable<(Operation, string, StorageMessage.EffectiveAcl)> UserOperations() {
				
				yield return (new Operation(Operations.Subscriptions.ProcessMessages).WithParameter(Operations.Subscriptions.Parameters.StreamId(_streamWithCustomPermissions)), _streamWithCustomPermissions, userStreamPermission);
				yield return (new Operation(Operations.Subscriptions.ProcessMessages).WithParameter(Operations.Subscriptions.Parameters.StreamId(_streamWithDefaultPermissions)), _streamWithDefaultPermissions, defaultUseruserStreamPermission);
				yield return CreateOperation(Operations.Projections.List);
				yield return CreateOperation(Operations.Projections.Abort);
				yield return CreateOperation(Operations.Projections.Create);
				yield return CreateOperation(Operations.Projections.DebugProjection);
				yield return CreateOperation(Operations.Projections.Disable);
				yield return CreateOperation(Operations.Projections.Enable);
				yield return CreateOperation(Operations.Projections.Read);
				yield return CreateOperation(Operations.Projections.Reset);
				yield return CreateOperation(Operations.Projections.Update);
				yield return CreateOperation(Operations.Projections.State);
				yield return CreateOperation(Operations.Projections.Status);
				yield return CreateOperation(Operations.Projections.Statistics);
			}

			IEnumerable<(Operation, string, StorageMessage.EffectiveAcl)> AuthenticatedOperations() {
				yield return CreateOperation(Operations.Subscriptions.Statistics);
				yield return CreateOperation(Operations.Projections.List);
			}

			IEnumerable<(Operation, string, StorageMessage.EffectiveAcl)> AnonymousOperations() {
				yield return CreateOperation(Operations.Node.Redirect);
				yield return CreateOperation(Operations.Node.StaticContent);
				yield return CreateOperation(Operations.Node.Ping);
				yield return CreateOperation(Operations.Node.Information.Read);
			}

			IEnumerable<(Operation, string, StorageMessage.EffectiveAcl)> AllowAnonymousEndpointAccessOperations() {
				yield return CreateOperation(Operations.Node.Options);
				yield return CreateOperation(Operations.Node.Statistics.Read);
				yield return CreateOperation(Operations.Node.Statistics.Replication);
				yield return CreateOperation(Operations.Node.Statistics.Tcp);
				yield return CreateOperation(Operations.Node.Statistics.Custom);
				yield return CreateOperation(Operations.Node.Gossip.ClientRead);
			}
			
			IEnumerable<(Operation, string, StorageMessage.EffectiveAcl)> AllowAnonymousStreamAccessOperations() {
				yield return (new Operation(Operations.Streams.Read).WithParameter(
						Operations.Streams.Parameters.StreamId(_streamWithDefaultPermissions)),
					_streamWithDefaultPermissions, defaultUseruserStreamPermission);
				yield return (new Operation(Operations.Streams.Write).WithParameter(
						Operations.Streams.Parameters.StreamId(_streamWithDefaultPermissions)),
					_streamWithDefaultPermissions, defaultUseruserStreamPermission);
				yield return (new Operation(Operations.Streams.Delete).WithParameter(
						Operations.Streams.Parameters.StreamId(_streamWithDefaultPermissions)),
					_streamWithDefaultPermissions, defaultUseruserStreamPermission);
				yield return (new Operation(Operations.Streams.MetadataRead).WithParameter(
						Operations.Streams.Parameters.StreamId(_streamWithDefaultPermissions)),
					_streamWithDefaultPermissions, defaultUseruserStreamPermission);
				yield return (new Operation(Operations.Streams.MetadataWrite).WithParameter(
						Operations.Streams.Parameters.StreamId(_streamWithDefaultPermissions)),
					_streamWithDefaultPermissions, defaultUseruserStreamPermission);
			}
			
			(Operation, string, StorageMessage.EffectiveAcl) CreateOperation(OperationDefinition def) {
				return (new Operation(def),null, null);
			}

			ClaimsPrincipal CreatePrincipal(string name, params string[] roles) {
				var claims =
					(new[] {new Claim(ClaimTypes.Name, name)}).Concat(roles.Select(x => new Claim(ClaimTypes.Role, x)));
				return new ClaimsPrincipal(new ClaimsIdentity(claims));
			}
		}


		[Test]
		public async Task VerifyPolicy([ValueSource(nameof(PolicyTests))]PolicyVerificationParameters pvp) {
			_aclResponder.ExpectedAcl(pvp.Stream, pvp.StreamAcl);
			var result =
					await _authorizationProvider.CheckAccessAsync(pvp.User, pvp.Operation, CancellationToken.None);
			if (pvp.AuthorizationCheck != null && pvp.AclCheck != null) {
				Assert.AreEqual(pvp.AuthorizationCheck(_allowAnonymousEndpointAccess, _allowAnonymousStreamAccess), result,
					pvp.AuthorizationCheck(_allowAnonymousEndpointAccess, _allowAnonymousStreamAccess)
						? "was not authorized"
						: "was authorized");
				Assert.AreEqual(pvp.AclCheck(_allowAnonymousStreamAccess), _aclResponder.MessageReceived,
					pvp.AclCheck(_allowAnonymousStreamAccess) ? "did not request acl" : "requested acl");
			} else {
				Assert.AreEqual(pvp.IsAuthorized, result,
					pvp.IsAuthorized
						? "was not authorized"
						: "was authorized");
				Assert.AreEqual(pvp.ShouldRequestAcl, _aclResponder.MessageReceived,
					pvp.ShouldRequestAcl ? "did not request acl" : "requested acl");
			}
		}
		
		class AclResponder : IPublisher {
			public bool MessageReceived { get; private set; }
			private StorageMessage.EffectiveAcl _acl;
			private string _expectedStream;

			public AclResponder() {
				MessageReceived = false;
			}
			public void Publish(Message message) {
				MessageReceived = true;
				Assert.IsInstanceOf<StorageMessage.EffectiveStreamAclRequest>(message);
				var request = (StorageMessage.EffectiveStreamAclRequest)message;
				Assert.AreEqual(_expectedStream, request.StreamId);
				Assert.NotNull(request.Envelope);
				request.Envelope.ReplyWith(new StorageMessage.EffectiveStreamAclResponse(_acl));
			}

			public void ExpectedAcl(string stream, StorageMessage.EffectiveAcl acl) {
				MessageReceived = false;
				if (stream == null) return;
				_expectedStream = SystemStreams.IsMetastream(stream) ? SystemStreams.OriginalStreamOf(stream) : stream;
				_acl = acl;
			}
		}
	}
}
