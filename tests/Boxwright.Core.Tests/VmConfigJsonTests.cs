using Xunit;

namespace Boxwright.Core.Tests;

// CORE-1: VmConfig model + JSON load/save (schemaVersion 1).
public class VmConfigJsonTests
{
    // Mirrors the architecture.md §9 example config.
    private const string Example = """
        {
          "schemaVersion": 1,
          "id": "9f1c2a3e-1111-2222-3333-444455556666",
          "name": "Ubuntu 24.04",
          "arch": "x86_64",
          "machine": "q35",
          "firmware": "uefi",
          "cpu": { "model": "host", "sockets": 1, "cores": 4, "threads": 1 },
          "memoryMiB": 4096,
          "disks": [ { "file": "disk.qcow2", "format": "qcow2", "interface": "virtio" } ],
          "removableMedia": [ { "type": "cdrom", "file": "ubuntu-24.04.iso", "attached": true } ],
          "network": { "mode": "user", "model": "virtio-net", "portForwards": [ { "hostPort": 2222, "guestPort": 22 } ] },
          "display": { "protocol": "spice", "gl": false },
          "accelerator": "auto",
          "boot": { "order": "cd", "menu": false }
        }
        """;

    [Fact]
    public void Deserialize_Example_ParsesAllFields()
    {
        VmConfig config = VmConfigJson.Deserialize(Example);

        Assert.Equal(1, config.SchemaVersion);
        Assert.Equal("Ubuntu 24.04", config.Name);
        Assert.Equal("x86_64", config.Arch);
        Assert.Equal("q35", config.Machine);
        Assert.Equal("uefi", config.Firmware);
        Assert.Equal("host", config.Cpu.Model);
        Assert.Equal(4, config.Cpu.Cores);
        Assert.Equal(4096, config.MemoryMiB);
        Assert.Equal("disk.qcow2", Assert.Single(config.Disks).File);
        Assert.Equal("virtio", config.Disks[0].Interface);
        RemovableMediaConfig media = Assert.Single(config.RemovableMedia);
        Assert.Equal("ubuntu-24.04.iso", media.File);
        Assert.True(media.Attached);
        Assert.Equal("user", config.Network.Mode);
        PortForward forward = Assert.Single(config.Network.PortForwards);
        Assert.Equal(2222, forward.HostPort);
        Assert.Equal(22, forward.GuestPort);
        Assert.Equal("spice", config.Display.Protocol);
        Assert.Equal("auto", config.Accelerator);
        Assert.Equal("cd", config.Boot.Order);
    }

    [Fact]
    public void RoundTrip_PreservesFields()
    {
        VmConfig original = VmConfigJson.Deserialize(Example);

        string json = VmConfigJson.Serialize(original);
        VmConfig roundTripped = VmConfigJson.Deserialize(json);

        Assert.Equal(original.Name, roundTripped.Name);
        Assert.Equal(original.MemoryMiB, roundTripped.MemoryMiB);
        Assert.Equal(original.Firmware, roundTripped.Firmware);
        Assert.Equal(original.Cpu, roundTripped.Cpu);
        Assert.Equal(original.Disks[0], roundTripped.Disks[0]);
        Assert.Equal(original.Network.PortForwards[0], roundTripped.Network.PortForwards[0]);
        Assert.Equal(original.Accelerator, roundTripped.Accelerator);
        Assert.Equal(original.Boot, roundTripped.Boot);
    }

    [Fact]
    public void Serialize_PersistsAcceleratorAsAuto_NeverConcrete()
    {
        var config = new VmConfig { Name = "test" };

        string json = VmConfigJson.Serialize(config);

        Assert.Contains("\"accelerator\": \"auto\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("kvm", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Deserialize_FutureSchemaVersion_Throws()
    {
        const string future = "{ \"schemaVersion\": 2, \"name\": \"from the future\" }";

        VmConfigException ex = Assert.Throws<VmConfigException>(() => VmConfigJson.Deserialize(future));

        Assert.Contains("2", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Deserialize_MalformedJson_Throws()
    {
        Assert.Throws<VmConfigException>(() => VmConfigJson.Deserialize("{ this is not json"));
    }

    [Fact]
    public void Deserialize_AllowsCommentsAndTrailingCommas()
    {
        const string jsonc = """
            {
              "schemaVersion": 1,
              "name": "commented", // hand-edited
              "memoryMiB": 8192,
            }
            """;

        VmConfig config = VmConfigJson.Deserialize(jsonc);

        Assert.Equal("commented", config.Name);
        Assert.Equal(8192, config.MemoryMiB);
    }

    [Fact]
    public async Task SaveAsync_ThenLoadAsync_RoundTripsViaFile()
    {
        var config = new VmConfig { Name = "Fedora 40", MemoryMiB = 8192 };
        string path = Path.Combine(Path.GetTempPath(), $"boxwright-test-{Guid.NewGuid():N}.json");
        try
        {
            await VmConfigJson.SaveAsync(path, config);
            VmConfig loaded = await VmConfigJson.LoadAsync(path);

            Assert.Equal("Fedora 40", loaded.Name);
            Assert.Equal(8192, loaded.MemoryMiB);
            Assert.Equal("auto", loaded.Accelerator);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
