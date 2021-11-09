// Copyright 2021 Maintainers of NUKE.
// Distributed under the MIT License.
// https://github.com/nuke-build/nuke/blob/master/LICENSE

using System;
using System.Collections.Generic;
using System.Linq;
using Nuke.Common.Execution;

namespace Nuke.Common.Notifications
{
    public class BuildUpdateMessage
    {
        public string AccessToken { get; set; }
        public Guid Cookie { get; set; }
        public UpdateReason UpdateReason { get; set; }
        public DateTime TimeCreated { get; set; }
        public BuildStatus Status { get; set; }
    }

    public class BuildStatus
    {
        public DateTime Started { get; set; }
        public string Host { get; set; }
        public string HostInformation { get; set; }
        public string Version { get; set; }
        public string Repository { get; set; }
        public string Branch { get; set; }
        public IReadOnlyCollection<Commit> Commits { get; set; }
        public List<TargetStatus> Targets { get; set; }
        public string ErrorMessage { get; set; }
        public int? ExitCode { get; set; }
    }

    public class TargetStatus
    {
        public TimeSpan Duration { get; set; }
        public ExecutionStatus Status { get; set; }
        public string Name { get; set; }
        public Dictionary<string, string> Data { get; set; }
    }

    public class Commit
    {
        public string Sha { get; set; }
        public string Message { get; set; }
        public string Author { get; set; }
        public string Email { get; set; }
    }

    public enum UpdateReason
    {
        BuildCreated,
        TargetStarted,
        BuildFinished
    }
}
