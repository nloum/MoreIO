using System.Collections.Generic;
using System.IO;
using System.Linq;
using Nuke.Common;
using Nuke.Common.CI;
using Nuke.Common.CI.AzurePipelines;
using Nuke.Common.CI.GitHubActions;
using Nuke.Common.CI.GitHubActions.Configuration;
using Nuke.Common.Execution;
using Nuke.Common.Git;
using Nuke.Common.IO;
using Nuke.Common.ProjectModel;
using Nuke.Common.Tooling;
using Nuke.Common.Tools.Coverlet;
using Nuke.Common.Tools.DotNet;
using Nuke.Common.Tools.GitVersion;
using Nuke.Common.Tools.Npm;
using Nuke.Common.Tools.ReportGenerator;
using Nuke.Common.Utilities.Collections;
using static Nuke.Common.IO.CompressionTasks;
using static Nuke.Common.IO.FileSystemTasks;
using static Nuke.Common.Tools.DotNet.DotNetTasks;
using static Nuke.Common.Tools.Npm.NpmTasks;
using static Nuke.Common.Tools.ReportGenerator.ReportGeneratorTasks;

[CheckBuildProjectConfigurations]
[UnsetVisualStudioEnvironmentVariables]
[GitHubActions("dotnetcore",
	GitHubActionsImage.Ubuntu1804,
	ImportSecrets = new[]{ "NUGET_API_KEY", "NETLIFY_PAT" },
	AutoGenerate = true,
	On = new [] { GitHubActionsTrigger.Push },
	InvokedTargets = new [] {"Push"}
	)]
class Build : NukeBuild
{
    /// Support plugins are available for:
    ///   - JetBrains ReSharper        https://nuke.build/resharper
    ///   - JetBrains Rider            https://nuke.build/rider
    ///   - Microsoft VisualStudio     https://nuke.build/visualstudio
    ///   - Microsoft VSCode           https://nuke.build/vscode

    public static int Main() => Execute<Build>(x => x.Compile);

    [Parameter("Configuration to build - Default is 'Debug' (local) or 'Release' (server)")]
    readonly Configuration Configuration = Configuration.Release;
    [Parameter("NuGet server URL.")]
	readonly string NugetSource = "https://api.nuget.org/v3/index.json";
	[Parameter("API Key for the NuGet server.")]
	readonly string NugetApiKey;
	[Parameter("Personal authentication token to push CI website to Netlify")]
	readonly string NetlifyPat;

    [Solution]
	readonly Solution Solution;
    [GitRepository]
	readonly GitRepository GitRepository;
 //    [GitVersion]
	// readonly GitVersion GitVersion;
	
    AbsolutePath SourceDirectory => RootDirectory / "src";
    AbsolutePath ArtifactsDirectory => RootDirectory / "artifacts";
    AbsolutePath WebsiteDirectory => RootDirectory / "website";
    AbsolutePath TestResultDirectory => ArtifactsDirectory / "test-results";

    Project[] PackageProjects => new[]
    {
	    Solution.GetProject("IoFluently"),
	    Solution.GetProject("IoFluently.SystemTextJson")
    };
    
    IEnumerable<Project> TestProjects => Solution.GetProjects("*.Test");
    
    Target Clean => _ => _
        .Before(Restore)
        .Executes(() =>
        {
            SourceDirectory.GlobDirectories("**/bin", "**/obj").ForEach(DeleteDirectory);
            EnsureCleanDirectory(ArtifactsDirectory);
        });

    Target Restore => _ => _
        .Executes(() =>
        {
            DotNetRestore(s => s
                .SetProjectFile(Solution)
			);
        });

    Target Compile => _ => _
        .DependsOn(Restore)
        .Executes(() =>
        {
            DotNetBuild(s => s
                .EnableNoRestore()
                .SetProjectFile(Solution)
                .SetConfiguration(Configuration)
                .SetAssemblyVersion(GitVersion.AssemblySemVer)
                .SetFileVersion(GitVersion.AssemblySemFileVer)
                .SetInformationalVersion(GitVersion.InformationalVersion)
			);

            DotNetPublish(s => s
				.EnableNoRestore()
				.EnableNoBuild()
				.SetConfiguration(Configuration)
				.SetAssemblyVersion(GitVersion.AssemblySemVer)
				.SetFileVersion(GitVersion.AssemblySemFileVer)
				.SetInformationalVersion(GitVersion.InformationalVersion)
				.CombineWith(
					from project in PackageProjects
					from framework in project.GetTargetFrameworks()
                    select new { project, framework }, (cs, v) => cs
						.SetProject(v.project)
						.SetFramework(v.framework)
				)
			);
        });
    
    Target Pack => _ => _
        .DependsOn(Compile)
		.Requires(() => Configuration == Configuration.Release)
        .Executes(() =>
        {
            DotNetPack(s => s
                .EnableNoRestore()
                .EnableNoBuild()
                .SetConfiguration(Configuration)
                .SetOutputDirectory(ArtifactsDirectory)
                .SetVersion(GitVersion.NuGetVersionV2)
				.SetIncludeSymbols(true)
				.SetSymbolPackageFormat(DotNetSymbolPackageFormat.snupkg)
                .CombineWith(
	                from project in PackageProjects
	                select new { project }, (cs, v) => cs
		                .SetProject(v.project)
                )
            );
        });

    Target Push => _ => _
        .DependsOn(Pack)
        .Consumes(Pack)
        .Requires(() => Configuration == Configuration.Release)
        .Executes(() =>
        {
            DotNetNuGetPush(s => s
				.SetSource(NugetSource)
				.SetApiKey(NugetApiKey)
				.SetSkipDuplicate(true)
				.CombineWith(ArtifactsDirectory.GlobFiles("*.nupkg"), (s, v) => s
					.SetTargetPath(v)
				)
            );
        });


    public GitVersion GitVersion
    {
	    get
	    {
		    var package = NuGetPackageResolver.GetGlobalInstalledPackage("GitVersion.Tool", "5.6.0", null);
		    var settings = new GitVersionSettings().SetProcessToolPath(package.Directory / "tools/net5.0/any/gitversion.dll");
		    var gitVersion = GitVersionTasks.GitVersion(settings).Result;
		    return gitVersion;
	    }
    }
}
