using System.Text;
using DiscUtils.Iso9660;
using Xunit;

namespace Boxwright.Core.Tests;

public sealed class RecipeTemplateTests
{
    private static readonly UnattendedAnswers Answers = new()
    {
        Username = "alice",
        Password = "secret",
        Hostname = "host1",
        Locale = "en_GB.UTF-8",
        Timezone = "Europe/Warsaw",
        KeyboardLayout = "gb",
    };

    [Fact]
    public void Substitute_ReplacesAllPlaceholders()
    {
        const string template = "u={username} p={password} h={passwordHash} host={hostname} l={locale} tz={timezone} kb={keyboard} label={isoLabel}";

        string result = RecipeTemplate.Substitute(template, Answers, "HASHED", "MYISO");

        Assert.Equal("u=alice p=secret h=HASHED host=host1 l=en_GB.UTF-8 tz=Europe/Warsaw kb=gb label=MYISO", result);
    }

    [Fact]
    public void Substitute_UsesTheSuppliedHash_NotARecomputedOne()
    {
        // The hash is passed in (computed once per install) so two references stay identical.
        string result = RecipeTemplate.Substitute("{passwordHash}/{passwordHash}", Answers, "ONCE", "x");

        Assert.Equal("ONCE/ONCE", result);
    }

    [Fact]
    public void Substitute_LeavesUnknownTokensUntouched()
    {
        Assert.Equal("keep {unknown}", RecipeTemplate.Substitute("keep {unknown}", Answers, "h", "x"));
    }
}

public sealed class RecipeInstallerTests : IDisposable
{
    private readonly string _dir;

    public RecipeInstallerTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"boxwright-recipe-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    // Builds a small ISO with a kernel + initrd at the given paths (extensioned names dodge DiscUtils'
    // trailing-dot mangling of extension-less files — see InstallMediaExtractorTests).
    private string BuildIso(string label, string kernelPath, string initrdPath, byte[] initrd)
    {
        var builder = new CDBuilder { UseJoliet = true, VolumeIdentifier = label };
        builder.AddFile(kernelPath, Encoding.UTF8.GetBytes("fake-kernel"));
        builder.AddFile(initrdPath, initrd);
        string iso = Path.Combine(_dir, "test.iso");
        builder.Build(iso);
        return iso;
    }

    [Fact]
    public void Prepare_InitrdInject_ExtractsKernelInitrd_InjectsSeed_AndTemplatesAppend()
    {
        byte[] initrd = Encoding.UTF8.GetBytes("original-initrd-bytes");
        string iso = BuildIso("INSTLABEL", "boot/kernel.img", "boot/initrd.gz", initrd);
        string vmFolder = Path.Combine(_dir, "vm");
        var recipe = new UnattendedRecipe
        {
            Kind = UnattendedRecipe.KindInitrdInject,
            KernelPath = "boot/kernel.img",
            InitrdPaths = ["boot/missing.gz", "boot/initrd.gz"], // first-exists wins
            SeedFileName = "preseed.cfg",
            SeedTemplate = "d-i passwd/username string {username}",
            Append = "auto=true hostname={hostname} inst.stage2=hd:LABEL={isoLabel}",
        };
        var answers = new UnattendedAnswers { Username = "bob", Password = "pw", Hostname = "node7" };

        UnattendedInstallPlan plan = new RecipeInstaller().Prepare(recipe, iso, vmFolder, answers);

        // Kernel + initrd copied into the VM folder.
        Assert.True(File.Exists(Path.Combine(vmFolder, "vmlinuz")));
        string initrdDest = Path.Combine(vmFolder, "initrd");
        Assert.True(File.Exists(initrdDest));
        Assert.True(new FileInfo(initrdDest).Length > initrd.Length); // the seed was appended

        // Plan: kernel/initrd names, no seed disk (the seed is inside the initrd), templated append.
        Assert.Equal("vmlinuz", plan.Boot.KernelFile);
        Assert.Equal("initrd", plan.Boot.InitrdFile);
        Assert.Empty(plan.SeedDisks);
        // {username}/{hostname}/{isoLabel} substituted. (CDBuilder pads the Joliet volume id, so the
        // label readback has trailing fill chars in this synthetic ISO — real distro ISOs come back clean,
        // as the FedoraKickstart path is verified to; assert the meaningful prefix, not the padding.)
        Assert.StartsWith("auto=true hostname=node7 inst.stage2=hd:LABEL=INSTLABEL", plan.Boot.Append, StringComparison.Ordinal);
        Assert.DoesNotContain("{", plan.Boot.Append, StringComparison.Ordinal); // all placeholders resolved
    }

    [Fact]
    public void Prepare_UnsupportedKind_Throws()
    {
        string iso = BuildIso("X", "boot/k.img", "boot/i.gz", [1, 2, 3]);
        var recipe = new UnattendedRecipe { Kind = "cloud-init", KernelPath = "boot/k.img", InitrdPaths = ["boot/i.gz"], SeedFileName = "user-data" };

        Assert.Throws<InstallMediaException>(() =>
            new RecipeInstaller().Prepare(recipe, iso, Path.Combine(_dir, "vm"), new UnattendedAnswers()));
    }

    [Fact]
    public void Prepare_MissingKernelInIso_Throws()
    {
        string iso = BuildIso("X", "boot/k.img", "boot/i.gz", [1, 2, 3]);
        var recipe = new UnattendedRecipe
        {
            Kind = UnattendedRecipe.KindInitrdInject,
            KernelPath = "boot/nope.img",
            InitrdPaths = ["boot/i.gz"],
            SeedFileName = "preseed.cfg",
        };

        Assert.Throws<InstallMediaException>(() =>
            new RecipeInstaller().Prepare(recipe, iso, Path.Combine(_dir, "vm"), new UnattendedAnswers()));
    }

    [Fact]
    public void Prepare_NoMatchingInitrd_Throws()
    {
        string iso = BuildIso("X", "boot/k.img", "boot/i.gz", [1, 2, 3]);
        var recipe = new UnattendedRecipe
        {
            Kind = UnattendedRecipe.KindInitrdInject,
            KernelPath = "boot/k.img",
            InitrdPaths = ["boot/absent.gz"],
            SeedFileName = "preseed.cfg",
        };

        Assert.Throws<InstallMediaException>(() =>
            new RecipeInstaller().Prepare(recipe, iso, Path.Combine(_dir, "vm"), new UnattendedAnswers()));
    }
}
