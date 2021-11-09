// Copyright 2021 Maintainers of NUKE.
// Distributed under the MIT License.
// https://github.com/nuke-build/nuke/blob/master/LICENSE

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text.RegularExpressions;
using JetBrains.Annotations;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using Nuke.Common.CI.AppVeyor;
using Nuke.Common.CI.AzurePipelines;
using Nuke.Common.CI.GitHubActions;
using Nuke.Common.CI.GitLab;
using Nuke.Common.CI.TeamCity;
using Nuke.Common.Execution;
using Nuke.Common.Git;
using Nuke.Common.Tooling;
using Nuke.Common.Tools.Git;
using Nuke.Common.Utilities;
using Nuke.Common.Utilities.Net;
using Serilog;

namespace Nuke.Common.Notifications
{
    public class NotificationsAttribute
        : BuildExtensionAttributeBase,
            IOnBuildCreated,
            IOnTargetRunning,
            IOnBuildFinished
    {
        private const string DefaultFunctionsHost = "https://hellonukefoobar3.azurewebsites.net";

        public string VersionParameter { get; set; }
        public bool EnableAuthorizedActions { get; set; }
        public bool Debug { get; set; }

        // private string FunctionsHost => Environment.GetEnvironmentVariable("NUKE_FUNCTIONS_HOST") ?? DefaultFunctionsHost;
        // private string AccessToken => Environment.GetEnvironmentVariable("NUKE_ACCESS_TOKEN").NotNull();
        private string FunctionsHost => "https://functionapptest-nuke.azurewebsites.net";
        private string AccessToken => "pgjjhoQURfsrgiHGzYDcTCFDGfx5Ankg+XaaRg8QzgM=";
        private string BuildStatusEndpoint => $"{FunctionsHost}/api/build/status";

        private readonly HttpClient Client = new HttpClient();

        private readonly Guid _cookie = Guid.NewGuid();
        private readonly JsonSerializerSettings _serializerSettings;
        private DateTime _started;
        private IReadOnlyCollection<ExecutableTarget> _targets;
        private GitRepository _repository;
        private IReadOnlyCollection<Commit> _commits;

        public NotificationsAttribute()
        {
            _serializerSettings = new JsonSerializerSettings { ContractResolver = new HostContractResolver(ShouldSerialize) };
        }

        public void OnBuildCreated(NukeBuild build, IReadOnlyCollection<ExecutableTarget> executableTargets)
        {
            _started = DateTime.Now;
            _targets = executableTargets;
            _repository = GitRepository.FromLocalDirectory(NukeBuild.RootDirectory);
            _commits = GetCommits(_repository.Commit)?.ToList();

            PostStatus(build, UpdateReason.BuildCreated);
        }

        public void OnTargetRunning(NukeBuild build, ExecutableTarget target)
        {
            PostStatus(build, UpdateReason.TargetStarted);
        }

        public void OnBuildFinished(NukeBuild build)
        {
            PostStatus(build, UpdateReason.BuildFinished);
        }

        private void PostStatus(NukeBuild build, UpdateReason updateReason)
        {
            // if (NukeBuild.IsLocalBuild)
            //     return;

            TargetStatus GetStatus(ExecutableTarget target) =>
                new TargetStatus
                {
                    Name = target.Name,
                    Status = target.Status,
                    Duration = target.Duration,
                    Data = target.SummaryInformation
                };

            var status =
                new BuildStatus
                {
                    Started = _started,
                    Host = NukeBuild.Host.GetType().Name,
                    HostInformation = JsonConvert.SerializeObject(NukeBuild.Host, _serializerSettings),
                    Version = "1.2.3",
                    Repository = _repository.HttpsUrl.TrimEnd(".git"),
                    Branch = _repository.Branch,
                    Commits = _commits,
                    Targets = _targets.Select(GetStatus).ToList(),
                    // IsFinished = build.IsFinished,
                    // IsSuccessful = build.IsSuccessful,
                    ErrorMessage = Logging.InMemorySink.Instance.LogEvents.Select(x => x.MessageTemplate.Text).JoinNewLine(),
                    ExitCode = build.ExitCode
                };

            var message =
                new BuildUpdateMessage
                {
                    AccessToken = AccessToken,
                    Cookie = _cookie,
                    UpdateReason = updateReason,
                    TimeCreated = DateTime.Now,
                    // TODO: encrypt
                    Status = status
                };

            if (Debug)
            {
                Console.WriteLine(status.HostInformation);
                return;
            }

            try
            {
                var response = Client
                    .CreateRequest(HttpMethod.Post, BuildStatusEndpoint)
                    .WithJsonContent(message)
                    .GetResponse();
                response.AssertStatusCode(HttpStatusCode.OK);
            }
            catch (Exception exception)
            {
                Log.Warning(exception, "Reporting build status to {Endpoint} failed", BuildStatusEndpoint);
            }
        }

        private bool ShouldSerialize(MemberInfo member)
        {
            return NukeBuild.Host switch
            {
                AppVeyor =>
                    member.Name.EqualsAnyOrdinalIgnoreCase(
                        nameof(AppVeyor.Url),
                        nameof(AppVeyor.AccountName),
                        nameof(AppVeyor.ProjectSlug),
                        nameof(AppVeyor.BuildId),
                        nameof(AppVeyor.BuildVersion),
                        nameof(AppVeyor.JobName),
                        nameof(AppVeyor.JobId),
                        nameof(AppVeyor.RepositoryBranch),
                        nameof(AppVeyor.ProjectName)),
                TeamCity =>
                    member.Name.EqualsAnyOrdinalIgnoreCase(
                        nameof(TeamCity.ServerUrl),
                        nameof(TeamCity.ProjectName),
                        nameof(TeamCity.ProjectId),
                        nameof(TeamCity.BuildTypeId),
                        nameof(TeamCity.BuildId),
                        nameof(TeamCity.BuildNumber),
                        nameof(TeamCity.BuildConfiguration),
                        nameof(TeamCity.BranchName)) ||
                    EnableAuthorizedActions && member.Name.EqualsAnyOrdinalIgnoreCase(
                        nameof(TeamCity.AuthUserId),
                        nameof(TeamCity.AuthPassword)),
                AzurePipelines =>
                    member.Name.EqualsAnyOrdinalIgnoreCase(
                        nameof(AzurePipelines.TeamFoundationCollectionUri),
                        nameof(AzurePipelines.TeamProject),
                        nameof(AzurePipelines.DefinitionName),
                        nameof(AzurePipelines.DefinitionId),
                        nameof(AzurePipelines.BuildId),
                        nameof(AzurePipelines.BuildNumber),
                        nameof(AzurePipelines.StageName),
                        nameof(AzurePipelines.JobId),
                        nameof(AzurePipelines.TaskInstanceId)) ||
                    EnableAuthorizedActions && member.Name.EqualsAnyOrdinalIgnoreCase(
                        nameof(AzurePipelines.AccessToken)),
                GitHubActions =>
                    member.Name.EqualsAnyOrdinalIgnoreCase(
                        nameof(GitHubActions.ServerUrl),
                        nameof(GitHubActions.Repository),
                        nameof(GitHubActions.Workflow),
                        nameof(GitHubActions.RunId),
                        nameof(GitHubActions.RunNumber),
                        nameof(GitHubActions.JobId),
                        nameof(GitHubActions.Job),
                        nameof(GitHubActions.Ref)) ||
                    EnableAuthorizedActions && member.Name.EqualsAnyOrdinalIgnoreCase(
                        nameof(GitHubActions.Token)),
                GitLab =>
                    member.Name.EqualsAnyOrdinalIgnoreCase(
                        nameof(GitLab.ProjectUrl),
                        nameof(GitLab.PipelineId),
                        nameof(GitLab.ProjectName)),
                _ =>
                    throw new NotSupportedException(NukeBuild.Host.ToString())
            };
        }

        [CanBeNull]
        private static IEnumerable<Commit> GetCommits(string commit)
        {
            var regex = new Regex(@"^(?'sha'[^\t]+)\t(?'author'[^\t]+)\t(?'email'[^\t]+)\t(?'message'[^\t]+)$");
            try
            {
                var commitData = GitTasks.Git($"log {commit}^.. --pretty=tformat:{"%H\t%an\t%ae\t%s".DoubleQuote()}")
                    .EnsureOnlyStd()
                    .Select(x => regex.Match(x.Text))
                    .Select(match =>
                        new Commit
                        {
                            Sha = match.Groups["sha"].Value,
                            Message = match.Groups["message"].Value,
                            Author = match.Groups["author"].Value,
                            Email = match.Groups["email"].Value
                        }).ToList();
                Assert.NotEmpty(commitData);
                return commitData;
            }
            catch
            {
                return null;
            }
        }

        internal class HostContractResolver : DefaultContractResolver
        {
            private readonly Func<MemberInfo, bool> _shouldSerialize;

            public HostContractResolver(Func<MemberInfo, bool> shouldSerialize)
            {
                _shouldSerialize = shouldSerialize;
            }

            protected override JsonProperty CreateProperty(MemberInfo member, MemberSerialization memberSerialization)
            {
                var property = base.CreateProperty(member, memberSerialization);
                property.ShouldSerialize = _ => _shouldSerialize.Invoke(member);
                return property;
            }
        }
    }
}
