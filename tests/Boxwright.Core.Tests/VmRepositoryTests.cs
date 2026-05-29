using Xunit;

namespace Boxwright.Core.Tests;

// CORE-3: VM folder repository (ADR-0006). Runs against a fresh temp root per test.
public class VmRepositoryTests
{
    [Fact]
    public async Task ListAsync_WhenRootMissing_ReturnsEmpty()
    {
        await WithTempRootAsync(async repo =>
        {
            IReadOnlyList<Vm> vms = await repo.ListAsync();
            Assert.Empty(vms);
        });
    }

    [Fact]
    public async Task CreateAsync_WritesSelfContainedFolderUnderRoot()
    {
        await WithTempRootAsync(async repo =>
        {
            Vm vm = await repo.CreateAsync(new VmConfig { Name = "Ubuntu" });

            Assert.StartsWith(repo.RootDirectory, vm.FolderPath, StringComparison.Ordinal);
            Assert.True(File.Exists(vm.ConfigPath));
            VmConfig reloaded = await VmConfigJson.LoadAsync(vm.ConfigPath);
            Assert.Equal("Ubuntu", reloaded.Name);
        });
    }

    [Fact]
    public async Task CreateAsync_GeneratesId_WhenMissing()
    {
        await WithTempRootAsync(async repo =>
        {
            Vm vm = await repo.CreateAsync(new VmConfig { Name = "no-id" });

            Assert.False(string.IsNullOrWhiteSpace(vm.Config.Id));
            Assert.EndsWith(vm.Config.Id, vm.FolderPath, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task CreateAsync_KeepsProvidedId()
    {
        await WithTempRootAsync(async repo =>
        {
            Vm vm = await repo.CreateAsync(new VmConfig { Id = "fixed-id", Name = "x" });

            Assert.Equal("fixed-id", vm.Config.Id);
        });
    }

    [Fact]
    public async Task ListAsync_DiscoversCreatedVms()
    {
        await WithTempRootAsync(async repo =>
        {
            await repo.CreateAsync(new VmConfig { Name = "one" });
            await repo.CreateAsync(new VmConfig { Name = "two" });

            IReadOnlyList<Vm> vms = await repo.ListAsync();

            Assert.Equal(2, vms.Count);
            Assert.Contains(vms, v => v.Config.Name == "one");
            Assert.Contains(vms, v => v.Config.Name == "two");
        });
    }

    [Fact]
    public async Task ListAsync_SkipsFoldersWithoutOrWithInvalidConfig()
    {
        await WithTempRootAsync(async repo =>
        {
            await repo.CreateAsync(new VmConfig { Name = "valid" });
            Directory.CreateDirectory(Path.Combine(repo.RootDirectory, "empty-folder"));
            string bogus = Path.Combine(repo.RootDirectory, "bogus");
            Directory.CreateDirectory(bogus);
            await File.WriteAllTextAsync(Path.Combine(bogus, VmRepository.ConfigFileName), "{ not json");

            IReadOnlyList<Vm> vms = await repo.ListAsync();

            Vm only = Assert.Single(vms);
            Assert.Equal("valid", only.Config.Name);
        });
    }

    [Fact]
    public async Task SaveAsync_PersistsEdits()
    {
        await WithTempRootAsync(async repo =>
        {
            Vm vm = await repo.CreateAsync(new VmConfig { Name = "edit-me", MemoryMiB = 2048 });

            await repo.SaveAsync(vm.Config with { MemoryMiB = 8192 });

            VmConfig reloaded = await VmConfigJson.LoadAsync(vm.ConfigPath);
            Assert.Equal(8192, reloaded.MemoryMiB);
        });
    }

    [Fact]
    public void DefaultRootDirectory_IsUnderBoxwright()
    {
        string path = VmRepository.DefaultRootDirectory;

        Assert.Contains("Boxwright", path, StringComparison.Ordinal);
        Assert.EndsWith("VMs", path, StringComparison.Ordinal);
    }

    private static async Task WithTempRootAsync(Func<VmRepository, Task> body)
    {
        string root = Path.Combine(Path.GetTempPath(), $"boxwright-repo-{Guid.NewGuid():N}");
        try
        {
            await body(new VmRepository(root));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
