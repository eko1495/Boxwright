using Microsoft.Extensions.Logging;
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
    public async Task CreateAsync_StampsAUniqueMac_WhenNoneProvided()
    {
        await WithTempRootAsync(async repo =>
        {
            Vm a = await repo.CreateAsync(new VmConfig { Name = "a" });
            Vm b = await repo.CreateAsync(new VmConfig { Name = "b" });

            Assert.True(MacAddress.IsValid(a.Config.Network.MacAddress));
            Assert.True(MacAddress.IsValid(b.Config.Network.MacAddress));
            Assert.NotEqual(a.Config.Network.MacAddress, b.Config.Network.MacAddress);
        });
    }

    [Fact]
    public async Task CreateAsync_KeepsAProvidedMac()
    {
        await WithTempRootAsync(async repo =>
        {
            Vm vm = await repo.CreateAsync(new VmConfig
            {
                Name = "fixed-mac",
                Network = new NetworkConfig { MacAddress = "52:54:00:11:22:33" },
            });

            Assert.Equal("52:54:00:11:22:33", vm.Config.Network.MacAddress);
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
    public async Task SaveAsyncVm_WritesIntoTheVmsActualFolder_NotRootId()
    {
        // ADR-0028: a slugged folder is not root/id. SaveAsync(Vm) must honor the VM's actual folder so an
        // edit doesn't re-create root/id and orphan the slug folder. Simulate a renamed VM via MoveFolderAsync.
        await WithTempRootAsync(async repo =>
        {
            Vm vm = await repo.CreateAsync(new VmConfig { Name = "edit-me", MemoryMiB = 2048 });
            Vm relocated = await repo.MoveFolderAsync(vm, "edit-me-slug");

            await repo.SaveAsync(relocated with { Config = relocated.Config with { MemoryMiB = 8192 } });

            // Written into the slug folder, and root/id was NOT re-created.
            Assert.True(File.Exists(Path.Combine(repo.RootDirectory, "edit-me-slug", VmRepository.ConfigFileName)));
            Assert.False(Directory.Exists(Path.Combine(repo.RootDirectory, vm.Config.Id)));
            Vm only = Assert.Single(await repo.ListAsync());
            Assert.Equal(8192, only.Config.MemoryMiB);
            Assert.Equal(vm.Config.Id, only.Config.Id); // id still the key
        });
    }

    [Fact]
    public async Task SaveAsyncConfig_OnARenamedVm_StaysInTheSlugFolder_AndDoesNotOrphan()
    {
        // ADR-0028 regression: every edit path goes through SaveAsync(VmConfig), which must resolve the
        // VM's actual (slug) folder by id — NOT re-create root/id and split the config from its disks.
        await WithTempRootAsync(async repo =>
        {
            Vm vm = await repo.CreateAsync(new VmConfig { Name = "edit-me", MemoryMiB = 2048 });
            await repo.MoveFolderAsync(vm, "edit-me-slug");

            // A caller that only holds the config (e.g. NetCommand / UsbCommand / the GUI settings save).
            await repo.SaveAsync(vm.Config with { MemoryMiB = 8192 });

            Assert.True(File.Exists(Path.Combine(repo.RootDirectory, "edit-me-slug", VmRepository.ConfigFileName)));
            Assert.False(Directory.Exists(Path.Combine(repo.RootDirectory, vm.Config.Id))); // no root/id orphan
            Vm only = Assert.Single(await repo.ListAsync());                                  // exactly one VM, not two
            Assert.Equal(8192, only.Config.MemoryMiB);
        });
    }

    [Fact]
    public async Task DeleteAsync_RemovesARenamedVmsSlugFolder()
    {
        // DeleteAsync(id) must also resolve the slug folder — a naive root/id delete would no-op and leave it.
        await WithTempRootAsync(async repo =>
        {
            Vm vm = await repo.CreateAsync(new VmConfig { Name = "trash-me" });
            Vm relocated = await repo.MoveFolderAsync(vm, "trash-me-slug");

            await repo.DeleteAsync(vm.Config.Id);

            Assert.False(Directory.Exists(relocated.FolderPath));
            Assert.Empty(await repo.ListAsync());
        });
    }

    [Fact]
    public async Task MoveFolderAsync_RelocatesTheFolderAndKeepsTheConfig()
    {
        await WithTempRootAsync(async repo =>
        {
            Vm vm = await repo.CreateAsync(new VmConfig { Name = "movable" });
            string oldFolder = vm.FolderPath;

            Vm moved = await repo.MoveFolderAsync(vm, "movable-slug");

            Assert.False(Directory.Exists(oldFolder));
            Assert.True(File.Exists(moved.ConfigPath));
            Assert.EndsWith("movable-slug", moved.FolderPath.TrimEnd(Path.DirectorySeparatorChar), StringComparison.Ordinal);
            Assert.Equal(vm.Config.Id, moved.Config.Id);
        });
    }

    [Fact]
    public async Task DeleteAsync_RemovesTheVmFolder()
    {
        await WithTempRootAsync(async repo =>
        {
            Vm vm = await repo.CreateAsync(new VmConfig { Name = "trash-me" });
            Assert.True(Directory.Exists(vm.FolderPath));

            await repo.DeleteAsync(vm.Config.Id);

            Assert.False(Directory.Exists(vm.FolderPath));
            Assert.Empty(await repo.ListAsync());
        });
    }

    [Fact]
    public async Task DeleteAsync_WhenFolderMissing_DoesNotThrow()
    {
        await WithTempRootAsync(repo => repo.DeleteAsync("does-not-exist"));
    }

    [Fact]
    public void DefaultRootDirectory_IsUnderBoxwright()
    {
        string path = VmRepository.DefaultRootDirectory;

        Assert.Contains("Boxwright", path, StringComparison.Ordinal);
        Assert.EndsWith("VMs", path, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ListAsync_LogsAWarning_ForASkippedBrokenConfig()
    {
        string root = Path.Combine(Path.GetTempPath(), $"boxwright-repo-{Guid.NewGuid():N}");
        var logger = new CapturingLogger();
        try
        {
            var repo = new VmRepository(root, logger);
            await repo.CreateAsync(new VmConfig { Name = "valid" });
            string bogus = Path.Combine(root, "bogus");
            Directory.CreateDirectory(bogus);
            await File.WriteAllTextAsync(Path.Combine(bogus, VmRepository.ConfigFileName), "{ not json");

            IReadOnlyList<Vm> vms = await repo.ListAsync();

            Assert.Single(vms); // the broken folder is still skipped
            string warning = Assert.Single(logger.Warnings);
            Assert.Contains("bogus", warning, StringComparison.Ordinal); // and the skip is now visible
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
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

    private sealed class CapturingLogger : ILogger<VmRepository>
    {
        public List<string> Warnings { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Warning)
            {
                Warnings.Add(formatter(state, exception));
            }
        }
    }
}
