using HdrDoctor.Core;
using HdrDoctor.Core.Checks;
using HdrDoctor.Core.Model;

namespace HdrDoctor.Tests;

public class StageAltsCheckTests
{
    [Fact]
    public async Task Missing_hashes_file_is_critical()
    {
        using var sd = new SdFixture().WithHealthyInstall();
        File.Delete(Path.Combine(sd.Root, HdrPaths.StageAltsHashes.Replace('/', Path.DirectorySeparatorChar)));

        var findings = await sd.RunAsync(new StageAltsCheck());

        var finding = findings.Single("hash table is missing");
        Assert.Equal(Severity.Critical, finding.Severity);
        Assert.Contains(HdrPaths.StageAltsHashes, finding.Paths);
    }

    [Fact]
    public async Task Truncated_hashes_file_is_critical()
    {
        // A few kilobytes is what an interrupted download leaves behind. It fails the
        // same way a missing file does, so it must not pass a bare existence check.
        using var sd = new SdFixture()
            .WithHealthyInstall()
            .WithFileOfSize(HdrPaths.StageAltsHashes, 4096);

        var findings = await sd.RunAsync(new StageAltsCheck());

        Assert.Equal(Severity.Critical, findings.Single("looks truncated").Severity);
    }

    [Fact]
    public async Task Full_sized_hashes_file_passes()
    {
        using var sd = new SdFixture().WithHealthyInstall();

        var findings = await sd.RunAsync(new StageAltsCheck());

        Assert.Empty(findings.Problems());
    }
}
